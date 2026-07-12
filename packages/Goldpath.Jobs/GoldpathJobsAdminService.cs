using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Quartz.Impl.Matchers;

namespace Goldpath;

/// <summary>One registered job as the store sees it, with its live triggers.</summary>
public sealed record GoldpathJobInfo(string Name, string? Description, bool RequestsRecovery, IReadOnlyList<GoldpathTriggerInfo> Triggers);

/// <summary>One trigger's live state.</summary>
public sealed record GoldpathTriggerInfo(string Name, string State, string? CronExpression, string? CalendarName, DateTimeOffset? NextFireAt, DateTimeOffset? PreviousFireAt);

/// <summary>A run with its chunk breakdown and open repair items.</summary>
public sealed record GoldpathRunDetail(GoldpathJobRun Run, IReadOnlyDictionary<string, int> ChunksByStatus, IReadOnlyList<GoldpathJobItemFailure> OpenFailures);

/// <summary>Verb outcome — failures carry the reason, never a silent 200.</summary>
public sealed record GoldpathAdminResult(bool Ok, string Message);

/// <summary>A calendar over the wire: exactly one shape per type.</summary>
public sealed record GoldpathCalendarSpec(string Type, string? Description, IReadOnlyList<DateTime>? ExcludedDates, IReadOnlyList<DayOfWeek>? ExcludedDays, string? CronExpression);

/// <summary>A named calendar and the triggers currently riding it.</summary>
public sealed record GoldpathCalendarInfo(string Name, string? Description, IReadOnlyList<string> UsedByTriggers);

