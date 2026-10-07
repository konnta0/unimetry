using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace Unimetry.Internal
{
    internal static class TraceContext
    {
        private const int MaxBaggage = 8;
        private const int MaxBaggageKeyLength = 64;
        private const int MaxBaggageValueLength = 128;

        private static readonly AsyncLocal<Frame> Slot = new AsyncLocal<Frame>();
        private static readonly object BaggageGate = new object();
        private static readonly List<KeyValuePair<string, string>> BaggageItems = new List<KeyValuePair<string, string>>(MaxBaggage);
        private static Action<PendingExport> spanRecorder;

        public static void SetSpanRecorder(Action<PendingExport> recorder)
        {
            spanRecorder = recorder;
        }

        public static void Clear()
        {
            Slot.Value = null;
            spanRecorder = null;
            lock (BaggageGate)
            {
                BaggageItems.Clear();
            }
        }

        public static bool ExtractTraceParent(string header)
        {
            if (!TryParseTraceParent(header, out var traceId, out var spanId))
            {
                return false;
            }

            Slot.Value = new Frame
            {
                TraceId = traceId,
                SpanId = spanId,
                ParentSpanId = string.Empty,
                Remote = true,
                Previous = null,
            };
            return true;
        }

        public static void SetBaggage(string key, string value)
        {
            key = SanitizeToken(key, MaxBaggageKeyLength);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            value = SanitizeToken(value, MaxBaggageValueLength);
            lock (BaggageGate)
            {
                for (var index = 0; index < BaggageItems.Count; index++)
                {
                    if (!string.Equals(BaggageItems[index].Key, key, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(value))
                    {
                        BaggageItems.RemoveAt(index);
                    }
                    else
                    {
                        BaggageItems[index] = new KeyValuePair<string, string>(key, value);
                    }

                    return;
                }

                if (string.IsNullOrEmpty(value) || BaggageItems.Count >= MaxBaggage)
                {
                    return;
                }

                BaggageItems.Add(new KeyValuePair<string, string>(key, value));
            }
        }

        public static string FormatBaggage()
        {
            lock (BaggageGate)
            {
                if (BaggageItems.Count == 0)
                {
                    return string.Empty;
                }

                var builder = new System.Text.StringBuilder();
                for (var index = 0; index < BaggageItems.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append('\n');
                    }

                    builder.Append(BaggageItems[index].Key);
                    builder.Append('=');
                    builder.Append(BaggageItems[index].Value);
                }

                return builder.ToString();
            }
        }

        public static UnimetrySpan Start(string name)
        {
            var parentTrace = string.Empty;
            var parentSpan = string.Empty;
            if (!TryReadActivity(out parentTrace, out parentSpan))
            {
                var current = Slot.Value;
                if (current != null)
                {
                    parentTrace = current.TraceId;
                    parentSpan = current.SpanId;
                }
            }

            var traceId = string.IsNullOrEmpty(parentTrace) ? IdGenerator.CreateTraceId() : parentTrace;
            var spanId = IdGenerator.CreateSpanId();
            Slot.Value = new Frame
            {
                TraceId = traceId,
                SpanId = spanId,
                ParentSpanId = parentSpan ?? string.Empty,
                Remote = false,
                Previous = Slot.Value,
            };
            var startNano = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L;
            return new UnimetrySpan(name, traceId, spanId, parentSpan ?? string.Empty, startNano);
        }

        public static void Complete(UnimetrySpan span)
        {
            if (span == null || string.IsNullOrEmpty(span.SpanId))
            {
                return;
            }

            var current = Slot.Value;
            if (current != null && string.Equals(current.SpanId, span.SpanId, StringComparison.Ordinal))
            {
                Slot.Value = current.Previous;
            }

            var endNano = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L;
            if (endNano < span.StartUnixNano)
            {
                endNano = span.StartUnixNano;
            }

            var export = new PendingExport
            {
                Message = span.Name,
                Severity = CapturedErrorSeverity.Error.ToString(),
                Source = CapturedErrorSource.Manual.ToString(),
                ExceptionType = string.Empty,
                StackTrace = string.Empty,
                ThreadName = string.Empty,
                IsTerminating = false,
                Fingerprint = string.Empty,
                TraceId = span.TraceId,
                SpanId = span.SpanId,
                ParentSpanId = span.ParentSpanId,
                CapturedAtUnixNano = endNano,
                StartUnixNano = span.StartUnixNano,
                IncludeSpan = true,
                SkipLog = true,
                SpanName = span.Name,
                SpanStatusCode = 1,
                Baggage = FormatBaggage(),
            };
            spanRecorder?.Invoke(export);
        }

        public static void ApplyToError(ref string traceId, ref string parentSpanId)
        {
            if (TryReadActivity(out var activityTrace, out var activitySpan))
            {
                traceId = activityTrace;
                parentSpanId = activitySpan;
                return;
            }

            var current = Slot.Value;
            if (current == null || string.IsNullOrEmpty(current.TraceId))
            {
                return;
            }

            traceId = current.TraceId;
            parentSpanId = current.SpanId ?? string.Empty;
        }

        public static bool TryParseTraceParent(string header, out string traceId, out string spanId)
        {
            traceId = string.Empty;
            spanId = string.Empty;
            if (string.IsNullOrWhiteSpace(header))
            {
                return false;
            }

            var parts = header.Trim().Split('-');
            if (parts.Length != 4 || !string.Equals(parts[0], "00", StringComparison.Ordinal))
            {
                return false;
            }

            if (!IsHex(parts[1], 32) || !IsHex(parts[2], 16) || !IsHex(parts[3], 2))
            {
                return false;
            }

            if (IsAllZero(parts[1]) || IsAllZero(parts[2]))
            {
                return false;
            }

            traceId = parts[1];
            spanId = parts[2];
            return true;
        }

        private static bool activityLookupDone;
        private static PropertyInfo activityCurrent;
        private static PropertyInfo activityTraceId;
        private static PropertyInfo activitySpanId;
        private static MethodInfo traceToHex;
        private static MethodInfo spanToHex;

        private static bool TryReadActivity(out string traceId, out string spanId)
        {
            traceId = string.Empty;
            spanId = string.Empty;
            try
            {
                EnsureActivityLookup();
                if (activityCurrent == null)
                {
                    return false;
                }

                var activity = activityCurrent.GetValue(null, null);
                if (activity == null)
                {
                    return false;
                }

                traceId = ReadHex(activity, activityTraceId, traceToHex);
                spanId = ReadHex(activity, activitySpanId, spanToHex);
                if (!IsHex(traceId, 32) || !IsHex(spanId, 16) || IsAllZero(traceId))
                {
                    traceId = string.Empty;
                    spanId = string.Empty;
                    return false;
                }

                return true;
            }
            catch (Exception)
            {
                traceId = string.Empty;
                spanId = string.Empty;
                return false;
            }
        }

        private static void EnsureActivityLookup()
        {
            if (activityLookupDone)
            {
                return;
            }

            activityLookupDone = true;
            var activityType = Type.GetType("System.Diagnostics.Activity, System.Diagnostics.DiagnosticSource");
            if (activityType == null)
            {
                activityType = Type.GetType("System.Diagnostics.Activity");
            }

            if (activityType == null)
            {
                return;
            }

            activityCurrent = activityType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
            activityTraceId = activityType.GetProperty("TraceId", BindingFlags.Public | BindingFlags.Instance);
            activitySpanId = activityType.GetProperty("SpanId", BindingFlags.Public | BindingFlags.Instance);
            traceToHex = activityTraceId?.PropertyType.GetMethod("ToHexString", Type.EmptyTypes);
            spanToHex = activitySpanId?.PropertyType.GetMethod("ToHexString", Type.EmptyTypes);
        }

        private static string ReadHex(object activity, PropertyInfo property, MethodInfo toHex)
        {
            if (activity == null || property == null)
            {
                return string.Empty;
            }

            var value = property.GetValue(activity, null);
            if (value == null)
            {
                return string.Empty;
            }

            if (toHex != null)
            {
                return toHex.Invoke(value, null) as string ?? string.Empty;
            }

            return value.ToString() ?? string.Empty;
        }

        private static string SanitizeToken(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(value.Length);
            for (var index = 0; index < value.Length && builder.Length < maxLength; index++)
            {
                var character = value[index];
                if (character == '=' || character == '\n' || character == '\r' || character < 32)
                {
                    continue;
                }

                builder.Append(character);
            }

            return builder.ToString();
        }

        private static bool IsHex(string value, int length)
        {
            if (string.IsNullOrEmpty(value) || value.Length != length)
            {
                return false;
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                var hex = (character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAllZero(string value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] != '0')
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class Frame
        {
            public string TraceId;
            public string SpanId;
            public string ParentSpanId;
            public bool Remote;
            public Frame Previous;
        }
    }
}
