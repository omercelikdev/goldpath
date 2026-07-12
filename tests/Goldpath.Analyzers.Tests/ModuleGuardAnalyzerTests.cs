using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Goldpath.Analyzers.Tests;

public class ModuleGuardAnalyzerTests
{
    private const string Stubs = """
        namespace Goldpath
        {
            public interface IAuditLogged { }
            public interface ISoftDeletable { }
            public interface IMultiTenant { }
            public interface IAuditedEntity
            {
                System.DateTimeOffset CreatedAt { get; set; }
                string? CreatedBy { get; set; }
            }
            public interface IEntitySaveContributor { }
            public static class ModelExtensions
            {
                public static void AddGoldpathAuditLog(this Microsoft.EntityFrameworkCore.ModelBuilder b) { }
                public static void ApplyGoldpathSoftDelete(this Microsoft.EntityFrameworkCore.ModelBuilder b) { }
                public static void ApplyGoldpathMultiTenancy(this Microsoft.EntityFrameworkCore.ModelBuilder b, object context) { }
            }
        }
        namespace Mediant.Abstractions
        {
            public interface IQuery<out T> { }
        }
        namespace Mediant.Behaviors.Attributes
        {
            public sealed class IdempotentAttribute : System.Attribute { }
        }
        namespace Microsoft.EntityFrameworkCore
        {
            public abstract class DbContext { }
            public class ModelBuilder { }
        }
        """;

    private static Task Verify<TAnalyzer>(string source, params DiagnosticResult[] expected)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source + "\n" + Stubs,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }

    [Fact]
    public Task GOLDPATH0501_flags_audit_logged_entity_when_model_not_wired()
        => Verify<ModelWiringAnalyzer>("""
            public class {|#0:Loan|} : Goldpath.IAuditLogged { }
            public class AppDb : Microsoft.EntityFrameworkCore.DbContext { }
            """,
            new DiagnosticResult(Descriptors.AuditLogNotWired).WithLocation(0).WithArguments("Loan"));

    [Fact]
    public Task GOLDPATH0501_quiet_when_wired()
        => Verify<ModelWiringAnalyzer>("""
            public class Loan : Goldpath.IAuditLogged { }
            public class AppDb : Microsoft.EntityFrameworkCore.DbContext
            {
                public void Configure(Microsoft.EntityFrameworkCore.ModelBuilder b) => Goldpath.ModelExtensions.AddGoldpathAuditLog(b);
            }
            """);

    [Fact]
    public Task GOLDPATH0501_quiet_in_entity_only_assembly_without_dbcontext()
        => Verify<ModelWiringAnalyzer>("""
            public class Loan : Goldpath.IAuditLogged { }
            public class Cheque : Goldpath.ISoftDeletable { }
            """);

    [Fact]
    public Task GOLDPATH0901_flags_multitenant_entity_when_model_not_wired()
        => Verify<ModelWiringAnalyzer>("""
            public class {|#0:Loan|} : Goldpath.IMultiTenant { }
            public class AppDb : Microsoft.EntityFrameworkCore.DbContext { }
            """,
            new DiagnosticResult(Descriptors.MultiTenancyNotWired).WithLocation(0).WithArguments("Loan"));

    [Fact]
    public Task GOLDPATH0901_quiet_when_wired()
        => Verify<ModelWiringAnalyzer>("""
            public class Loan : Goldpath.IMultiTenant { }
            public class AppDb : Microsoft.EntityFrameworkCore.DbContext
            {
                public void Configure(Microsoft.EntityFrameworkCore.ModelBuilder b) => Goldpath.ModelExtensions.ApplyGoldpathMultiTenancy(b, this);
            }
            """);

    [Fact]
    public Task GOLDPATH0601_flags_soft_deletable_entity_when_filter_not_wired()
        => Verify<ModelWiringAnalyzer>("""
            public class {|#0:Cheque|} : Goldpath.ISoftDeletable { }
            public class AppDb : Microsoft.EntityFrameworkCore.DbContext { }
            """,
            new DiagnosticResult(Descriptors.SoftDeleteNotWired).WithLocation(0).WithArguments("Cheque"));

    [Fact]
    public Task GOLDPATH0502_flags_manual_stamp_write_in_app_code()
        => Verify<ManualStampWriteAnalyzer>("""
            public class Order : Goldpath.IAuditedEntity
            {
                public System.DateTimeOffset CreatedAt { get; set; }
                public string? CreatedBy { get; set; }
            }
            public class Service
            {
                public void M(Order o) => {|#0:o.CreatedBy = "me"|};
            }
            """,
            new DiagnosticResult(Descriptors.ManualStampWrite).WithLocation(0).WithArguments("CreatedBy"));

    [Fact]
    public Task GOLDPATH0502_allows_contributors_and_other_properties()
        => Verify<ManualStampWriteAnalyzer>("""
            public class Order : Goldpath.IAuditedEntity
            {
                public System.DateTimeOffset CreatedAt { get; set; }
                public string? CreatedBy { get; set; }
                public string? Note { get; set; }
            }
            public class Stamper : Goldpath.IEntitySaveContributor
            {
                public void Fill(Order o) => o.CreatedBy = "infra";   // contributor: exempt
            }
            public class Service
            {
                public void M(Order o) => o.Note = "fine";            // not a stamp field
            }
            """);

    [Fact]
    public Task GOLDPATH1003_flags_idempotent_attribute_on_query()
        => Verify<IdempotentOnQueryAnalyzer>("""
            [Mediant.Behaviors.Attributes.Idempotent]
            public record {|#0:GetOrders|} : Mediant.Abstractions.IQuery<string>;
            """,
            new DiagnosticResult(Descriptors.IdempotentOnQuery).WithLocation(0).WithArguments("GetOrders"));

    [Fact]
    public Task GOLDPATH1003_quiet_on_plain_queries()
        => Verify<IdempotentOnQueryAnalyzer>("""
            public record GetOrders : Mediant.Abstractions.IQuery<string>;
            """);
}
