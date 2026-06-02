using System;

namespace Unimetry
{
    /// <summary>
    /// Severity of a captured Unity log or exception event.
    /// </summary>
    public enum CapturedErrorSeverity
    {
        /// <summary>Non-fatal error log.</summary>
        Error = 0,

        /// <summary>Assertion failure.</summary>
        Assert = 1,

        /// <summary>Unhandled or observed exception.</summary>
        Exception = 2,

        /// <summary>Process-terminating unhandled exception.</summary>
        Fatal = 3,
    }

    /// <summary>
    /// Source that produced a captured error.
    /// </summary>
    public enum CapturedErrorSource
    {
        /// <summary>Unity log callback.</summary>
        UnityLog = 0,

        /// <summary><see cref="AppDomain.UnhandledException"/> handler.</summary>
        UnhandledException = 1,

        /// <summary><see cref="TaskScheduler.UnobservedTaskException"/> handler.</summary>
        UnobservedTaskException = 2,

        /// <summary>Explicit call to <see cref="UnimetryClient.Report"/>.</summary>
        Manual = 3,
    }

    /// <summary>
    /// Normalized error payload captured by Unimetry before OTLP export.
    /// </summary>
    public sealed class CapturedError
    {
        /// <summary>
        /// Initializes a new captured error payload.
        /// </summary>
        /// <param name="message">Human-readable error message.</param>
        /// <param name="severity">Captured severity.</param>
        /// <param name="source">Capture source.</param>
        /// <param name="exceptionType">Exception type name when available.</param>
        /// <param name="stackTrace">Formatted stack trace.</param>
        /// <param name="threadName">Managed thread name when available.</param>
        /// <param name="isTerminating">Whether the runtime is terminating after this error.</param>
        /// <param name="fingerprint">Stable deduplication fingerprint.</param>
        /// <param name="traceId">Hex-encoded 32-character trace identifier.</param>
        /// <param name="spanId">Hex-encoded 16-character span identifier.</param>
        /// <param name="capturedAtUtc">UTC timestamp when the error was captured.</param>
        public CapturedError(
            string message,
            CapturedErrorSeverity severity,
            CapturedErrorSource source,
            string exceptionType,
            string stackTrace,
            string threadName,
            bool isTerminating,
            string fingerprint,
            string traceId,
            string spanId,
            DateTimeOffset capturedAtUtc)
        {
            Message = message ?? string.Empty;
            Severity = severity;
            Source = source;
            ExceptionType = exceptionType ?? string.Empty;
            StackTrace = stackTrace ?? string.Empty;
            ThreadName = threadName ?? string.Empty;
            IsTerminating = isTerminating;
            Fingerprint = fingerprint ?? string.Empty;
            TraceId = traceId ?? string.Empty;
            SpanId = spanId ?? string.Empty;
            CapturedAtUtc = capturedAtUtc;
        }

        /// <summary>Human-readable error message.</summary>
        public string Message { get; }

        /// <summary>Captured severity.</summary>
        public CapturedErrorSeverity Severity { get; }

        /// <summary>Capture source.</summary>
        public CapturedErrorSource Source { get; }

        /// <summary>Exception type name when available.</summary>
        public string ExceptionType { get; }

        /// <summary>Formatted stack trace.</summary>
        public string StackTrace { get; }

        /// <summary>Managed thread name when available.</summary>
        public string ThreadName { get; }

        /// <summary>Whether the runtime is terminating after this error.</summary>
        public bool IsTerminating { get; }

        /// <summary>Stable deduplication fingerprint.</summary>
        public string Fingerprint { get; }

        /// <summary>Hex-encoded 32-character trace identifier.</summary>
        public string TraceId { get; }

        /// <summary>Hex-encoded 16-character span identifier.</summary>
        public string SpanId { get; }

        /// <summary>UTC timestamp when the error was captured.</summary>
        public DateTimeOffset CapturedAtUtc { get; }
    }
}
