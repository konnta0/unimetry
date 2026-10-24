using System.Collections.Generic;
using NUnit.Framework;
using Unimetry.Internal;

namespace Unimetry.Tests
{
    public sealed class OtlpJsonWriterTests
    {
        [Test]
        public void BuildLogsPayload_IncludesExceptionSemanticConventions()
        {
            var options = CreateOptions();
            var items = new List<PendingExport>
            {
                CreatePendingExport(),
            };

            var payload = OtlpJsonWriter.BuildLogsPayload(items, options);

            StringAssert.Contains("\"resourceLogs\"", payload);
            StringAssert.Contains("\"exception.type\"", payload);
            StringAssert.Contains("System.InvalidOperationException", payload);
            StringAssert.Contains("\"exception.message\"", payload);
            StringAssert.Contains("test failure", payload);
            StringAssert.Contains("\"exception.stacktrace\"", payload);
            StringAssert.Contains("at Example()", payload);
            StringAssert.Contains("\"unimetry.fingerprint\"", payload);
            StringAssert.Contains("\"service.name\"", payload);
            StringAssert.Contains("unimetry-test", payload);
        }

        [Test]
        public void BuildLogsPayload_IncludesEventNameAndBothTimestamps()
        {
            var options = CreateOptions();
            var events = new EventRecord[1];
            events[0].Tags = new EventTag[EventBuffer.MaxTags];
            events[0].Name = "player.jump";
            events[0].StartUnixNano = 10;
            events[0].EndUnixNano = 25;
            events[0].SeverityNumber = (int)UnimetrySeverity.Info;
            events[0].TagCount = 1;
            events[0].Tags[0] = EventTag.FromInt("player.id", 7);

            var payload = OtlpJsonWriter.BuildLogsPayload(new List<PendingExport>(), events, 1, options);

            StringAssert.Contains("\"eventName\":\"player.jump\"", payload);
            StringAssert.Contains("\"timeUnixNano\":\"10\"", payload);
            StringAssert.Contains("\"observedTimeUnixNano\":\"25\"", payload);
            StringAssert.Contains("\"unimetry.event.duration_ns\"", payload);
            StringAssert.Contains("\"intValue\":\"15\"", payload);
            StringAssert.Contains("\"player.id\"", payload);
            StringAssert.Contains("\"intValue\":\"7\"", payload);
            StringAssert.Contains("\"severityText\":\"INFO\"", payload);
        }

        [Test]
        public void BuildTracesPayload_IncludesErrorSpanAndExceptionEvent()
        {
            var options = CreateOptions();
            var items = new List<PendingExport>
            {
                CreatePendingExport(includeSpan: true),
            };

            var payload = OtlpJsonWriter.BuildTracesPayload(items, options);

            StringAssert.Contains("\"resourceSpans\"", payload);
            StringAssert.Contains("\"name\":\"unity.error\"", payload);
            StringAssert.Contains("\"status\":{\"code\":2", payload);
            StringAssert.Contains("\"name\":\"exception\"", payload);
            StringAssert.Contains("\"traceId\":\"abc123traceabc123traceabc12312\"", payload);
            StringAssert.Contains("\"spanId\":\"abc123spanabc12\"", payload);
        }

        [Test]
        public void BuildTracesPayload_UsesCaptureTimeWhenSpanStartIsMissing()
        {
            var options = CreateOptions();
            var item = CreatePendingExport(includeSpan: true);
            item.StartUnixNano = 0;

            var payload = OtlpJsonWriter.BuildTracesPayload(
                new List<PendingExport> { item },
                options);

            var timestamp = item.CapturedAtUnixNano.ToString(System.Globalization.CultureInfo.InvariantCulture);
            StringAssert.Contains("\"startTimeUnixNano\":\"" + timestamp + "\"", payload);
            StringAssert.Contains("\"endTimeUnixNano\":\"" + timestamp + "\"", payload);
        }

        [Test]
        public void BuildLogsPayload_GroupsRecordsByCapturedResource()
        {
            var options = CreateOptions();
            var first = CreatePendingExport();
            first.Message = "account-a-record";
            first.Resource = ResourceSnapshot.FromDictionary(new Dictionary<string, string>
            {
                ["service.name"] = "unimetry-test",
                ["app.user_id"] = "account-a",
            });
            var second = CreatePendingExport();
            second.Message = "account-b-record";
            second.Resource = ResourceSnapshot.FromDictionary(new Dictionary<string, string>
            {
                ["service.name"] = "unimetry-test",
                ["app.user_id"] = "account-b",
            });

            var payload = OtlpJsonWriter.BuildLogsPayload(
                new List<PendingExport> { first, second },
                options);

            Assert.AreEqual(2, Count(payload, "\"resource\""));
            StringAssert.Contains("account-a", payload);
            StringAssert.Contains("account-b", payload);
        }

        [Test]
        public void BuildLogsPayload_LegacyRecordDoesNotInheritCurrentIdentity()
        {
            var options = CreateOptions();
            options.ResourceAttributes = new Dictionary<string, string>
            {
                ["app.user_id"] = "current-account",
            };

            var payload = OtlpJsonWriter.BuildLogsPayload(
                new List<PendingExport> { CreatePendingExport() },
                options);

            StringAssert.DoesNotContain("current-account", payload);
            StringAssert.Contains("unimetry.resource_snapshot.missing", payload);
        }

        [Test]
        public void JsonEscaper_EscapesControlCharacters()
        {
            var builder = new System.Text.StringBuilder();
            JsonEscaper.AppendEscaped(builder, "line1\nline2\t\"quoted\"");

            Assert.AreEqual("line1\\nline2\\t\\\"quoted\\\"", builder.ToString());
        }

        private static UnimetryOptions CreateOptions()
        {
            return new UnimetryOptions
            {
                Endpoint = "http://localhost:4318",
                ServiceName = "unimetry-test",
                ServiceVersion = "0.1.0-test",
                DeploymentEnvironment = "test",
            };
        }

        private static PendingExport CreatePendingExport(bool includeSpan = false)
        {
            return new PendingExport
            {
                Message = "test failure",
                Severity = CapturedErrorSeverity.Exception.ToString(),
                Source = CapturedErrorSource.Manual.ToString(),
                ExceptionType = "System.InvalidOperationException",
                StackTrace = "at Example()",
                ThreadName = "Main Thread",
                IsTerminating = false,
                Fingerprint = "deadbeef",
                TraceId = "abc123traceabc123traceabc12312",
                SpanId = "abc123spanabc12",
                CapturedAtUnixNano = 1_700_000_000_000_000_000L,
                IncludeSpan = includeSpan,
            };
        }

        private static int Count(string value, string needle)
        {
            var count = 0;
            var offset = 0;
            while ((offset = value.IndexOf(needle, offset, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += needle.Length;
            }

            return count;
        }
    }
}
