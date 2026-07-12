var builder = DistributedApplication.CreateBuilder(args);

#if (UsePostgres)
var database = builder.AddPostgres("dbserver").AddDatabase("ordersdb");
#endif
#if (UseSqlServer)
var database = builder.AddSqlServer("dbserver").AddDatabase("ordersdb");
#endif
#if (UseBroker)
var messaging = builder.AddRabbitMQ("messaging");
#endif
// goldpath:features resources — the drift profile is the source of these rows
#if (UseCaching)
var cache = builder.AddRedis("redis");
#endif

builder.AddProject<Projects.GoldpathTemplate_Api>("api")
    .WithReference(database).WaitFor(database)
#if (UseBroker)
    .WithReference(messaging).WaitFor(messaging)
#endif
    // goldpath:features references — the drift profile is the source of these rows
#if (UseCaching)
    .WithReference(cache).WaitFor(cache)
#endif
    .WithHttpHealthCheck("/health/ready");

// goldpath:workers — additional worker projects wire here (goldpath add worker)

builder.Build().Run();
