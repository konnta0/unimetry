using System;
using System.Collections.Generic;

namespace Unimetry
{
    /// <summary>
    /// Configuration for Unimetry telemetry export.
    /// </summary>
    public sealed class UnimetryOptions
    {
        /// <summary>
        /// OTLP/HTTP base endpoint, for example <c>https://collector.example.com:4318</c>.
        /// </summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>
        /// OpenTelemetry <c>service.name</c> resource attribute.
        /// </summary>
        public string ServiceName { get; set; } = "unity-app";

        /// <summary>
        /// OpenTelemetry <c>service.version</c> resource attribute.
        /// </summary>
        public string ServiceVersion { get; set; } = string.Empty;

        /// <summary>
        /// OpenTelemetry <c>deployment.environment</c> resource attribute.
        /// </summary>
        public string DeploymentEnvironment { get; set; } = "production";

        /// <summary>
        /// Additional resource attributes appended to every export batch.
        /// </summary>
        public IReadOnlyDictionary<string, string> ResourceAttributes { get; set; }
            = new Dictionary<string, string>();

        /// <summary>
        /// Optional HTTP headers, for example bearer tokens required by a gateway.
        /// </summary>
        public IReadOnlyDictionary<string, string> Headers { get; set; }
            = new Dictionary<string, string>();

        /// <summary>
        /// When true, TLS certificate validation errors are ignored. Use only for local development.
        /// </summary>
        public bool AllowInsecureTls { get; set; }

        /// <summary>
        /// When true, captured errors are also exported as OTLP trace spans in addition to logs.
        /// </summary>
        public bool ExportErrorSpans { get; set; } = true;

        /// <summary>
        /// Maximum number of stack frames included in exported stack traces.
        /// </summary>
        public int MaxStackFrames { get; set; } = 64;

        /// <summary>
        /// Maximum number of queued export payloads retained on disk while offline.
        /// </summary>
        public int MaxQueueSize { get; set; } = 256;

        /// <summary>
        /// Maximum number of log records included in a single OTLP export request.
        /// </summary>
        public int MaxBatchSize { get; set; } = 32;

        /// <summary>
        /// Minimum interval between automatic flush attempts.
        /// </summary>
        public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Optional callback that can redact or rewrite captured error payloads before export.
        /// </summary>
        public Func<CapturedError, CapturedError> Sanitizer { get; set; }

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(Endpoint))
            {
                throw new InvalidOperationException("Unimetry endpoint must be configured.");
            }

            if (MaxStackFrames <= 0)
            {
                throw new InvalidOperationException("MaxStackFrames must be greater than zero.");
            }

            if (MaxQueueSize <= 0)
            {
                throw new InvalidOperationException("MaxQueueSize must be greater than zero.");
            }

            if (MaxBatchSize <= 0)
            {
                throw new InvalidOperationException("MaxBatchSize must be greater than zero.");
            }
        }
    }
}
