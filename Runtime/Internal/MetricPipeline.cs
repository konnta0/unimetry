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

        public static void AddCounter(string name, double delta, string unit)
        {
            buffer?.AddCounter(name, delta, unit);
        }

        public static void RecordHistogram(string name, double value, string unit)
        {
            buffer?.RecordHistogram(name, value, unit);
        }
    }
}
