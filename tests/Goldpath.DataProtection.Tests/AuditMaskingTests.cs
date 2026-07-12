using Mediant.Behaviors.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Goldpath.Tests;

public sealed class AuditMaskingTests : IDisposable
{
    private sealed class Patient : IAuditLogged
    {
        public long Id { get; set; }
        public string? Ward { get; set; }

        [GoldpathPersonalData]
        public string? NationalId { get; set; }

        public string? Room { get; set; }
    }

    private sealed class ClinicDb(DbContextOptions<ClinicDb> options) : DbContext(options)
    {
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<GoldpathAuditLogEntry> AuditLog => Set<GoldpathAuditLogEntry>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddGoldpathAuditLog();
    }

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public void Dispose() => _connection.Dispose();

    private IHost BuildHost(Action<GoldpathAuditTrailOptions>? auditOptions = null, bool withDataProtection = true)
    {
        _connection.Open();
        var builder = Host.CreateApplicationBuilder();
        if (withDataProtection)
        {
            builder.AddGoldpathDataProtection(o => o.Catalog(c => c.Classify<Patient>(x => x.Ward, GoldpathDataClass.Sensitive)));
        }

        builder.AddGoldpathAuditTrail<HostApplicationBuilder, ClinicDb>(auditOptions);
        builder.AddGoldpathData<HostApplicationBuilder, ClinicDb>(o => o.UseSqlite(_connection));
        var host = builder.Build();

        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicDb>().Database.EnsureCreated();
        return host;
    }

    private static async Task<long> AddPatientAsync(IHost host, string nationalId, string ward)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicDb>();
        var patient = new Patient { NationalId = nationalId, Ward = ward, Room = "R-7" };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();
        return patient.Id;
    }

    [Fact]
    public async Task Classified_properties_are_masked_in_change_rows_and_the_rest_stay_full()
    {
        using var host = BuildHost();
        var id = await AddPatientAsync(host, "12345678901", "A1");

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicDb>();
        var log = await db.AuditLog.ToListAsync();

        var nationalId = Assert.Single(log, e => e.PropertyName == nameof(Patient.NationalId));
        Assert.Equal(GoldpathErasingRedactor.Token, nationalId.NewValue);          // annotated → masked

        var ward = Assert.Single(log, e => e.PropertyName == nameof(Patient.Ward));
        Assert.Equal(GoldpathErasingRedactor.Token, ward.NewValue);                // cataloged → masked

        var room = Assert.Single(log, e => e.PropertyName == nameof(Patient.Room));
        Assert.Equal("R-7", room.NewValue);                                   // unclassified → full
        Assert.True(id > 0);
    }

    [Fact]
    public async Task Without_the_module_values_are_recorded_unmasked()
    {
        using var host = BuildHost(withDataProtection: false);
        var id = await AddPatientAsync(host, "12345678901", "A1");

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicDb>();
        var row = await db.AuditLog.SingleAsync(e => e.PropertyName == nameof(Patient.NationalId));

        Assert.Equal("12345678901", row.NewValue);   // absent module = absent masking, by design
    }

    [Fact]
    public async Task NamesOnly_fallback_still_wins_over_masking()
    {
        using var host = BuildHost(o => o.EntityValues = AuditValueMode.NamesOnly);
        var id = await AddPatientAsync(host, "12345678901", "A1");

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicDb>();
        var log = await db.AuditLog.ToListAsync();

        Assert.NotEmpty(log);
        Assert.All(log, e => Assert.Null(e.NewValue));
    }

    [Fact]
    public void Cataloged_names_flow_into_mediant_sensitive_patterns()
    {
        using var host = BuildHost();
        var mediantOptions = host.Services.GetRequiredService<IOptions<AuditBehaviorOptions>>().Value;

        Assert.Contains(nameof(Patient.Ward), mediantOptions.SensitivePatterns);
    }
}