/// <summary>
/// The admin verbs behind <c>MapGoldpathJobsAdmin</c> (§7.1: the API is the contract, every
/// screen is its skin, agents script the same surface). EVERY mutating verb writes a
/// <see cref="GoldpathJobAdminAudit"/> row — iron rule 2 lives here, not in each caller.
/// </summary>
public sealed class GoldpathJobsAdminService<TContext>
    where TContext : DbContext
{
    private readonly IGoldpathJobsFleetRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _time;

    /// <summary>Registered by AddGoldpathJobs/AddGoldpathJobsManagement.</summary>
    public GoldpathJobsAdminService(IGoldpathJobsFleetRegistry registry, IServiceScopeFactory scopeFactory, TimeProvider time)
    {
        _registry = registry;
        _scopeFactory = scopeFactory;
        _time = time;
    }

    /// <summary>Every fleet in the store with its live cluster nodes.</summary>
    public Task<IReadOnlyList<GoldpathFleetInfo>> GetFleetsAsync(CancellationToken ct)
        => _registry.GetFleetsAsync(ct);

    /// <summary>The fleet's jobs with their live trigger states.</summary>
    public async Task<IReadOnlyList<GoldpathJobInfo>> GetJobsAsync(string fleet, CancellationToken ct)
    {
        var scheduler = await _registry.GetSchedulerAsync(fleet, ct);
        var keys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(GoldpathJobsExtensions.JobGroup), ct);
        var jobs = new List<GoldpathJobInfo>();
        foreach (var key in keys.OrderBy(k => k.Name, StringComparer.Ordinal))
        {
            var detail = await scheduler.GetJobDetail(key, ct);
            var triggers = new List<GoldpathTriggerInfo>();
            foreach (var trigger in await scheduler.GetTriggersOfJob(key, ct))
            {
                var state = await scheduler.GetTriggerState(trigger.Key, ct);
                triggers.Add(new GoldpathTriggerInfo(
                    trigger.Key.Name,
                    state.ToString(),
                    (trigger as ICronTrigger)?.CronExpressionString,
                    trigger.CalendarName,
                    trigger.GetNextFireTimeUtc(),
                    trigger.GetPreviousFireTimeUtc()));
            }

            jobs.Add(new GoldpathJobInfo(key.Name, detail?.Description, detail?.RequestsRecovery ?? false, triggers));
        }

        return jobs;
    }

    /// <summary>Latest runs of a fleet (optionally one job), newest first.</summary>
    public async Task<IReadOnlyList<GoldpathJobRun>> GetRunsAsync(string fleet, string? job, int take, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        return await db.Set<GoldpathJobRun>().AsNoTracking()
            .Where(r => r.SchedulerName == fleet && (job == null || r.JobName == job))
            .OrderByDescending(r => r.StartedAt)
            .Take(AdminPaging.Clamp(take))
            .ToListAsync(ct);
    }

    /// <summary>One run with chunk breakdown and its OPEN repair queue.</summary>
    public async Task<GoldpathRunDetail?> GetRunAsync(Guid runId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var run = await db.Set<GoldpathJobRun>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null)
        {
            return null;
        }

        var chunks = await db.Set<GoldpathJobRunChunk>().AsNoTracking()
            .Where(c => c.RunId == runId)
            .GroupBy(c => c.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, StringComparer.Ordinal, ct);
        var failures = await db.Set<GoldpathJobItemFailure>().AsNoTracking()
            .Where(f => f.RunId == runId && f.RedrivenAt == null)
            .OrderBy(f => f.Id)
            .Take(200)
            .ToListAsync(ct);
        return new GoldpathRunDetail(run, chunks, failures);
    }

    /// <summary>Fires the job now; dry-run reports what WOULD happen without firing.</summary>
    public async Task<GoldpathAdminResult> TriggerAsync(string fleet, string job, bool dryRun, string actor, CancellationToken ct)
    {
        var scheduler = await _registry.GetSchedulerAsync(fleet, ct);
        var key = new JobKey(job, GoldpathJobsExtensions.JobGroup);
        if (!await scheduler.CheckExists(key, ct))
        {
            return new GoldpathAdminResult(false, $"no job '{job}' in fleet '{fleet}'");
        }

        if (dryRun)
        {
            var triggers = await scheduler.GetTriggersOfJob(key, ct);
            var next = triggers.Select(t => t.GetNextFireTimeUtc()).Where(t => t.HasValue).Min();
            return new GoldpathAdminResult(true, next is { } n
                ? $"dry-run: would fire now; next scheduled fire {n:O}"
                : "dry-run: would fire now; no scheduled trigger (ad-hoc job)");
        }

        await scheduler.TriggerJob(key, StampTraceParent(new JobDataMap()), ct);
        await AuditAsync(actor, "trigger", fleet, job, null, ct);
        return new GoldpathAdminResult(true, "triggered");
    }

    /// <summary>Pauses every trigger of one job (cluster-wide, via the store).</summary>
    public async Task<GoldpathAdminResult> PauseJobAsync(string fleet, string job, string actor, CancellationToken ct)
    {
        var scheduler = await _registry.GetSchedulerAsync(fleet, ct);
        var key = new JobKey(job, GoldpathJobsExtensions.JobGroup);
        if (!await scheduler.CheckExists(key, ct))
        {
            return new GoldpathAdminResult(false, $"no job '{job}' in fleet '{fleet}'");
        }

        await scheduler.PauseJob(key, ct);
        await AuditAsync(actor, "pause", fleet, job, null, ct);
        return new GoldpathAdminResult(true, "paused");
    }

    /// <summary>Resumes a paused job.</summary>
    public async Task<GoldpathAdminResult> ResumeJobAsync(string fleet, string job, string actor, CancellationToken ct)
    {
        var scheduler = await _registry.GetSchedulerAsync(fleet, ct);
        var key = new JobKey(job, GoldpathJobsExtensions.JobGroup);
        if (!await scheduler.CheckExists(key, ct))
        {
            return new GoldpathAdminResult(false, $"no job '{job}' in fleet '{fleet}'");
        }

        await scheduler.ResumeJob(key, ct);
        await AuditAsync(actor, "resume", fleet, job, null, ct);
        return new GoldpathAdminResult(true, "resumed");
    }

    /// <summary>Fleet-wide stop/go: pauses or resumes EVERY trigger in the fleet (audited).</summary>
    public async Task<GoldpathAdminResult> SetFleetPausedAsync(string fleet, bool paused, string actor, CancellationToken ct)
    {
        var scheduler = await _registry.GetSchedulerAsync(fleet, ct);
        if (paused)
        {
            await scheduler.PauseAll(ct);
        }
        else
        {
            await scheduler.ResumeAll(ct);
        }

        await AuditAsync(actor, paused ? "pause-all" : "resume-all", fleet, "*", null, ct);
        return new GoldpathAdminResult(true, paused ? "fleet paused" : "fleet resumed");
    }

    /// <summary>
    /// Runtime schedule override (RFC D7): the DEFINITION stays in code, the CRON is an
    /// audited ops decision — "run at 03:00 tonight" never waits for an MR.
    /// </summary>
    public async Task<GoldpathAdminResult> RescheduleAsync(string fleet, string job, string cron, string? timeZoneId, string actor, CancellationToken ct)
    {
        if (!CronExpression.IsValidExpression(cron))
        {
            return new GoldpathAdminResult(false, $"'{cron}' is not a valid Quartz cron expression");
        }

        var scheduler = await _registry.GetSchedulerAsync(fleet, ct);
        var jobKey = new JobKey(job, GoldpathJobsExtensions.JobGroup);
        if (!await scheduler.CheckExists(jobKey, ct))
        {
            return new GoldpathAdminResult(false, $"no job '{job}' in fleet '{fleet}'");
        }

        var triggerKey = new TriggerKey($"{job}-cron", GoldpathJobsExtensions.JobGroup);
        var existing = await scheduler.GetTrigger(triggerKey, ct);
        var builder = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .WithCronSchedule(cron, s =>
            {
                if (timeZoneId is not null)
                {
                    s.InTimeZone(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
                }
            });
        if (existing?.CalendarName is { } calendar)
        {
            builder = builder.ModifiedByCalendar(calendar);
        }

        var trigger = builder.Build();
        if (existing is null)
        {
            await scheduler.ScheduleJob(trigger, ct);
        }
        else
        {
            await scheduler.RescheduleJob(triggerKey, trigger, ct);
        }

        var oldCron = (existing as ICronTrigger)?.CronExpressionString ?? "<none>";
        await AuditAsync(actor, "reschedule", fleet, job, $"{oldCron} -> {cron}", ct);
        return new GoldpathAdminResult(true, $"rescheduled: {oldCron} -> {cron}");
    }

    /// <summary>Re-fires a TERMINAL run's job; refuses while a run is open (never a double-run).</summary>
    public async Task<GoldpathAdminResult> RerunAsync(Guid runId, string actor, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var run = await db.Set<GoldpathJobRun>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null)
        {
            return new GoldpathAdminResult(false, "no such run");
        }

        if (run.Status == GoldpathJobRunStatus.Running)
        {
            return new GoldpathAdminResult(false, "the run is still open — resume happens on the next fire, not through rerun");
        }

        var openRun = await db.Set<GoldpathJobRun>().AsNoTracking().AnyAsync(r =>
            r.SchedulerName == run.SchedulerName && r.JobName == run.JobName && r.Status == GoldpathJobRunStatus.Running, ct);
        if (openRun)
        {
            return new GoldpathAdminResult(false, "another run of this job is open — rerun would double-run");
        }

        var scheduler = await _registry.GetSchedulerAsync(run.SchedulerName, ct);
        await scheduler.TriggerJob(new JobKey(run.JobName, GoldpathJobsExtensions.JobGroup), ct);
        await AuditAsync(actor, "rerun", run.SchedulerName, run.JobName, $"after run {runId}", ct);
        return new GoldpathAdminResult(true, "triggered a fresh run");
    }

    /// <summary>
    /// Redrives OPEN repair items through the job's <see cref="IGoldpathItemReplay"/> hook on an
    /// EXECUTOR (the type lives there, not here): a one-off fire carries the item keys in
    /// its data map. Items without the hook fail loudly on the executor, never silently.
    /// </summary>
    public async Task<GoldpathAdminResult> ReplayItemsAsync(Guid runId, string actor, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var run = await db.Set<GoldpathJobRun>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null)
        {
            return new GoldpathAdminResult(false, "no such run");
        }

        var openItems = await db.Set<GoldpathJobItemFailure>().AsNoTracking()
            .Where(f => f.RunId == runId && f.RedrivenAt == null)
            .Select(f => f.ItemKey)
            .Take(500)
            .ToListAsync(ct);
        if (openItems.Count == 0)
        {
            return new GoldpathAdminResult(false, "the repair queue of this run is empty");
        }

        var scheduler = await _registry.GetSchedulerAsync(run.SchedulerName, ct);
        var data = StampTraceParent(new JobDataMap
        {
            [GoldpathJobsExtensions.ReplayRunKey] = runId.ToString(),
        });
        await scheduler.TriggerJob(new JobKey(run.JobName, GoldpathJobsExtensions.JobGroup), data, ct);
        await AuditAsync(actor, "replay-items", run.SchedulerName, run.JobName, $"{openItems.Count} items of run {runId}", ct);
        return new GoldpathAdminResult(true, $"replay fire queued for {openItems.Count} open items");
    }

    /// <summary>
    /// Stamps the caller's W3C traceparent into the fire's data map — the only vehicle
    /// that crosses the Quartz store, so the run span can link back to the request.
    /// </summary>
    private static JobDataMap StampTraceParent(JobDataMap data)
    {
        if (Activity.Current?.Id is { } traceParent)
        {
            data[GoldpathJobsExtensions.TraceParentKey] = traceParent;
        }

        return data;
    }

    /// <summary>The fleet's calendars with the triggers riding them.</summary>
    public async Task<IReadOnlyList<GoldpathCalendarInfo>> GetCalendarsAsync(string fleet, CancellationToken ct)
    {
        var scheduler = await _registry.GetSchedulerAsync(fleet, ct);
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var usage = await db.Set<QrtzTrigger>().AsNoTracking()
            .Where(t => t.SchedName == fleet && t.CalendarName != null)
            .Select(t => new { t.CalendarName, t.TriggerName })
            .ToListAsync(ct);

        var calendars = new List<GoldpathCalendarInfo>();
        foreach (var name in await scheduler.GetCalendarNames(ct))
        {
            var calendar = await scheduler.GetCalendar(name, ct);
            calendars.Add(new GoldpathCalendarInfo(name, calendar?.Description,
                usage.Where(u => u.CalendarName == name).Select(u => u.TriggerName).ToList()));
        }

        return calendars;
    }

    /// <summary>Creates or replaces a calendar (holiday | weekly | cron), updating riding triggers.</summary>
    public async Task<GoldpathAdminResult> PutCalendarAsync(string fleet, string name, GoldpathCalendarSpec spec, string actor, CancellationToken ct)
    {
        Quartz.ICalendar calendar;
        switch (spec.Type.ToUpperInvariant())
        {
            case "HOLIDAY":
                var holiday = new Quartz.Impl.Calendar.HolidayCalendar { Description = spec.Description };
                foreach (var date in spec.ExcludedDates ?? [])
                {
                    holiday.AddExcludedDate(date.Date);
                }

                calendar = holiday;
                break;
            case "WEEKLY":
                var weekly = new Quartz.Impl.Calendar.WeeklyCalendar { Description = spec.Description };
                foreach (var day in spec.ExcludedDays ?? [])
                {
                    weekly.SetDayExcluded(day, true);
                }

                calendar = weekly;
                break;
            case "CRON" when spec.CronExpression is not null && CronExpression.IsValidExpression(spec.CronExpression):
                calendar = new Quartz.Impl.Calendar.CronCalendar(spec.CronExpression) { Description = spec.Description };
                break;
            default:
                return new GoldpathAdminResult(false, "calendar type must be holiday|weekly|cron (cron needs a valid expression)");
        }

        var scheduler = await _registry.GetSchedulerAsync(fleet, ct);
        await scheduler.AddCalendar(name, calendar, replace: true, updateTriggers: true, ct);
        await AuditAsync(actor, "calendar-put", fleet, name, spec.Type, ct);
        return new GoldpathAdminResult(true, "calendar stored");
    }

    /// <summary>Deletes a calendar (refused by the store while triggers ride it).</summary>
    public async Task<GoldpathAdminResult> DeleteCalendarAsync(string fleet, string name, string actor, CancellationToken ct)
    {
        var scheduler = await _registry.GetSchedulerAsync(fleet, ct);
        try
        {
            if (!await scheduler.DeleteCalendar(name, ct))
            {
                return new GoldpathAdminResult(false, $"no calendar '{name}' in fleet '{fleet}'");
            }
        }
        catch (SchedulerException exception)
        {
            return new GoldpathAdminResult(false, exception.Message);   // e.g. "calendar is referenced by a trigger"
        }

        await AuditAsync(actor, "calendar-delete", fleet, name, null, ct);
        return new GoldpathAdminResult(true, "calendar deleted");
    }

    /// <summary>The audit trail of admin verbs, newest first.</summary>
    public async Task<IReadOnlyList<GoldpathJobAdminAudit>> GetAuditAsync(int take, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        return await db.Set<GoldpathJobAdminAudit>().AsNoTracking()
            .OrderByDescending(a => a.At)
            .Take(AdminPaging.Clamp(take))
            .ToListAsync(ct);
    }

    private async Task AuditAsync(string actor, string action, string fleet, string target, string? detail, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        db.Add(new GoldpathJobAdminAudit
        {
            At = _time.GetUtcNow(),
            Actor = actor,
            Action = action,
            Fleet = fleet,
            Target = target,
            Detail = detail,
        });
        await db.SaveChangesAsync(ct);
    }
}
