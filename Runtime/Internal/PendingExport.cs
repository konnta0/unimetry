using System;

namespace Unimetry.Internal
{
    [Serializable]
    internal sealed class PendingExport
    {
        public string Message;
        public string Severity;
        public string Source;
        public string ExceptionType;
        public string StackTrace;
        public string ThreadName;
        public bool IsTerminating;
        public string Fingerprint;
        public string TraceId;
        public string SpanId;
        public long CapturedAtUnixNano;
        public bool IncludeSpan;
        public string RecordType;
        public string Signal;
        public string Registers;
        public string Breadcrumbs;
        public string DeviceModel;
        public string OsType;
        public string BuildId;
        public string MinidumpFile;
        public int MinidumpBytes;
        public string SpanName;
        public string ParentSpanId;
        public string Baggage;
        public int SpanStatusCode;
        public long StartUnixNano;
        public bool SkipLog;

        public static PendingExport FromCapturedError(CapturedError capturedError, bool includeSpan)
        {
            return new PendingExport
            {
                Message = capturedError.Message,
                Severity = capturedError.Severity.ToString(),
                Source = capturedError.Source.ToString(),
                ExceptionType = capturedError.ExceptionType,
                StackTrace = capturedError.StackTrace,
                ThreadName = capturedError.ThreadName,
                IsTerminating = capturedError.IsTerminating,
                Fingerprint = capturedError.Fingerprint,
                TraceId = capturedError.TraceId,
                SpanId = capturedError.SpanId,
                CapturedAtUnixNano = capturedError.CapturedAtUtc.ToUnixTimeMilliseconds() * 1_000_000L,
                IncludeSpan = includeSpan,
            };
        }

        public CapturedError ToCapturedError()
        {
            Enum.TryParse(Severity, out CapturedErrorSeverity severity);
            Enum.TryParse(Source, out CapturedErrorSource source);
            var capturedAt = DateTimeOffset.FromUnixTimeMilliseconds(CapturedAtUnixNano / 1_000_000L);

            return new CapturedError(
                Message,
                severity,
                source,
                ExceptionType,
                StackTrace,
                ThreadName,
                IsTerminating,
                Fingerprint,
                TraceId,
                SpanId,
                capturedAt);
        }
    }

    [Serializable]
    internal sealed class PendingExportEnvelope
    {
        public PendingExport[] Items = Array.Empty<PendingExport>();
    }
}
