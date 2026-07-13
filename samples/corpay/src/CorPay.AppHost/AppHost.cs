var builder = DistributedApplication.CreateBuilder(args);

var database = builder.AddPostgres("dbserver").AddDatabase("ordersdb");
var messaging = builder.AddRabbitMQ("messaging");
// goldpath:features resources — the drift profile is the source of these rows
var cache = builder.AddRedis("redis");

builder.AddProject<Projects.CorPay_Api>("api")
    .WithReference(database).WaitFor(database)
    .WithReference(messaging).WaitFor(messaging)
    // goldpath:features references — the drift profile is the source of these rows
    .WithReference(cache).WaitFor(cache)
    .WithHttpHealthCheck("/health/ready");

// goldpath:workers — additional worker projects wire here (goldpath add worker)

builder.AddProject<Projects.CorPay_EodWorker>("eod-worker")
    .WithReference(database).WaitFor(database)
    .WithHttpHealthCheck("/health/ready");

builder.AddProject<Projects.CorPay_PaymentsWorker>("payments-worker")
    .WithReference(database).WaitFor(database)
    .WithReference(messaging).WaitFor(messaging)
    .WithHttpHealthCheck("/health/ready");

builder.Build().Run();
