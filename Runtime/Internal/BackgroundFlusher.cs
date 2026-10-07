using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Unimetry.Internal
{
    internal sealed class BackgroundFlusher : IDisposable
    {
        private readonly PersistentQueue queue;
        private readonly OtlpExporter exporter;
        private readonly UnimetryOptions options;
        private readonly EventBuffer eventBuffer;
        private readonly Action beforeFlush;
        private readonly EventRecord[] eventBatch;
        private CancellationTokenSource cancellationTokenSource;
        private UnimetryDispatcherBehaviour dispatcher;
        private bool started;
        private bool disposed;
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
            Action beforeFlush)
        {
            this.queue = queue;
            this.exporter = exporter;
            this.options = options;
            this.eventBuffer = eventBuffer;
            this.beforeFlush = beforeFlush;
            eventBatch = new EventRecord[options.MaxBatchSize];
            for (var index = 0; index < eventBatch.Length; index++)
            {
                eventBatch[index].Tags = new EventTag[EventBuffer.MaxTags];
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
        }

        public void Tick()
        {
            if (disposed)
            {
                return;
            }

            beforeFlush?.Invoke();
            SampleFrame();
            if (Time.realtimeSinceStartup < nextFlushTime)
            {
                return;
            }

            nextFlushTime = Time.realtimeSinceStartup + (float)options.FlushInterval.TotalSeconds;
            _ = FlushAsync(cancellationTokenSource.Token);
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref flushInProgress, 1, 0) != 0)
            {
                return;
            }

            try
            {
                beforeFlush?.Invoke();
                await ExportMetricsAsync(cancellationToken).ConfigureAwait(true);
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batch = queue.DequeueBatch(options.MaxBatchSize);
                    var eventCount = eventBuffer == null ? 0 : eventBuffer.CopyOldest(eventBatch);
                    if (batch.Count == 0 && eventCount == 0)
                    {
                        return;
                    }

                    var exported = await exporter.ExportAsync(batch, eventBatch, eventCount, cancellationToken).ConfigureAwait(true);
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

        private async Task ExportMetricsAsync(CancellationToken cancellationToken)
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
                options,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L,
                frames,
                GC.GetTotalMemory(false),
                startupSeconds);
            var sent = await exporter.ExportMetricsAsync(payload, cancellationToken).ConfigureAwait(true);
            if (!sent)
            {
                Debug.LogWarning("Unimetry metrics export failed.");
            }
        }

        private void HandleApplicationQuitting()
        {
            _ = FlushAsync(CancellationToken.None);
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
    }
}
