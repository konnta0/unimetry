using System.Runtime.CompilerServices;

namespace Unimetry
{
    /// <summary>
    /// Records application counters and histograms for OTLP metrics export.
    /// Values are aggregated between flushes and exported with delta temporality.
    /// </summary>
    public static class UnimetryMetric
    {
        /// <summary>
        /// Non-zero while metric capture is active.
        /// </summary>
        internal static volatile int MetricEnabled;

        /// <summary>
        /// Gets whether application metric capture is currently recording.
        /// </summary>
        public static bool IsEnabled
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => MetricEnabled != 0;
        }

        /// <summary>
        /// Adds a positive delta to a monotonic counter.
        /// The value is ignored when metrics are disabled, the name is empty, or the delta is not positive and finite.
        /// </summary>
        /// <param name="name">Metric name, for example <c>game.match.started</c>.</param>
        /// <param name="delta">Positive amount to add.</param>
        /// <param name="unit">Optional OpenTelemetry unit, for example <c>{match}</c> or <c>By</c>.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddCounter(string name, double delta = 1d, string unit = null)
        {
            if (MetricEnabled == 0)
            {
                return;
            }

            Internal.MetricPipeline.AddCounter(name, delta, unit);
        }

        /// <summary>
        /// Records one finite value into a histogram.
        /// The value is ignored when metrics are disabled or the name is empty.
        /// </summary>
        /// <param name="name">Metric name, for example <c>game.match.duration</c>.</param>
        /// <param name="value">Observed value.</param>
        /// <param name="unit">Optional OpenTelemetry unit, for example <c>ms</c>, <c>s</c>, or <c>By</c>.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RecordHistogram(string name, double value, string unit = null)
        {
            if (MetricEnabled == 0)
            {
                return;
            }

            Internal.MetricPipeline.RecordHistogram(name, value, unit);
        }
    }
}
