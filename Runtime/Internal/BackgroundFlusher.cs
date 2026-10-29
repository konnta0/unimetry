using System;
using System.Threading;
#if UNIMETRY_UNITASK
using AsyncTask = Cysharp.Threading.Tasks.UniTask;
using Cysharp.Threading.Tasks;
#else
using System.Threading.Tasks;
using AsyncTask = System.Threading.Tasks.Task;
#endif
using UnityEngine;

namespace Unimetry.Internal
{
    internal sealed class BackgroundFlusher : IDisposable
    {
        private readonly PersistentQueue queue;
        private readonly OtlpExporter exporter;
        private readonly UnimetryOptions options;
        private readonly EventBuffer eventBuffer;
        private readonly ResourceState resourceState;
        private readonly Action beforeFlush;
        private readonly EventRecord[] eventBatch;
        private CancellationTokenSource cancellationTokenSource;
        private UnimetryDispatcherBehaviour dispatcher;
        private bool started;
        private bool disposed;
        private bool quitting;
        private float nextFlushTime;
        private int flushInProgress;
        private float fpsSum;
        private int fpsCount;
        private float startupSeconds;

        public BackgroundFlusher(
            PersistentQueue queue,
            OtlpExporter exporter,
            UnimetryOptions options,
            EventBuffer eventBuffer,
            ResourceState resourceState,
            Action beforeFlush)
        {
            this.queue = queue;
            this.exporter = exporter;
            this.options = options;
            this.eventBuffer = eventBuffer;
            this.resourceState = resourceState;
            this.beforeFlush = beforeFlush;
            eventBatch = new EventRecord[options.MaxBatchSize];
            for (var index = 0; index < eventBatch.Length; index++)
            {
                eventBatch[index].Tags = new EventTag[EventBuffer.MaxTags];
                eventBatch[index].CommonTags = new EventTag[EventAttributes.MaxCount];
            }
        }

        public void Start()
        {
            if (started)
            {
                return;
            }

            started = true;
            startupSeconds = Time.realtimeSinceStartup;
            cancellationTokenSource = new CancellationTokenSource();
            nextFlushTime = Time.realtimeSinceStartup + (float)options.FlushInterval.TotalSeconds;
            EnsureDispatcher();
            Application.quitting += HandleApplicationQuitting;
            // Outside Play Mode the dispatcher's Update does not run, so deferred events would only be
            // saved by Dispose, and a domain reload skips Dispose.
            if (!Application.isPlaying)
            {
                eventBuffer?.SetSaveImmediately(true);
            }
        }

        public void Tick()
        {
            if (disposed)
            {
                return;
            }

            eventBuffer?.SaveIfDue();
            beforeFlush?.Invoke();
            SampleFrame();
            if (Time.realtimeSinceStartup < nextFlushTime)
            {
                return;
            }

            nextFlushTime = Time.realtimeSinceStartup + (float)options.FlushInterval.TotalSeconds;
#if UNIMETRY_UNITASK
            FlushAsync(cancellationTokenSource.Token).Forget();
#else
            _ = FlushAsync(cancellationTokenSource.Token);
#endif
        }

        public async AsyncTask FlushAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref flushInProgress, 1, 0) != 0)
            {
                return;
            }

            try
            {
                beforeFlush?.Invoke();
                await ExportMetricsAsync(cancellationToken);
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batch = queue.DequeueBatch(options.MaxBatchSize);
                    var eventCount = eventBuffer == null ? 0 : eventBuffer.CopyOldest(eventBatch);
                    if (batch.Count == 0 && eventCount == 0)
                    {
                        return;
                    }

                    bool exported;
                    try
                    {
                        exported = await exporter.ExportAsync(batch, eventBatch, eventCount, cancellationToken);
                    }
                    catch (Exception exception)
                    {
                        queue.RequeueFront(batch);
                        if (!(exception is OperationCanceledException))
                        {
                            Debug.LogWarning("Unimetry OTLP export failed: " + exception.Message);
                        }

                        throw;
                    }

                    if (!exported)
                    {
                        queue.RequeueFront(batch);
                        return;
                    }

                    if (eventCount > 0)
                    {
                        eventBuffer.Commit(eventCount);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref flushInProgress, 0);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            // Shutdown leaves unsent events on disk, so deferred ones are saved now and any event that
            // still reaches the buffer afterwards is saved as it is written.
            eventBuffer?.SetSaveImmediately(true);
            Application.quitting -= HandleApplicationQuitting;
            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;

            if (dispatcher != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(dispatcher.gameObject);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(dispatcher.gameObject);
                }

                dispatcher = null;
            }
        }

        private void SampleFrame()
        {
            if (!options.CaptureMetrics)
            {
                return;
            }

            var delta = Time.unscaledDeltaTime;
            if (delta <= 0f)
            {
                return;
            }

            fpsSum += 1f / delta;
            fpsCount++;
        }

        private async AsyncTask ExportMetricsAsync(CancellationToken cancellationToken)
        {
            if (!options.CaptureMetrics)
            {
                return;
            }

            var frames = double.NaN;
            if (fpsCount > 0)
            {
                frames = fpsSum / fpsCount;
                fpsSum = 0f;
                fpsCount = 0;
            }

            var payload = OtlpJsonWriter.BuildMetricsPayload(
                resourceState?.Capture(),
                options,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L,
                frames,
                GC.GetTotalMemory(false),
                startupSeconds);
            var sent = await exporter.ExportMetricsAsync(payload, cancellationToken);
            if (!sent)
            {
                Debug.LogWarning("Unimetry metrics export failed.");
            }
        }

        private void HandleApplicationQuitting()
        {
            // Events still end during scene teardown (OnDisable / OnDestroy) after quitting starts, and no
            // later frame runs to save them.
            quitting = true;
            eventBuffer?.SetSaveImmediately(true);
#if UNIMETRY_UNITASK
            FlushAsync(CancellationToken.None).Forget();
#else
            _ = FlushAsync(CancellationToken.None);
#endif
        }

        /// <summary>
        /// Saves events as they are written while the application is paused, because a paused
        /// application may be killed without another frame.
        /// </summary>
        public void HandleApplicationPause(bool paused)
        {
            if (disposed || quitting)
            {
                return;
            }

            eventBuffer?.SetSaveImmediately(paused);
        }

        private void EnsureDispatcher()
        {
            if (dispatcher != null)
            {
                return;
            }

            var existing = GameObject.Find("UnimetryDispatcher");
            if (existing != null &&
                existing.TryGetComponent<UnimetryDispatcherBehaviour>(out var existingBehaviour))
            {
                dispatcher = existingBehaviour;
                dispatcher.Initialize(this);
                return;
            }

            var gameObject = new GameObject("UnimetryDispatcher");
            if (Application.isPlaying)
            {
                UnityEngine.Object.DontDestroyOnLoad(gameObject);
            }

            gameObject.hideFlags = HideFlags.HideAndDontSave;
            dispatcher = gameObject.AddComponent<UnimetryDispatcherBehaviour>();
            dispatcher.Initialize(this);
        }
    }

    internal sealed class UnimetryDispatcherBehaviour : MonoBehaviour
    {
        private BackgroundFlusher owner;

        public void Initialize(BackgroundFlusher flusher)
        {
            owner = flusher;
        }

        private void Update()
        {
            owner?.Tick();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            owner?.HandleApplicationPause(pauseStatus);
        }
    }
}
