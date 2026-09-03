global using Goldpath;
#if (UseQueue)
// The trigger's own context under ONE name, so the feature rows in Program.cs read the same
// whichever trigger this worker has (a queue worker's tables live next to its inbox).
global using WorkerDbContext = GoldpathWorker.Host.WorkItems.WorkDbContext;
#endif
#if (UseJobs)
// The trigger's own context under ONE name, so the feature rows in Program.cs read the same
// whichever trigger this worker has (a jobs worker's tables live next to its run store).
global using WorkerDbContext = GoldpathWorker.Host.Reports.ReportsDbContext;
#endif
