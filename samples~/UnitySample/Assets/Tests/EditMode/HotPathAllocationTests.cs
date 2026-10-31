using System;
using System.Globalization;
using NUnit.Framework;
using Unimetry.Internal;

namespace Unimetry.Tests
{
    public sealed class HotPathAllocationTests
    {
        [Test]
        public void Capture_DoesNotAllocateWhenTheResourceIsUnchanged()
        {
            var state = new ResourceState(new UnimetryOptions
            {
                ServiceName = "game",
                ServiceVersion = "1.0.0",
                DeploymentEnvironment = "development",
            });
            state.Set("user.id", "account-a");
            var captured = state.Capture();

            var allocated = AllocatedBytes(() => state.Capture(), 64);

            Assert.AreEqual(0, allocated, "steady-state resource capture bytes");
            state.Set("user.id", "account-b");
            Assert.AreEqual("account-a", FindAttribute(captured, "user.id"));
            Assert.AreEqual("account-b", FindAttribute(state.Capture(), "user.id"));
        }

        [Test]
        public void AddCounter_DoesNotAllocateForAnExistingSeries()
        {
            var buffer = new MetricBuffer(1);
            buffer.AddCounter("game.match.started", 1d, "{match}");

            var allocated = AllocatedBytes(() => buffer.AddCounter("game.match.started", 1d, "{match}"), 64);

            Assert.AreEqual(0, allocated, "steady-state counter bytes");
            Assert.AreEqual(81d, Find(buffer.Collect(2), "game.match.started").Sum);
        }

        [Test]
        public void AddCounter_DoesNotAllocateForAnExistingAttributedSeries()
        {
            var buffer = new MetricBuffer(1);
            var attributes = new[]
            {
                new MetricAttribute("map", "forest"),
                new MetricAttribute("mode", "ranked"),
            };
            buffer.AddCounter("game.match.started", 1d, "{match}", attributes);

            var allocated = AllocatedBytes(
                () => buffer.AddCounter("game.match.started", 1d, "{match}", attributes),
                64);

            Assert.AreEqual(0, allocated, "steady-state attributed counter bytes");
            Assert.AreEqual(81d, Find(buffer.Collect(2), "game.match.started").Sum);
        }

        [Test]
        public void RecordHistogram_DoesNotAllocateForAnExistingSeries()
        {
            var buffer = new MetricBuffer(1);
            buffer.RecordHistogram("game.frame.ms", 16.6d, "ms");

            var allocated = AllocatedBytes(() => buffer.RecordHistogram("game.frame.ms", 16.6d, "ms"), 64);

            Assert.AreEqual(0, allocated, "steady-state histogram bytes");
            Assert.AreEqual(81, Find(buffer.Collect(2), "game.frame.ms").Count);
        }

        [Test]
        public void TrimmedMetricNames_ShareOneSeries()
        {
            var buffer = new MetricBuffer(1);
            buffer.AddCounter("  game.match.started  ", 2d, " {match} ");
            buffer.AddCounter("game.match.started", 3d, "{match}");

            var batch = buffer.Collect(2);
            Assert.AreEqual(1, batch.Metrics.Count);
            Assert.AreEqual(5d, batch.Metrics[0].Sum);
            Assert.AreEqual("{match}", batch.Metrics[0].Unit);
        }

        [Test]
        public void BuildLogsPayload_FormatsIntegersWithInvariantDigits()
        {
            var options = new UnimetryOptions { ServiceName = "game" };
            var events = new EventRecord[1];
            events[0].Name = "player.jump";
            events[0].StartUnixNano = long.MaxValue;
            events[0].EndUnixNano = long.MinValue;
            events[0].SeverityNumber = (int)UnimetrySeverity.Info;
            events[0].Resource = ResourceSnapshot.FromOptions(options);

            var payload = OtlpJsonWriter.BuildLogsPayload(Array.Empty<PendingExport>(), events, 1, options);

            StringAssert.Contains(long.MaxValue.ToString(CultureInfo.InvariantCulture), payload);
            StringAssert.Contains(long.MinValue.ToString(CultureInfo.InvariantCulture), payload);
        }

        [Test]
        public void BuildMetricsPayload_FormatsDoublesWithRoundTripInvariant()
        {
            var options = new UnimetryOptions
            {
                ServiceName = "game",
                CaptureBuiltInMetrics = true,
            };
            var framesPerSecond = 59.97d;
            var usedBytes = 12345678d;
            var startupSeconds = 0.0125d;
            var payload = OtlpJsonWriter.BuildMetricsPayload(
                ResourceSnapshot.FromOptions(options),
                options,
                1_700_000_000_000_000_000L,
                framesPerSecond,
                (long)usedBytes,
                startupSeconds);

            StringAssert.Contains(framesPerSecond.ToString("R", CultureInfo.InvariantCulture), payload);
            StringAssert.Contains(usedBytes.ToString("R", CultureInfo.InvariantCulture), payload);
            StringAssert.Contains(startupSeconds.ToString("R", CultureInfo.InvariantCulture), payload);
            StringAssert.Contains(1_700_000_000_000_000_000L.ToString(CultureInfo.InvariantCulture), payload);
        }

        [Test]
        public void BuildLogsPayload_AllocatesLittleBeyondTheResultString()
        {
            var options = new UnimetryOptions
            {
                ServiceName = "game",
                ServiceVersion = "1.0.0",
                DeploymentEnvironment = "development",
            };
            var resource = ResourceSnapshot.FromOptions(options);
            var events = new EventRecord[8];
            for (var index = 0; index < events.Length; index++)
            {
                events[index].Tags = new EventTag[1];
                events[index].Name = "player.jump";
                events[index].StartUnixNano = 1_700_000_000_000_000_000L + index;
                events[index].EndUnixNano = events[index].StartUnixNano + 15000L;
                events[index].SeverityNumber = (int)UnimetrySeverity.Info;
                events[index].TagCount = 1;
                events[index].Tags[0] = EventTag.FromInt("player.id", index);
                events[index].Resource = resource;
            }

            var items = Array.Empty<PendingExport>();
            for (var index = 0; index < 4; index++)
            {
                OtlpJsonWriter.BuildLogsPayload(items, events, events.Length, options);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            var payload = OtlpJsonWriter.BuildLogsPayload(items, events, events.Length, options);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            var budget = payload.Length * 2 + 4096;

            Assert.LessOrEqual(allocated, budget, "bytes " + allocated + " payload chars " + payload.Length);
            StringAssert.Contains("\"eventName\":\"player.jump\"", payload);
        }

        private static long AllocatedBytes(Action action, int iterations)
        {
            for (var index = 0; index < 16; index++)
            {
                action();
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < iterations; index++)
            {
                action();
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static string FindAttribute(ResourceSnapshot snapshot, string key)
        {
            var attributes = snapshot.Attributes;
            for (var index = 0; index < attributes.Length; index++)
            {
                if (string.Equals(attributes[index].Key, key, StringComparison.Ordinal))
                {
                    return attributes[index].Value;
                }
            }

            return null;
        }

        private static MetricSnapshot Find(MetricBatch batch, string name)
        {
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
