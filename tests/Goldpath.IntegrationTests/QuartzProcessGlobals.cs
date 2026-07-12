using Microsoft.Extensions.Logging;
using Xunit;

namespace Goldpath.Tests.Integration;

/// <summary>
/// Quartz's <c>LogProvider</c> is a PROCESS-GLOBAL: whichever host wires Quartz last owns
/// it, and disposing that host leaves the global pointing at a DISPOSED LoggerFactory —
/// the next scheduler instantiation anywhere in the process throws ObjectDisposedException.
/// Harmless in production (one host per process, disposed at exit); lethal in a test
/// assembly that builds several in-process hosts. Two defenses:
/// 1. <see cref="Pin"/> — a never-disposed factory as the provider (module init + after
///    every test-host disposal);
/// 2. the <c>quartz-process-globals</c> collection — classes that build in-process Quartz
///    hosts never overlap, so a mid-flight test cannot watch its provider die.
/// </summary>
public static class QuartzProcessGlobals
{
    private static readonly ILoggerFactory ProcessLifetimeFactory = LoggerFactory.Create(_ => { });

    /// <summary>Points Quartz's global log provider at a factory that outlives every host.</summary>
    public static void Pin()
        => Quartz.Logging.LogContext.SetCurrentLogProvider(ProcessLifetimeFactory);

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void PinAtLoad() => Pin();
}

/// <summary>Serializes every test class that builds an in-process Quartz host.</summary>
[CollectionDefinition("quartz-process-globals")]
public sealed class QuartzProcessGlobalsCollection;
