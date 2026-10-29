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
            UnimetryMetric.AddUpDownCounter("game.players.active", -1);
            UnimetryMetric.RecordGauge("game.queue.depth", 4);
            UnimetryMetric.RecordHistogram("game.match.duration", 12.5, "ms");

            Assert.IsFalse(UnimetryMetric.IsEnabled);
        }

        [Test]
        public void UpDownCounter_AggregatesPositiveAndNegativeDeltas()
        {
            var buffer = new MetricBuffer(100);
            buffer.AddUpDownCounter("game.players.active", 5, "{player}");
            buffer.AddUpDownCounter("game.players.active", -2, "{player}");

            var metric = Find(buffer.Collect(200), "game.players.active");
            Assert.NotNull(metric);
            Assert.AreEqual(MetricKind.UpDownCounter, metric.Kind);
            Assert.AreEqual(3d, metric.Sum);
            Assert.AreEqual("{player}", metric.Unit);
        }

        [Test]
        public void Gauge_ExportsOnlyLatestValueForEachSeries()
        {
            var buffer = new MetricBuffer(100);
            buffer.RecordGauge("game.queue.depth", 4, "{item}");
            buffer.RecordGauge("game.queue.depth", 7, "{item}");

            var metric = Find(buffer.Collect(200), "game.queue.depth");
            Assert.NotNull(metric);
            Assert.AreEqual(MetricKind.Gauge, metric.Kind);
            Assert.AreEqual(7d, metric.Sum);
            Assert.IsTrue(buffer.Collect(300).IsEmpty);
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
        public void GaugeRequeue_PreservesValueRecordedWhileExportWasInFlight()
        {
            var buffer = new MetricBuffer(100);
            buffer.RecordGauge("game.queue.depth", 4, null);
            var failed = buffer.Collect(200);

            buffer.RecordGauge("game.queue.depth", 7, null);
            buffer.Requeue(failed);

            Assert.AreEqual(7d, Find(buffer.Collect(300), "game.queue.depth").Sum);
        }

        [Test]
        public void Attributes_AreOrderIndependentAndCreateSeparateSeries()
        {
            var buffer = new MetricBuffer(100);
            MetricPipeline.Configure(buffer, true);
            UnimetryMetric.AddCounter(
                "game.match.started",
                2,
                null,
                new MetricAttribute("region", "jp"),
                new MetricAttribute("ranked", true));
            UnimetryMetric.AddCounter(
                "game.match.started",
                3,
                null,
                new MetricAttribute("ranked", true),
                new MetricAttribute("region", "jp"));
            UnimetryMetric.AddCounter(
                "game.match.started",
                4,
                null,
                new MetricAttribute("region", "us"),
                new MetricAttribute("ranked", true));

            var batch = buffer.Collect(200);
            Assert.AreEqual(2, batch.Metrics.Count);
            Assert.AreEqual(5d, FindByStringAttribute(batch, "region", "jp").Sum);
            Assert.AreEqual(4d, FindByStringAttribute(batch, "region", "us").Sum);
        }

        [Test]
        public void SeriesLimit_BoundsAttributeCardinality()
        {
            var buffer = new MetricBuffer(100);
            for (var index = 0; index < MetricBuffer.MaxInstruments + 1; index++)
            {
                buffer.AddCounter(
                    "game.match.started",
                    1,
                    null,
                    new[] { new MetricAttribute("match", index) });
            }

            Assert.AreEqual(MetricBuffer.MaxInstruments, buffer.Collect(200).Metrics.Count);
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
        public void BuildMetricsPayload_IncludesAllApplicationMetricKindsAndTypedAttributes()
        {
            var buffer = new MetricBuffer(100);
            buffer.AddCounter(
                "game.match.started",
                3,
                "{match}",
                new[]
                {
                    new MetricAttribute("region", "jp"),
                    new MetricAttribute("ranked", true),
                    new MetricAttribute("mode", 2),
                    new MetricAttribute("sample", 0.5),
                });
            buffer.AddUpDownCounter("game.players.active", -2, "{player}");
            buffer.RecordGauge("game.queue.depth", 7, "{item}");
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
            StringAssert.Contains("\"key\":\"region\",\"value\":{\"stringValue\":\"jp\"}", payload);
            StringAssert.Contains("\"key\":\"ranked\",\"value\":{\"boolValue\":true}", payload);
            StringAssert.Contains("\"key\":\"mode\",\"value\":{\"intValue\":\"2\"}", payload);
            StringAssert.Contains("\"key\":\"sample\",\"value\":{\"doubleValue\":0.5}", payload);
            StringAssert.Contains("\"name\":\"game.players.active\"", payload);
            StringAssert.Contains("\"sum\":{\"aggregationTemporality\":1,\"isMonotonic\":false", payload);
            StringAssert.Contains("\"asDouble\":-2", payload);
            StringAssert.Contains("\"name\":\"game.queue.depth\"", payload);
            StringAssert.Contains("\"gauge\":{\"dataPoints\":[{\"timeUnixNano\":\"200\",\"asDouble\":7", payload);
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

        private static MetricSnapshot FindByStringAttribute(MetricBatch batch, string key, string value)
        {
            for (var metricIndex = 0; metricIndex < batch.Metrics.Count; metricIndex++)
            {
                var metric = batch.Metrics[metricIndex];
                for (var attributeIndex = 0; attributeIndex < metric.AttributeCount; attributeIndex++)
                {
                    var attribute = metric.Attributes[attributeIndex];
                    if (attribute.Kind == EventTagKind.String &&
                        string.Equals(attribute.Key, key, StringComparison.Ordinal) &&
                        string.Equals(attribute.Text, value, StringComparison.Ordinal))
                    {
                        return metric;
                    }
                }
            }

            return null;
        }
    }
}
