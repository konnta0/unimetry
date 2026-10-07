using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Unimetry.Internal
{
    internal sealed class UnimetryRuntime : IDisposable
    {
        private readonly UnimetryOptions options;
        private readonly ErrorCapture errorCapture;
        private readonly PersistentQueue queue;
        private readonly BackgroundFlusher flusher;
        private readonly OtlpExporter exporter;
        private readonly EventBuffer eventBuffer;
        private readonly CrashArtifactStore crashStore;
        private readonly BreadcrumbLog breadcrumbs;
        private int crashIngested;
        private bool started;

        public UnimetryRuntime(UnimetryOptions options)
        {
            this.options = options;
            queue = new PersistentQueue(options.MaxQueueSize);
            exporter = new OtlpExporter(options);
            eventBuffer = new EventBuffer(options.MaxEventBuffer);
            var root = Path.Combine(Application.persistentDataPath, "unimetry");
            crashStore = new CrashArtifactStore(Path.Combine(root, "crashes"));
            breadcrumbs = new BreadcrumbLog(
                options.BreadcrumbCapacity,
                options.BreadcrumbWindow,
                Path.Combine(root, "breadcrumbs.txt"));
            flusher = new BackgroundFlusher(queue, exporter, options, eventBuffer, IngestPreviousCrashes);
            errorCapture = new ErrorCapture(options, EnqueueCapturedError);
        }

        public void Start()
        {
            if (started)
            {
                return;
            }

            started = true;
            EventPipeline.Configure(eventBuffer, options.LogWriter, options.CaptureEvents);
            TraceContext.SetSpanRecorder(EnqueueSpan);
            if (options.CaptureNativeCrashes)
            {
                crashStore.EnsureDirectory();
                var session = CrashSession.FromUnity();
                WindowsCrashHandler.Install(
                    crashStore,
                    breadcrumbs,
                    session,
                    options.CaptureMinidumps,
                    options.MaxMinidumpBytes);
                PosixCrashHandler.Install(crashStore, breadcrumbs, session);
            }

            errorCapture.Start();
            flusher.Start();
        }

        public void AddBreadcrumb(string message)
        {
            if (!options.CaptureNativeCrashes)
            {
                return;
            }

            breadcrumbs.Add(message);
        }

        public void ReportManual(Exception exception, string message)
        {
            errorCapture.CaptureManual(exception, message);
        }

        public void IngestPreviousCrashes()
        {
            if (Interlocked.Exchange(ref crashIngested, 1) != 0)
            {
                return;
            }

            try
            {
                if (!options.CaptureNativeCrashes)
                {
                    crashStore.DeleteAll();
                    breadcrumbs.DeleteSnapshot();
                    return;
                }

                CrashIngest.EnqueuePending(crashStore, queue, options);
                WindowsCrashHandler.EnsureInstalled();
            }
            catch (Exception exception)
            {
                Interlocked.Exchange(ref crashIngested, 0);
                Debug.LogWarning("Unimetry failed to read native crash artifacts: " + exception.Message);
            }
        }

        public Task FlushAsync(CancellationToken cancellationToken)
        {
            return flusher.FlushAsync(cancellationToken);
        }

        public void Dispose()
        {
            WindowsCrashHandler.Uninstall();
            PosixCrashHandler.Uninstall();
            TraceContext.Clear();
            EventPipeline.Configure(null, null, false);
            errorCapture.Dispose();
            flusher.Dispose();
        }

        private void EnqueueCapturedError(CapturedError capturedError, string parentSpanId)
        {
            if (capturedError == null)
            {
                return;
            }

            var sanitized = options.Sanitizer?.Invoke(capturedError) ?? capturedError;
            var unixNano = sanitized.CapturedAtUtc.ToUnixTimeMilliseconds() * 1_000_000L;
            var severity = sanitized.Severity == CapturedErrorSeverity.Fatal
                ? UnimetrySeverity.Fatal
                : UnimetrySeverity.Error;
            var entry = new UnimetryLogEntry(
                UnimetryLogKind.Log,
                "error",
                sanitized.Message,
                severity,
                unixNano,
                unixNano);
            EventPipeline.EmitLog(in entry);
            var export = PendingExport.FromCapturedError(sanitized, options.ExportErrorSpans);
            export.ParentSpanId = parentSpanId ?? string.Empty;
            export.Baggage = TraceContext.FormatBaggage();
            queue.Enqueue(export);
        }

        private void EnqueueSpan(PendingExport export)
        {
            if (export == null || !options.ExportErrorSpans)
            {
                return;
            }

            queue.Enqueue(export);
        }
    }

    internal static class UnimetryBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitializeFromEnvironment()
        {
            var endpoint = Environment.GetEnvironmentVariable("UNIMETRY_OTLP_ENDPOINT");
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                TryInitializeFromSettings();
                return;
            }

            var options = new UnimetryOptions
            {
                Endpoint = endpoint,
                ServiceName = Environment.GetEnvironmentVariable("UNIMETRY_SERVICE_NAME") ?? Application.productName,
                ServiceVersion = Environment.GetEnvironmentVariable("UNIMETRY_SERVICE_VERSION") ?? Application.version,
                DeploymentEnvironment = Environment.GetEnvironmentVariable("UNIMETRY_DEPLOYMENT_ENVIRONMENT") ?? "production",
            };

            var allowInsecure = Environment.GetEnvironmentVariable("UNIMETRY_ALLOW_INSECURE_TLS");
            if (string.Equals(allowInsecure, "1", StringComparison.Ordinal) ||
                string.Equals(allowInsecure, "true", StringComparison.OrdinalIgnoreCase))
            {
                options.AllowInsecureTls = true;
            }

            try
            {
                UnimetryClient.Initialize(options);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unimetry auto-initialization failed: {exception.Message}");
            }
        }

        private static void TryInitializeFromSettings()
        {
            if (UnimetryClient.IsInitialized)
            {
                return;
            }

            var settings = Resources.Load<UnimetrySettings>("UnimetrySettings");
            if (settings == null || string.IsNullOrWhiteSpace(settings.Endpoint))
            {
                return;
            }

            try
            {
                UnimetryClient.Initialize(settings.ToOptions());
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unimetry settings initialization failed: {exception.Message}");
            }
        }
    }
}
