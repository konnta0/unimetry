using System;
using NUnit.Framework;
using Unimetry.Internal;

namespace Unimetry.Tests
{
    public sealed class MetricTests
    {
        [TearDown]
        public void TearDown()
        {
            MetricPipeline.Configure(null, false);
        }

        [Test]
        public void PublicApi_IsNoOpWhileMetricsAreDisabled()
        {
            UnimetryMetric.AddCounter("game.match.started");
            UnimetryMetric.RecordHistogram("game.match.duration", 12.5, "ms");

            Assert.IsFalse(UnimetryMetric.IsEnabled);
        }

        [Test]
        public void Counter_AggregatesPositiveDeltasAndResetsAfterCollect()
        {
            var buffer = new MetricBuffer(100);
            MetricPipeline.Configure(buffer, true);

            UnimetryMetric.AddCounter("game.match.started", 2, "{match}");
            UnimetryMetric.AddCounter("game.match.started", 3, "{match}");
            UnimetryMetric.AddCounter("game.match.started", 0, "{match}");
            UnimetryMetric.AddCounter("game.match.started", -1, "{match}");

            var batch = buffer.Collect(200);
            var metric = Find(batch, "game.match.started");
            Assert.NotNull(metric);
            Assert.AreEqual(MetricKind.Counter, metric.Kind);
            Assert.AreEqual(5d, metric.Sum);
            Assert.AreEqual("{match}", metric.Unit);
            Assert.AreEqual(100, batch.StartTimeUnixNano);
            Assert.AreEqual(200, batch.TimeUnixNano);
            Assert.IsTrue(buffer.Collect(300).IsEmpty);
        }

        [Test]
        public void Histogram_AggregatesCountSumRangeAndBuckets()
        {
            var buffer = new MetricBuffer(100);
            buffer.RecordHistogram("game.match.duration", -1, "ms");
            buffer.RecordHistogram("game.match.duration", 0, "ms");
            buffer.RecordHistogram("game.match.duration", 5, "ms");
            buffer.RecordHistogram("game.match.duration", 6, "ms");
            buffer.RecordHistogram("game.match.duration", 20000, "ms");

            var metric = Find(buffer.Collect(200), "game.match.duration");
            Assert.NotNull(metric);
            Assert.AreEqual(MetricKind.Histogram, metric.Kind);
            Assert.AreEqual(5, metric.Count);
            Assert.AreEqual(20010d, metric.Sum);
            Assert.AreEqual(-1d, metric.Min);
            Assert.AreEqual(20000d, metric.Max);
            Assert.AreEqual(2, metric.BucketCounts[0]);
            Assert.AreEqual(1, metric.BucketCounts[1]);
            Assert.AreEqual(1, metric.BucketCounts[2]);
            Assert.AreEqual(1, metric.BucketCounts[metric.BucketCounts.Length - 1]);
        }

        [Test]
        public void Requeue_MergesFailedBatchWithMeasurementsRecordedDuringExport()
        {
            var buffer = new MetricBuffer(100);
            buffer.AddCounter("game.retry", 2, null);
            buffer.RecordHistogram("game.latency", 10, "ms");
            var failed = buffer.Collect(200);

            buffer.AddCounter("game.retry", 3, null);
            buffer.RecordHistogram("game.latency", 20, "ms");
            buffer.Requeue(failed);

            var retried = buffer.Collect(300);
            Assert.AreEqual(100, retried.StartTimeUnixNano);
            Assert.AreEqual(5d, Find(retried, "game.retry").Sum);
            var histogram = Find(retried, "game.latency");
            Assert.AreEqual(2, histogram.Count);
            Assert.AreEqual(30d, histogram.Sum);
            Assert.AreEqual(10d, histogram.Min);
            Assert.AreEqual(20d, histogram.Max);
        }

        [Test]
        public void MetricName_CannotChangeInstrumentKind()
        {
            var buffer = new MetricBuffer(100);
            buffer.AddCounter("game.conflict", 1, null);
            buffer.RecordHistogram("game.conflict", 10, null);

            var metric = Find(buffer.Collect(200), "game.conflict");
            Assert.AreEqual(MetricKind.Counter, metric.Kind);
            Assert.AreEqual(1d, metric.Sum);
        }

        [Test]
        public void BuildMetricsPayload_IncludesCounterAndHistogram()
        {
            var buffer = new MetricBuffer(100);
            buffer.AddCounter("game.match.started", 3, "{match}");
            buffer.RecordHistogram("game.match.duration", 25, "ms");
            buffer.RecordHistogram("game.match.duration", 75, "ms");
            var options = new UnimetryOptions
            {
                Endpoint = "http://127.0.0.1:9",
                ServiceName = "unimetry-metric-test",
            };

            var payload = OtlpJsonWriter.BuildMetricsPayload(
                null,
                options,
                200,
                double.NaN,
                -1,
                double.NaN,
                buffer.Collect(200));

            StringAssert.Contains("\"name\":\"game.match.started\"", payload);
            StringAssert.Contains("\"unit\":\"{match}\"", payload);
            StringAssert.Contains("\"sum\":{\"aggregationTemporality\":1,\"isMonotonic\":true", payload);
            StringAssert.Contains("\"startTimeUnixNano\":\"100\"", payload);
            StringAssert.Contains("\"asDouble\":3", payload);
            StringAssert.Contains("\"name\":\"game.match.duration\"", payload);
            StringAssert.Contains("\"histogram\":{\"aggregationTemporality\":1", payload);
            StringAssert.Contains("\"count\":\"2\"", payload);
            StringAssert.Contains("\"sum\":100", payload);
            StringAssert.Contains("\"min\":25", payload);
            StringAssert.Contains("\"max\":75", payload);
            StringAssert.Contains("\"explicitBounds\":[0,5,10,25,50,75,100", payload);
        }

        private static MetricSnapshot Find(MetricBatch batch, string name)
        {
            if (batch?.Metrics == null)
            {
                return null;
            }

            for (var index = 0; index < batch.Metrics.Count; index++)
            {
                if (string.Equals(batch.Metrics[index].Name, name, StringComparison.Ordinal))
                {
                    return batch.Metrics[index];
                }
            }

            return null;
        }
    }
}
