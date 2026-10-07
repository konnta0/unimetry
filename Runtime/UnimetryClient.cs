using System;
using System.Threading;
using System.Threading.Tasks;
using Unimetry.Internal;

namespace Unimetry
{
    /// <summary>
    /// Public entry point for initializing Unimetry and reporting errors manually.
    /// </summary>
    public static class UnimetryClient
    {
        private static readonly object Gate = new();
        private static UnimetryRuntime runtime;

        /// <summary>
        /// Gets whether Unimetry has been initialized.
        /// </summary>
        public static bool IsInitialized
        {
            get
            {
                lock (Gate)
                {
                    return runtime != null;
                }
            }
        }

        /// <summary>
        /// Initializes Unimetry with the supplied options and starts automatic error capture.
        /// </summary>
        /// <param name="options">Runtime configuration.</param>
        public static void Initialize(UnimetryOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options.Validate();

            lock (Gate)
            {
                if (runtime != null)
                {
                    throw new InvalidOperationException("Unimetry is already initialized.");
                }

                runtime = new UnimetryRuntime(options);
                runtime.Start();
            }
        }

        /// <summary>
        /// Reports an exception or message explicitly.
        /// </summary>
        /// <param name="exception">Exception to report.</param>
        /// <param name="message">Optional override message.</param>
        public static void Report(Exception exception, string message = null)
        {
            var activeRuntime = GetRuntimeOrThrow();
            activeRuntime.ReportManual(exception, message);
        }

        /// <summary>
        /// Flushes queued telemetry to the configured OTLP endpoint.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when the flush attempt finishes.</returns>
        public static Task FlushAsync(CancellationToken cancellationToken = default)
        {
            return GetRuntimeOrThrow().FlushAsync(cancellationToken);
        }

        /// <summary>
        /// Stops capture hooks and releases runtime resources.
        /// Events still sitting in memory are discarded. Call <see cref="FlushAsync"/> first to export them.
        /// </summary>
        public static void Shutdown()
        {
            lock (Gate)
            {
                if (runtime != null)
                {
                    runtime.Dispose();
                    runtime = null;
                }
                else
                {
                    Internal.EventPipeline.Configure(null, null, false);
                }
            }
        }

        private static UnimetryRuntime GetRuntimeOrThrow()
        {
            lock (Gate)
            {
                if (runtime == null)
                {
                    throw new InvalidOperationException("Unimetry has not been initialized.");
                }

                return runtime;
            }
        }
    }
}
