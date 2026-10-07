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

        /// <summary>
        /// When true, <see cref="UnimetryEvent"/> records are buffered and exported as OTLP log events.
        /// </summary>
        public bool CaptureEvents { get; set; } = true;

        /// <summary>
        /// When true, flush exports FPS, managed memory, and startup duration as OTLP gauges.
        /// </summary>
        public bool CaptureMetrics { get; set; } = true;

        /// <summary>
        /// Maximum number of completed events retained in memory. Additional events are counted and dropped.
        /// </summary>
        public int MaxEventBuffer { get; set; } = 1024;

        /// <summary>
        /// Optional writer invoked for captured errors and completed events.
        /// </summary>
        public UnimetryLogWriter LogWriter { get; set; }

        /// <summary>
        /// When true, Windows standalone players record native crashes and upload them on the next launch.
        /// Leave this false until the game's own consent UI allows crash collection.
        /// A launch with this set to false deletes crash artifacts and breadcrumb files left on disk.
        /// </summary>
        public bool CaptureNativeCrashes { get; set; }

        /// <summary>
        /// When true, a Windows native crash also writes a minidump next to the crash artifact.
        /// The dump is size-capped and is not embedded in the OTLP payload.
        /// </summary>
        public bool CaptureMinidumps { get; set; }

        /// <summary>
        /// Maximum minidump size in bytes. Larger dumps are discarded. The default is 4 MiB and the maximum is 32 MiB.
        /// </summary>
        public int MaxMinidumpBytes { get; set; } = 4 * 1024 * 1024;

        /// <summary>
        /// Maximum number of breadcrumbs kept for the next native crash. Valid range is 1 through 256.
        /// </summary>
        public int BreadcrumbCapacity { get; set; } = 64;

        /// <summary>
        /// Breadcrumbs older than this window are dropped. The default is 30 seconds.
        /// </summary>
        public TimeSpan BreadcrumbWindow { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Sends captured errors and events to <see cref="UnityEngine.Debug.unityLogger"/>,
        /// which honors a user-installed log handler.
        /// </summary>
        /// <returns>This options instance.</returns>
        public UnimetryOptions WithConsoleLog()
        {
            LogWriter = Internal.UnimetryConsoleLog.Write;
            return this;
        }

        /// <summary>
        /// Sends captured errors and events to <paramref name="writer"/>.
        /// </summary>
        /// <param name="writer">User logging callback. Invoked only when a record is produced.</param>
        /// <returns>This options instance.</returns>
        public UnimetryOptions WithLog(UnimetryLogWriter writer)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            LogWriter = writer;
            return this;
        }

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

            if (MaxEventBuffer <= 0)
            {
                throw new InvalidOperationException("MaxEventBuffer must be greater than zero.");
            }

            if (BreadcrumbCapacity <= 0 || BreadcrumbCapacity > 256)
            {
                throw new InvalidOperationException("BreadcrumbCapacity must be from 1 through 256.");
            }

            if (BreadcrumbWindow <= TimeSpan.Zero)
            {
                throw new InvalidOperationException("BreadcrumbWindow must be greater than zero.");
            }

            if (MaxMinidumpBytes <= 0 || MaxMinidumpBytes > 32 * 1024 * 1024)
            {
                throw new InvalidOperationException("MaxMinidumpBytes must be from 1 through 33554432.");
            }
        }
    }
}
