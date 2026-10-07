using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Unimetry.Internal
{
    internal static class JsonEscaper
    {
        public static void AppendEscaped(StringBuilder builder, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                switch (character)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < 32)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)character).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }
        }
    }

    internal static class OtlpJsonWriter
    {
        public static string BuildLogsPayload(
            IReadOnlyList<PendingExport> items,
            UnimetryOptions options)
        {
            return BuildLogsPayload(items, null, 0, options);
        }

        public static string BuildLogsPayload(
            IReadOnlyList<PendingExport> items,
            EventRecord[] events,
            int eventCount,
            UnimetryOptions options)
        {
            var builder = new StringBuilder(2048);
            builder.Append("{\"resourceLogs\":[{\"resource\":{\"attributes\":");
            AppendResourceAttributes(builder, options);
            builder.Append("},\"scopeLogs\":[{\"scope\":{\"name\":\"Unimetry\",\"version\":\"0.1.0\"},\"logRecords\":[");
            var errorCount = items == null ? 0 : items.Count;
            if (eventCount < 0)
            {
                eventCount = 0;
            }

            if (events == null || eventCount > events.Length)
            {
                eventCount = events == null ? 0 : events.Length;
            }

            var writtenLogs = 0;
            for (var index = 0; index < errorCount; index++)
            {
                if (items[index].SkipLog)
                {
                    continue;
                }

                if (writtenLogs > 0)
                {
                    builder.Append(',');
                }

                AppendLogRecord(builder, items[index]);
                writtenLogs++;
            }

            for (var index = 0; index < eventCount; index++)
            {
                if (writtenLogs > 0 || index > 0)
                {
                    builder.Append(',');
                }

                AppendEventRecord(builder, events[index]);
            }

            builder.Append("]}]}]}");
            return builder.ToString();
        }

        public static string BuildTracesPayload(
            IReadOnlyList<PendingExport> items,
            UnimetryOptions options)
        {
            var builder = new StringBuilder(2048);
            builder.Append("{\"resourceSpans\":[{\"resource\":{\"attributes\":");
            AppendResourceAttributes(builder, options);
            builder.Append("},\"scopeSpans\":[{\"scope\":{\"name\":\"Unimetry\",\"version\":\"0.1.0\"},\"spans\":[");
            var first = true;
            for (var index = 0; index < items.Count; index++)
            {
                if (!items[index].IncludeSpan)
                {
                    continue;
                }

                if (!first)
                {
                    builder.Append(',');
                }

                AppendSpan(builder, items[index]);
                first = false;
            }

            builder.Append("]}]}]}");
            return builder.ToString();
        }

        private static void AppendResourceAttributes(StringBuilder builder, UnimetryOptions options)
        {
            var attributes = new AttributeWriter(builder);
            attributes.WriteString("service.name", options.ServiceName);
            attributes.WriteString("service.version", options.ServiceVersion);
            attributes.WriteString("deployment.environment", options.DeploymentEnvironment);
            attributes.WriteString("telemetry.sdk.name", "unimetry");
            attributes.WriteString("telemetry.sdk.language", "csharp");
            attributes.WriteString("telemetry.sdk.version", "0.1.0");

            if (options.ResourceAttributes != null)
            {
                foreach (var pair in options.ResourceAttributes)
                {
                    attributes.WriteString(pair.Key, pair.Value);
                }
            }

            attributes.Complete();
        }

        private static void AppendLogRecord(StringBuilder builder, PendingExport item)
        {
            Enum.TryParse(item.Severity, out CapturedErrorSeverity severity);
            var mappedSeverity = SeverityMapping.FromCapturedError(severity);

            builder.Append('{');
            builder.Append("\"timeUnixNano\":\"");
            builder.Append(item.CapturedAtUnixNano.ToString(CultureInfo.InvariantCulture));
            builder.Append("\",\"observedTimeUnixNano\":\"");
            builder.Append(item.CapturedAtUnixNano.ToString(CultureInfo.InvariantCulture));
            builder.Append("\",\"severityNumber\":");
            builder.Append(mappedSeverity.Number);
            builder.Append(",\"severityText\":\"");
            builder.Append(mappedSeverity.Text);
            builder.Append("\",\"traceId\":\"");
            builder.Append(item.TraceId);
            builder.Append("\",\"spanId\":\"");
            builder.Append(item.SpanId);
            builder.Append("\",\"body\":{\"stringValue\":\"");
            JsonEscaper.AppendEscaped(builder, item.Message);
            builder.Append("\"},\"attributes\":");
            AppendErrorAttributes(builder, item);
            builder.Append('}');
        }

        private static void AppendSpan(StringBuilder builder, PendingExport item)
        {
            var endTime = item.CapturedAtUnixNano;
            var startTime = item.StartUnixNano > 0 && item.StartUnixNano <= endTime
                ? item.StartUnixNano
                : Math.Max(0, endTime - 1_000_000L);
            var statusCode = item.SpanStatusCode == 1 || item.SpanStatusCode == 2 ? item.SpanStatusCode : 2;

            builder.Append('{');
            builder.Append("\"traceId\":\"");
            builder.Append(item.TraceId);
            builder.Append("\",\"spanId\":\"");
            builder.Append(item.SpanId);
            builder.Append("\",\"parentSpanId\":\"");
            JsonEscaper.AppendEscaped(builder, item.ParentSpanId);
            builder.Append("\",\"name\":\"");
            JsonEscaper.AppendEscaped(builder, string.IsNullOrEmpty(item.SpanName) ? "unity.error" : item.SpanName);
            builder.Append("\",\"kind\":1");
            builder.Append(",\"startTimeUnixNano\":\"");
            builder.Append(startTime.ToString(CultureInfo.InvariantCulture));
            builder.Append("\",\"endTimeUnixNano\":\"");
            builder.Append(endTime.ToString(CultureInfo.InvariantCulture));
            builder.Append("\",\"status\":{\"code\":");
            builder.Append(statusCode.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"message\":\"");
            JsonEscaper.AppendEscaped(builder, item.Message);
            builder.Append("\"},\"attributes\":");
            AppendErrorAttributes(builder, item);
            if (statusCode != 1)
            {
                builder.Append(",\"events\":[{\"timeUnixNano\":\"");
                builder.Append(endTime.ToString(CultureInfo.InvariantCulture));
                builder.Append("\",\"name\":\"exception\",\"attributes\":");
                AppendExceptionEventAttributes(builder, item);
                builder.Append("}]");
            }

            builder.Append('}');
        }

        private static void AppendEventRecord(StringBuilder builder, EventRecord item)
        {
            var mappedSeverity = SeverityMapping.FromEventSeverity(item.SeverityNumber);
            var duration = item.EndUnixNano - item.StartUnixNano;
            if (duration < 0)
            {
                duration = 0;
            }

            builder.Append('{');
            builder.Append("\"timeUnixNano\":\"");
            builder.Append(item.StartUnixNano.ToString(CultureInfo.InvariantCulture));
            builder.Append("\",\"observedTimeUnixNano\":\"");
            builder.Append(item.EndUnixNano.ToString(CultureInfo.InvariantCulture));
            builder.Append("\",\"severityNumber\":");
            builder.Append(mappedSeverity.Number);
            builder.Append(",\"severityText\":\"");
            builder.Append(mappedSeverity.Text);
            builder.Append("\",\"eventName\":\"");
            JsonEscaper.AppendEscaped(builder, item.Name);
            builder.Append("\",\"body\":{\"stringValue\":\"");
            JsonEscaper.AppendEscaped(builder, item.Name);
            builder.Append("\"}");
            if (item.DroppedTags > 0)
            {
                builder.Append(",\"droppedAttributesCount\":\"");
                builder.Append(item.DroppedTags.ToString(CultureInfo.InvariantCulture));
                builder.Append('"');
            }

            builder.Append(",\"attributes\":");
            AppendEventAttributes(builder, item, duration);
            builder.Append('}');
        }

        private static void AppendEventAttributes(StringBuilder builder, EventRecord item, long durationNanos)
        {
            var attributes = new AttributeWriter(builder);
            attributes.WriteInt("unimetry.event.duration_ns", durationNanos);
            if (!string.IsNullOrEmpty(item.ExceptionType))
            {
                attributes.WriteString("exception.type", item.ExceptionType);
                attributes.WriteString("exception.message", item.ExceptionMessage);
                attributes.WriteString("exception.stacktrace", item.ExceptionStack);
                attributes.WriteBool("exception.escaped", item.ExceptionEscaped);
            }

            var tagCount = item.TagCount;
            if (item.Tags != null && tagCount > item.Tags.Length)
            {
                tagCount = item.Tags.Length;
            }

            for (var index = 0; index < tagCount; index++)
            {
                var tag = item.Tags[index];
                switch (tag.Kind)
                {
                    case EventTagKind.String:
                        attributes.WriteString(tag.Key, tag.Text, allowEmpty: true);
                        break;
                    case EventTagKind.Bool:
                        attributes.WriteBool(tag.Key, tag.Bits != 0);
                        break;
                    case EventTagKind.Int:
                    case EventTagKind.Long:
                        attributes.WriteInt(tag.Key, tag.Bits);
                        break;
                    case EventTagKind.Double:
                        attributes.WriteDouble(tag.Key, tag.ReadDouble());
                        break;
                }
            }

            attributes.Complete();
        }

        private static void AppendErrorAttributes(StringBuilder builder, PendingExport item)
        {
            var attributes = new AttributeWriter(builder);
            attributes.WriteString("unimetry.source", item.Source);
            attributes.WriteString("unimetry.fingerprint", item.Fingerprint);
            attributes.WriteString("thread.name", item.ThreadName);
            attributes.WriteBool("exception.escaped", item.IsTerminating);
            attributes.WriteString("exception.type", item.ExceptionType);
            attributes.WriteString("exception.message", item.Message);
            attributes.WriteString("exception.stacktrace", item.StackTrace);
            AppendBaggage(attributes, item.Baggage);
            if (string.Equals(item.RecordType, CrashRecord.Type, StringComparison.Ordinal))
            {
                attributes.WriteString("unimetry.record_type", CrashRecord.Type);
                attributes.WriteString("crash.signal", item.Signal);
                attributes.WriteString("crash.registers", item.Registers);
                attributes.WriteString("crash.breadcrumbs", item.Breadcrumbs);
                attributes.WriteString("device.model", item.DeviceModel);
                attributes.WriteString("os.type", item.OsType);
                attributes.WriteString("app.build_id", item.BuildId);
                attributes.WriteString("crash.minidump_file", item.MinidumpFile);
                if (item.MinidumpBytes > 0)
                {
                    attributes.WriteInt("crash.minidump_bytes", item.MinidumpBytes);
                }
            }

            attributes.Complete();
        }

        public static string BuildMetricsPayload(
            UnimetryOptions options,
            long timeUnixNano,
            double framesPerSecond,
            long usedBytes,
            double startupSeconds)
        {
            var builder = new StringBuilder(1024);
            builder.Append("{\"resourceMetrics\":[{\"resource\":{\"attributes\":");
            AppendResourceAttributes(builder, options);
            builder.Append("},\"scopeMetrics\":[{\"scope\":{\"name\":\"Unimetry\",\"version\":\"0.1.0\"},\"metrics\":[");
            var written = 0;
            if (!double.IsNaN(framesPerSecond) && !double.IsInfinity(framesPerSecond) && framesPerSecond >= 0)
            {
                AppendGauge(builder, "unity.fps", framesPerSecond, timeUnixNano, ref written);
            }

            if (usedBytes >= 0)
            {
                AppendGauge(builder, "unity.memory.used_bytes", usedBytes, timeUnixNano, ref written);
            }

            if (!double.IsNaN(startupSeconds) && startupSeconds >= 0)
            {
                AppendGauge(builder, "unity.startup.duration_s", startupSeconds, timeUnixNano, ref written);
            }

            builder.Append("]}]}]}");
            return builder.ToString();
        }

        private static void AppendGauge(
            StringBuilder builder,
            string name,
            double value,
            long timeUnixNano,
            ref int written)
        {
            if (written > 0)
            {
                builder.Append(',');
            }

            written++;
            builder.Append("{\"name\":\"");
            JsonEscaper.AppendEscaped(builder, name);
            builder.Append("\",\"gauge\":{\"dataPoints\":[{\"timeUnixNano\":\"");
            builder.Append(timeUnixNano.ToString(CultureInfo.InvariantCulture));
            builder.Append("\",\"asDouble\":");
            builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
            builder.Append("}]}}");
        }

        private static void AppendBaggage(AttributeWriter attributes, string baggage)
        {
            if (string.IsNullOrEmpty(baggage) || attributes == null)
            {
                return;
            }

            var lines = baggage.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                var split = line.IndexOf('=');
                if (split <= 0 || split >= line.Length - 1)
                {
                    continue;
                }

                attributes.WriteString(line.Substring(0, split), line.Substring(split + 1));
            }
        }

        private static void AppendExceptionEventAttributes(StringBuilder builder, PendingExport item)
        {
            var attributes = new AttributeWriter(builder);
            attributes.WriteString("exception.type", item.ExceptionType);
            attributes.WriteString("exception.message", item.Message);
            attributes.WriteString("exception.stacktrace", item.StackTrace);
            attributes.Complete();
        }

        private sealed class AttributeWriter
        {
            private readonly StringBuilder builder;
            private bool hasItems;

            public AttributeWriter(StringBuilder builder)
            {
                this.builder = builder;
                builder.Append('[');
            }

            public void WriteString(string key, string value)
            {
                WriteString(key, value, allowEmpty: false);
            }

            public void WriteString(string key, string value, bool allowEmpty)
            {
                if (string.IsNullOrWhiteSpace(key) || (!allowEmpty && string.IsNullOrEmpty(value)))
                {
                    return;
                }

                if (value == null)
                {
                    value = string.Empty;
                }

                AppendSeparator();
                builder.Append("{\"key\":\"");
                JsonEscaper.AppendEscaped(builder, key);
                builder.Append("\",\"value\":{\"stringValue\":\"");
                JsonEscaper.AppendEscaped(builder, value);
                builder.Append("\"}}");
            }

            public void WriteInt(string key, long value)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    return;
                }

                AppendSeparator();
                builder.Append("{\"key\":\"");
                JsonEscaper.AppendEscaped(builder, key);
                builder.Append("\",\"value\":{\"intValue\":\"");
                builder.Append(value.ToString(CultureInfo.InvariantCulture));
                builder.Append("\"}}");
            }

            public void WriteDouble(string key, double value)
            {
                if (string.IsNullOrWhiteSpace(key) || double.IsNaN(value) || double.IsInfinity(value))
                {
                    return;
                }

                AppendSeparator();
                builder.Append("{\"key\":\"");
                JsonEscaper.AppendEscaped(builder, key);
                builder.Append("\",\"value\":{\"doubleValue\":");
                builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
                builder.Append("}}");
            }

            public void WriteBool(string key, bool value)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    return;
                }

                AppendSeparator();
                builder.Append("{\"key\":\"");
                JsonEscaper.AppendEscaped(builder, key);
                builder.Append("\",\"value\":{\"boolValue\":");
                builder.Append(value ? "true" : "false");
                builder.Append("}}");
            }

            public void Complete()
            {
                builder.Append(']');
            }

            private void AppendSeparator()
            {
                if (hasItems)
                {
                    builder.Append(',');
                }

                hasItems = true;
            }
        }
    }
}
