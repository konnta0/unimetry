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
        private CancellationTokenSource cancellationTokenSource;
        private UnimetryDispatcherBehaviour dispatcher;
        private bool started;
        private bool disposed;
        private float nextFlushTime;
        private int flushInProgress;

        public BackgroundFlusher(PersistentQueue queue, OtlpExporter exporter, UnimetryOptions options)
        {
            this.queue = queue;
            this.exporter = exporter;
            this.options = options;
        }

        public void Start()
        {
            if (started)
            {
                return;
            }

            started = true;
            cancellationTokenSource = new CancellationTokenSource();
            nextFlushTime = Time.realtimeSinceStartup + (float)options.FlushInterval.TotalSeconds;
            EnsureDispatcher();
            Application.quitting += HandleApplicationQuitting;
        }

        public void Tick()
        {
            if (disposed || Time.realtimeSinceStartup < nextFlushTime)
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
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batch = queue.DequeueBatch(options.MaxBatchSize);
                    if (batch.Count == 0)
                    {
                        return;
                    }

                    var exported = await exporter.ExportAsync(batch, cancellationToken).ConfigureAwait(true);
                    if (!exported)
                    {
                        queue.RequeueFront(batch);
                        return;
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
