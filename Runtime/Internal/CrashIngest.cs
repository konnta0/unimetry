using System;
using UnityEngine;

namespace Unimetry.Internal
{
    internal static class CrashIngest
    {
        private const long MaxSafeUnixMilliseconds = 9_223_372_036_854L;

        public static int EnqueuePending(CrashArtifactStore store, PersistentQueue queue, UnimetryOptions options)
        {
            if (store == null || queue == null || options == null)
            {
                return 0;
            }

            var pending = store.ReadPending();
            var enqueued = 0;
            for (var index = 0; index < pending.Count; index++)
            {
                var artifact = pending[index];
                if (!store.HasDump(artifact))
                {
                    artifact.minidumpFile = string.Empty;
                    artifact.minidumpBytes = 0;
                }

                var export = ToPendingExport(artifact, options);
                if (export != null)
                {
                    queue.Enqueue(export);
                    Emit(export, options);
                    enqueued++;
                }

                store.DeleteJson(artifact);
            }

            return enqueued;
        }

        public static PendingExport ToPendingExport(CrashArtifact artifact, UnimetryOptions options)
        {
            if (artifact == null || options == null)
            {
                return null;
            }

            artifact.Clamp();
            var message = artifact.message;
            if (string.IsNullOrEmpty(message))
            {
                message = string.IsNullOrEmpty(artifact.exceptionType) ? artifact.signal : artifact.exceptionType;
            }

            if (string.IsNullOrEmpty(message))
            {
                message = "native crash";
            }

            var capturedAtMs = artifact.capturedAtUnixMs;
            if (capturedAtMs <= 0 || capturedAtMs > MaxSafeUnixMilliseconds)
            {
                capturedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
            var capturedAt = DateTimeOffset.FromUnixTimeMilliseconds(capturedAtMs);
            var traceId = IdGenerator.CreateTraceId();
            var spanId = IdGenerator.CreateSpanId();
            var preliminary = new CapturedError(
                message,
                CapturedErrorSeverity.Fatal,
                CapturedErrorSource.NativeCrash,
                artifact.exceptionType,
                artifact.managedStack,
                artifact.threadName,
                true,
                string.Empty,
                traceId,
                spanId,
                capturedAt);
            var sanitized = ApplySanitizer(options, preliminary);
            var breadcrumbs = artifact.breadcrumbs;
            if (!string.IsNullOrEmpty(breadcrumbs) && options.Sanitizer != null)
            {
                var probe = new CapturedError(
                    breadcrumbs,
                    CapturedErrorSeverity.Fatal,
                    CapturedErrorSource.NativeCrash,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    true,
                    string.Empty,
                    traceId,
                    spanId,
                    capturedAt);
                breadcrumbs = ApplySanitizer(options, probe).Message ?? string.Empty;
            }

            var exceptionType = sanitized.ExceptionType ?? string.Empty;
            var sanitizedMessage = string.IsNullOrEmpty(sanitized.Message) ? message : sanitized.Message;
            var stack = sanitized.StackTrace ?? string.Empty;
            var threadName = sanitized.ThreadName ?? string.Empty;
            var fingerprint = IdGenerator.CreateFingerprint(exceptionType, sanitizedMessage, stack);
            var minidumpFile = artifact.minidumpFile ?? string.Empty;
            var minidumpBytes = artifact.minidumpBytes;
            if (!CrashArtifactStore.IsSafeDumpName(artifact.id, minidumpFile))
            {
                minidumpFile = string.Empty;
                minidumpBytes = 0;
            }

            return new PendingExport
            {
                Message = sanitizedMessage,
                Severity = CapturedErrorSeverity.Fatal.ToString(),
                Source = CapturedErrorSource.NativeCrash.ToString(),
                ExceptionType = exceptionType,
                StackTrace = stack,
                ThreadName = threadName,
                IsTerminating = true,
                Fingerprint = fingerprint,
                TraceId = traceId,
                SpanId = spanId,
                CapturedAtUnixNano = capturedAtMs * 1_000_000L,
                IncludeSpan = options.ExportErrorSpans,
                RecordType = CrashRecord.Type,
                Signal = artifact.signal ?? string.Empty,
                Registers = artifact.registers ?? string.Empty,
                Breadcrumbs = breadcrumbs ?? string.Empty,
                DeviceModel = artifact.deviceModel ?? string.Empty,
                OsType = artifact.osType ?? string.Empty,
                BuildId = artifact.buildId ?? string.Empty,
                MinidumpFile = minidumpFile,
                MinidumpBytes = minidumpBytes,
                SpanName = CrashRecord.SpanName,
            };
        }

        private static CapturedError ApplySanitizer(UnimetryOptions options, CapturedError captured)
        {
            if (options.Sanitizer == null)
            {
                return captured;
            }

            try
            {
                return options.Sanitizer(captured) ?? captured;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Unimetry crash sanitizer failed: " + exception.Message);
                return captured;
            }
        }

        private static void Emit(PendingExport export, UnimetryOptions options)
        {
            var entry = new UnimetryLogEntry(
                UnimetryLogKind.Log,
                CrashRecord.Type,
                export.Message,
                UnimetrySeverity.Fatal,
                export.CapturedAtUnixNano,
                export.CapturedAtUnixNano);
            if (options.LogWriter != null)
            {
                LogDispatch.Emit(options.LogWriter, in entry);
                return;
            }

            Debug.LogWarning(
                "Unimetry recovered a native crash from the previous session: " + export.Message);
        }
    }
}
