using System;
using System.Threading;
using System.Threading.Tasks;
#if UNIMETRY_UNITASK
using Cysharp.Threading.Tasks;
#endif
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
        private static bool shuttingDown;
#if !UNIMETRY_UNITASK
        private static SynchronizationContext mainThreadContext;
        private static TaskScheduler mainThreadScheduler;
#endif

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

            UnimetryRuntime created;
            lock (Gate)
            {
                if (runtime != null)
                {
                    throw new InvalidOperationException("Unimetry is already initialized.");
                }

                created = new UnimetryRuntime(options);
                created.Start();
#if !UNIMETRY_UNITASK
                CaptureMainThreadContext();
#endif
                Volatile.Write(ref runtime, created);
            }

            created.IngestPreviousCrashes();
        }

        /// <summary>
        /// Records a breadcrumb for the next native-crash artifact.
        /// Does nothing when Unimetry is not initialized or <see cref="UnimetryOptions.CaptureNativeCrashes"/> is false.
        /// </summary>
        /// <param name="message">Short description. Newlines are replaced with spaces and the text is truncated.</param>
        public static void AddBreadcrumb(string message)
        {
            Volatile.Read(ref runtime)?.AddBreadcrumb(message);
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
            var current = GetRuntimeOrThrow();
#if UNIMETRY_UNITASK
            return FlushOnMainThreadAsync(current, cancellationToken).AsTask();
#else
            return RunOnMainThread(() => current.FlushAsync(cancellationToken));
#endif
        }

#if UNIMETRY_UNITASK
        /// <summary>
        /// Flushes queued telemetry using UniTask without allocating a compatibility <see cref="Task"/>.
        /// This API is available when UniTask 2.x is installed in the Unity project.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A UniTask that completes when the flush attempt finishes.</returns>
        public static UniTask FlushUniTaskAsync(CancellationToken cancellationToken = default)
        {
            return FlushOnMainThreadAsync(GetRuntimeOrThrow(), cancellationToken);
        }

        private static async UniTask FlushOnMainThreadAsync(
            UnimetryRuntime current,
            CancellationToken cancellationToken)
        {
            // OtlpExporter creates UnityWebRequest objects, which only work on the main thread.
            await UniTask.SwitchToMainThread(cancellationToken);
            await current.FlushAsync(cancellationToken);
        }
#endif

        /// <summary>
        /// Sets or replaces a resource attribute for telemetry recorded after this call.
        /// Passing <see langword="null"/> removes the attribute. Already queued records retain
        /// the resource snapshot captured when they were recorded.
        /// </summary>
        public static void SetResourceAttribute(string key, string value)
        {
            GetRuntimeOrThrow().SetResourceAttribute(key, value);
        }

        /// <summary>
        /// Flushes queued telemetry, then stops capture hooks and releases runtime resources.
        /// </summary>
        public static Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
#if UNIMETRY_UNITASK
            return ShutdownCoreAsync(cancellationToken).AsTask();
#else
            return RunOnMainThread(() => ShutdownCoreAsync(cancellationToken));
#endif
        }

#if UNIMETRY_UNITASK
        /// <summary>
        /// Flushes queued telemetry and shuts down using UniTask without allocating a compatibility <see cref="Task"/>.
        /// This API is available when UniTask 2.x is installed in the Unity project.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static UniTask ShutdownUniTaskAsync(CancellationToken cancellationToken = default)
        {
            return ShutdownCoreAsync(cancellationToken);
        }

        private static async UniTask ShutdownCoreAsync(CancellationToken cancellationToken)
#else
        private static async Task ShutdownCoreAsync(CancellationToken cancellationToken)
#endif
        {
#if UNIMETRY_UNITASK
            // The final flush creates UnityWebRequest objects and Dispose destroys the dispatcher,
            // so both operations need the main thread.
            await UniTask.SwitchToMainThread(cancellationToken);
#endif
            UnimetryRuntime current;
            lock (Gate)
            {
                if (shuttingDown)
                {
                    throw new InvalidOperationException("Unimetry shutdown is already in progress.");
                }

                current = runtime;
                if (current != null)
                {
                    shuttingDown = true;
                }
            }

            if (current == null)
            {
                Internal.EventPipeline.Configure(null, null, false);
                Internal.MetricPipeline.Configure(null, false);
                return;
            }

            try
            {
                await current.FlushAsync(cancellationToken);
            }
            finally
            {
                lock (Gate)
                {
                    try
                    {
                        if (ReferenceEquals(runtime, current))
                        {
                            Volatile.Write(ref runtime, null);
                        }

                        current.Dispose();
                    }
                    finally
                    {
                        shuttingDown = false;
                    }
                }
            }
        }

        /// <summary>
        /// Stops capture hooks and releases runtime resources.
        /// Queued errors, spans, and events remain encrypted on disk for the next initialization.
        /// Use <see cref="ShutdownAsync"/> when the caller can await a final network flush.
        /// </summary>
        public static void Shutdown()
        {
            lock (Gate)
            {
                if (shuttingDown)
                {
                    throw new InvalidOperationException("Unimetry shutdown is already in progress.");
                }

                var current = runtime;
                Volatile.Write(ref runtime, null);
                if (current != null)
                {
                    current.Dispose();
                }
                else
                {
                    Internal.EventPipeline.Configure(null, null, false);
                    Internal.MetricPipeline.Configure(null, false);
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

#if !UNIMETRY_UNITASK
        private static void CaptureMainThreadContext()
        {
            // Initialize runs on the main thread because it creates the dispatcher MonoBehaviour.
            mainThreadContext = SynchronizationContext.Current;
            mainThreadScheduler = mainThreadContext == null
                ? null
                : TaskScheduler.FromCurrentSynchronizationContext();
        }

        private static Task RunOnMainThread(Func<Task> operation)
        {
            // OtlpExporter creates UnityWebRequest objects, which only work on the main thread.
            var scheduler = mainThreadScheduler;
            if (scheduler == null || SynchronizationContext.Current == mainThreadContext)
            {
                return operation();
            }

            return Task.Factory.StartNew(
                    operation,
                    CancellationToken.None,
                    TaskCreationOptions.None,
                    scheduler)
                .Unwrap();
        }
#endif
    }
}
