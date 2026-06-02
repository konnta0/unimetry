using System;
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
        private bool started;

        public UnimetryRuntime(UnimetryOptions options)
        {
            this.options = options;
            queue = new PersistentQueue(options.MaxQueueSize);
            exporter = new OtlpExporter(options);
            flusher = new BackgroundFlusher(queue, exporter, options);
            errorCapture = new ErrorCapture(options, EnqueueCapturedError);
        }

        public void Start()
        {
            if (started)
            {
                return;
            }

            started = true;
            errorCapture.Start();
            flusher.Start();
        }

        public void ReportManual(Exception exception, string message)
        {
            errorCapture.CaptureManual(exception, message);
        }

        public Task FlushAsync(CancellationToken cancellationToken)
        {
            return flusher.FlushAsync(cancellationToken);
        }

        public void Dispose()
        {
            errorCapture.Dispose();
            flusher.Dispose();
        }

        private void EnqueueCapturedError(CapturedError capturedError)
        {
            if (capturedError == null)
            {
                return;
            }

            var sanitized = options.Sanitizer?.Invoke(capturedError) ?? capturedError;
            queue.Enqueue(PendingExport.FromCapturedError(sanitized, options.ExportErrorSpans));
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
    }
}
