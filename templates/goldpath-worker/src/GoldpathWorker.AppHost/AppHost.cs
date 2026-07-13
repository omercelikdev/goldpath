var builder = DistributedApplication.CreateBuilder(args);

#if (UsePostgres)
var database = builder.AddPostgres("dbserver").AddDatabase("workdb");
#endif
#if (UseSqlServer)
var database = builder.AddSqlServer("dbserver").AddDatabase("workdb");
#endif
#if (UseQueue)
var messaging = builder.AddRabbitMQ("messaging");

builder.AddProject<Projects.GoldpathWkrSafe_Host>("worker")
    .WithReference(database).WaitFor(database)
    .WithReference(messaging).WaitFor(messaging)
    .WithHttpHealthCheck("/health/ready");
#endif
#if (UseJobs)
builder.AddProject<Projects.GoldpathWkrSafe_Host>("worker")
    .WithReference(database).WaitFor(database)
    .WithHttpHealthCheck("/health/ready");
#endif
#if (UseSchedule)
builder.AddProject<Projects.GoldpathWkrSafe_Host>("worker")
    // Dev loop: fast ticks so the dashboard (and the smoke test) shows life immediately.
    // The real cadence is deployment configuration (Worker:Interval).
    .WithEnvironment("Worker__Interval", "00:00:01")
    .WithHttpHealthCheck("/health/ready");
#endif

builder.Build().Run();
