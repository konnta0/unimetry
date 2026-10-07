using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unimetry.Internal;
using UnityEngine;

namespace Unimetry.Tests
{
    public sealed class RoadmapTests
    {
        [TearDown]
        public void TearDown()
        {
            TraceContext.Clear();
        }

        [Test]
        public void ExtractTraceParent_StartsChildSpanWithBaggage()
        {
            PendingExport exported = null;
            TraceContext.SetSpanRecorder(item => exported = item);
            var header = "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01";

            Assert.IsTrue(UnimetryTrace.ExtractTraceParent(header));
            Assert.IsFalse(UnimetryTrace.ExtractTraceParent("00-00000000000000000000000000000000-0123456789abcdef-01"));
            UnimetryTrace.SetBaggage("user.id", "player=1");

            using (var span = UnimetryTrace.Start("match.load"))
            {
                Assert.AreEqual("0123456789abcdef0123456789abcdef", span.TraceId);
                Assert.AreEqual("0123456789abcdef", span.ParentSpanId);
            }

            Assert.NotNull(exported);
            Assert.AreEqual("match.load", exported.SpanName);
            Assert.AreEqual(1, exported.SpanStatusCode);
            Assert.IsTrue(exported.SkipLog);
            StringAssert.Contains("user.id=player1", exported.Baggage);

            var options = new UnimetryOptions
            {
                Endpoint = "http://127.0.0.1:9",
                ServiceName = "unimetry-test",
            };
            var payload = OtlpJsonWriter.BuildTracesPayload(new List<PendingExport> { exported }, options);
            StringAssert.Contains("\"name\":\"match.load\"", payload);
            StringAssert.Contains("\"parentSpanId\":\"0123456789abcdef\"", payload);
            StringAssert.Contains("\"code\":1", payload);
            StringAssert.Contains("\"user.id\"", payload);
            StringAssert.DoesNotContain("\"name\":\"exception\"", payload);
        }

        [Test]
        public void ApplyToError_UsesCurrentSpanAsParent()
        {
            using (var span = UnimetryTrace.Start("gameplay"))
            {
                var traceId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
                var parent = string.Empty;
                TraceContext.ApplyToError(ref traceId, ref parent);
                Assert.AreEqual(span.TraceId, traceId);
                Assert.AreEqual(span.SpanId, parent);
            }
        }

        [Test]
        public void BuildMetricsPayload_IncludesFpsMemoryAndStartup()
        {
            var options = new UnimetryOptions
            {
                Endpoint = "http://127.0.0.1:9",
                ServiceName = "unimetry-test",
            };

            var payload = OtlpJsonWriter.BuildMetricsPayload(options, 1_700_000_000_000_000_000L, 59.5, 4096, 1.25);

            StringAssert.Contains("\"resourceMetrics\"", payload);
            StringAssert.Contains("\"name\":\"unity.fps\"", payload);
            StringAssert.Contains("\"name\":\"unity.memory.used_bytes\"", payload);
            StringAssert.Contains("\"name\":\"unity.startup.duration_s\"", payload);
            StringAssert.Contains("59.5", payload);
        }

        [Test]
        public void OfflineProtector_RoundTripsQueueJson()
        {
            var directory = Path.Combine(Application.temporaryCachePath, "unimetry-offline-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var keyPath = Path.Combine(directory, "offline.key");
                var protectedBytes = OfflineProtector.Protect("{\"items\":[{\"Message\":\"secret queue\"}]}", keyPath);
                Assert.AreEqual((byte)'U', protectedBytes[0]);
                var json = OfflineProtector.Unprotect(protectedBytes, keyPath);
                StringAssert.Contains("secret queue", json);
                var tampered = (byte[])protectedBytes.Clone();
                tampered[tampered.Length - 1] ^= 0xFF;
                Assert.IsNull(OfflineProtector.Unprotect(tampered, keyPath));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
