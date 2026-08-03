var builder = DistributedApplication.CreateBuilder(args);

var database = builder.AddPostgres("dbserver").AddDatabase("ordersdb");
var messaging = builder.AddRabbitMQ("messaging");
// goldpath:features resources — the drift profile is the source of these rows
var cache = builder.AddRedis("redis");

var api = builder.AddProject<Projects.CorPay_Api>("api")
    .WithReference(database).WaitFor(database)
    .WithReference(messaging).WaitFor(messaging)
    // goldpath:features references — the drift profile is the source of these rows
    .WithReference(cache).WaitFor(cache)
    .WithHttpHealthCheck("/health/ready");

// goldpath:workers — additional worker projects wire here (goldpath add worker)

// The workers wait for the API, not just the database CONTAINER: the API's context owns
// the shared tables' DDL (migrations D3 — one migration owner), and the EOD worker's
// Quartz store validates that schema at startup. Racing the migrator was T12: a worker
// whose /health/ready could never answer — invisible until the console smoke asked.
builder.AddProject<Projects.CorPay_EodWorker>("eod-worker")
    .WithReference(database).WaitFor(database)
    .WaitFor(api)
    .WithHttpHealthCheck("/health/ready");

builder.AddProject<Projects.CorPay_PaymentsWorker>("payments-worker")
    .WithReference(database).WaitFor(database)
    .WithReference(messaging).WaitFor(messaging)
    .WaitFor(api)
    .WithHttpHealthCheck("/health/ready");

builder.Build().Run();
