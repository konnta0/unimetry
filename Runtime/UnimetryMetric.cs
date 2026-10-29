using System.Runtime.CompilerServices;

namespace Unimetry
{
    /// <summary>
    /// Records application gauges, counters, up/down counters, and histograms for OTLP metrics export.
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

            Internal.MetricPipeline.AddCounter(name, delta, unit, null);
        }

        /// <summary>Adds a positive delta to a monotonic counter series identified by attributes.</summary>
        /// <param name="name">Metric name.</param>
        /// <param name="delta">Positive amount to add.</param>
        /// <param name="unit">Optional OpenTelemetry unit.</param>
        /// <param name="attributes">Up to four low-cardinality attributes identifying the series.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddCounter(
            string name,
            double delta,
            string unit,
            params MetricAttribute[] attributes)
        {
            if (MetricEnabled == 0)
            {
                return;
            }

            Internal.MetricPipeline.AddCounter(name, delta, unit, attributes);
        }

        /// <summary>
        /// Adds a positive or negative delta to a non-monotonic sum.
        /// Use this for additive current values such as active players or queued jobs.
        /// </summary>
        /// <param name="name">Metric name.</param>
        /// <param name="delta">Finite amount to add or subtract.</param>
        /// <param name="unit">Optional OpenTelemetry unit.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddUpDownCounter(string name, double delta, string unit = null)
        {
            if (MetricEnabled == 0)
            {
                return;
            }

            Internal.MetricPipeline.AddUpDownCounter(name, delta, unit, null);
        }

        /// <summary>Adds a positive or negative delta to an attributed non-monotonic sum.</summary>
        /// <param name="name">Metric name.</param>
        /// <param name="delta">Finite amount to add or subtract.</param>
        /// <param name="unit">Optional OpenTelemetry unit.</param>
        /// <param name="attributes">Up to four low-cardinality attributes identifying the series.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddUpDownCounter(
            string name,
            double delta,
            string unit,
            params MetricAttribute[] attributes)
        {
            if (MetricEnabled == 0)
            {
                return;
            }

            Internal.MetricPipeline.AddUpDownCounter(name, delta, unit, attributes);
        }

        /// <summary>
        /// Records the latest non-additive value for a gauge series.
        /// Only the last value recorded before a flush is exported.
        /// </summary>
        /// <param name="name">Metric name.</param>
        /// <param name="value">Finite current value.</param>
        /// <param name="unit">Optional OpenTelemetry unit.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RecordGauge(string name, double value, string unit = null)
        {
            if (MetricEnabled == 0)
            {
                return;
            }

            Internal.MetricPipeline.RecordGauge(name, value, unit, null);
        }

        /// <summary>Records the latest value for an attributed gauge series.</summary>
        /// <param name="name">Metric name.</param>
        /// <param name="value">Finite current value.</param>
        /// <param name="unit">Optional OpenTelemetry unit.</param>
        /// <param name="attributes">Up to four low-cardinality attributes identifying the series.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RecordGauge(
            string name,
            double value,
            string unit,
            params MetricAttribute[] attributes)
        {
            if (MetricEnabled == 0)
            {
                return;
            }

            Internal.MetricPipeline.RecordGauge(name, value, unit, attributes);
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

            Internal.MetricPipeline.RecordHistogram(name, value, unit, null);
        }

        /// <summary>Records one finite value into an attributed histogram series.</summary>
        /// <param name="name">Metric name.</param>
        /// <param name="value">Observed value.</param>
        /// <param name="unit">Optional OpenTelemetry unit.</param>
        /// <param name="attributes">Up to four low-cardinality attributes identifying the series.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RecordHistogram(
            string name,
            double value,
            string unit,
            params MetricAttribute[] attributes)
        {
            if (MetricEnabled == 0)
            {
                return;
            }

            Internal.MetricPipeline.RecordHistogram(name, value, unit, attributes);
        }
    }
}
