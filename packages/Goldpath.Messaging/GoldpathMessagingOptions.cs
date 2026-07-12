namespace Goldpath;

/// <summary>
/// Tuning surface of the message-path floor. Bound from configuration section
/// <c>Goldpath:Messaging</c>, then the code callback applies.
/// </summary>
public sealed class GoldpathMessagingOptions
{
    /// <summary>Consumer resilience defaults (RFC goldpath-messaging D4). Messages are never silently dropped.</summary>
    public RetryOptions Retry { get; } = new();

    /// <summary>Retry tuning: immediate attempts, then delayed redelivery, then the error queue.</summary>
    public sealed class RetryOptions
    {
        /// <summary>Immediate in-process retry attempts before redelivery.</summary>
        public int ImmediateCount { get; set; } = 3;

        /// <summary>
        /// Delayed redelivery intervals (requires a transport/scheduler that supports delay);
        /// an empty list disables the redelivery stage.
        /// </summary>
        public IList<TimeSpan> RedeliveryIntervals { get; } =
            [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30)];
    }
}
