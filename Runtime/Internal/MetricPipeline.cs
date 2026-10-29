namespace Unimetry.Internal
{
    internal static class MetricPipeline
    {
        private static volatile MetricBuffer buffer;

        public static void Configure(MetricBuffer nextBuffer, bool captureMetrics)
        {
            buffer = nextBuffer;
            UnimetryMetric.MetricEnabled = captureMetrics && nextBuffer != null ? 1 : 0;
        }

        public static void AddCounter(string name, double delta, string unit, MetricAttribute[] attributes)
        {
            buffer?.AddCounter(name, delta, unit, attributes);
        }

        public static void AddUpDownCounter(string name, double delta, string unit, MetricAttribute[] attributes)
        {
            buffer?.AddUpDownCounter(name, delta, unit, attributes);
        }

        public static void RecordGauge(string name, double value, string unit, MetricAttribute[] attributes)
        {
            buffer?.RecordGauge(name, value, unit, attributes);
        }

        public static void RecordHistogram(string name, double value, string unit, MetricAttribute[] attributes)
        {
            buffer?.RecordHistogram(name, value, unit, attributes);
        }
    }
}
