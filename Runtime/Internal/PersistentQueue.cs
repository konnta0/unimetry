using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Unimetry.Internal
{
    internal sealed class PersistentQueue
    {
        private readonly int maxQueueSize;
        private readonly object gate = new();
        private readonly LinkedList<PendingExport> items = new();
        private readonly string storagePath;
        private readonly string keyPath;

        public PersistentQueue(int maxQueueSize)
        {
            this.maxQueueSize = maxQueueSize;
            var root = Path.Combine(Application.persistentDataPath, "unimetry");
            storagePath = Path.Combine(root, "queue.json");
            keyPath = Path.Combine(root, "offline.key");
            LoadFromDisk();
        }

        public void Enqueue(PendingExport item)
        {
            if (item == null)
            {
                return;
            }

            lock (gate)
            {
                items.AddLast(item);
                TrimLocked();
                SaveToDiskLocked();
            }
        }

        public List<PendingExport> DequeueBatch(int batchSize)
        {
            var batch = new List<PendingExport>(batchSize);
            lock (gate)
            {
                while (batch.Count < batchSize && items.Count > 0)
                {
                    batch.Add(items.First.Value);
                    items.RemoveFirst();
                }

                if (batch.Count > 0)
                {
                    SaveToDiskLocked();
                }
            }

            return batch;
        }

        public void RequeueFront(IReadOnlyList<PendingExport> failedItems)
        {
            if (failedItems == null || failedItems.Count == 0)
            {
                return;
            }

            lock (gate)
            {
                for (var index = failedItems.Count - 1; index >= 0; index--)
                {
                    items.AddFirst(failedItems[index]);
                }

                TrimLocked();
                SaveToDiskLocked();
            }
        }

        public int Count
        {
            get
            {
                lock (gate)
                {
                    return items.Count;
                }
            }
        }

        private void TrimLocked()
        {
            while (items.Count > maxQueueSize)
            {
                items.RemoveFirst();
            }
        }

        private void LoadFromDisk()
        {
            try
            {
                if (!File.Exists(storagePath))
                {
                    return;
                }

                var json = OfflineProtector.Unprotect(File.ReadAllBytes(storagePath), keyPath);
                if (string.IsNullOrEmpty(json))
                {
                    return;
                }
                var envelope = JsonUtility.FromJson<PendingExportEnvelope>(json);
                if (envelope?.Items == null)
                {
                    return;
                }

                lock (gate)
                {
                    items.Clear();
                    for (var index = 0; index < envelope.Items.Length; index++)
                    {
                        items.AddLast(envelope.Items[index]);
                    }

                    TrimLocked();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unimetry failed to load offline queue: {exception.Message}");
            }
        }

        private void SaveToDiskLocked()
        {
            try
            {
                var directory = Path.GetDirectoryName(storagePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var envelope = new PendingExportEnvelope
                {
                    Items = new PendingExport[items.Count],
                };

                var node = items.First;
                var index = 0;
                while (node != null)
                {
                    envelope.Items[index++] = node.Value;
                    node = node.Next;
                }

                var protectedBytes = OfflineProtector.Protect(JsonUtility.ToJson(envelope), keyPath);
                File.WriteAllBytes(storagePath, protectedBytes);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unimetry failed to persist offline queue: {exception.Message}");
            }
        }
    }
}
