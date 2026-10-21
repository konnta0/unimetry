using System;
using System.IO;
using UnityEngine;

namespace Unimetry.Internal
{
    internal sealed class EventBuffer
    {
        public const int MaxTags = 8;

        private readonly EventRecord[] records;
        private readonly object gate = new object();
        private readonly ResourceState resourceState;
        private readonly Func<CapturedError, CapturedError> sanitizer;
        private readonly string storagePath;
        private readonly string keyPath;
        private int head;
        private int tail;
        private int count;
        private int dropped;

        public EventBuffer(
            int capacity,
            ResourceState resourceState = null,
            Func<CapturedError, CapturedError> sanitizer = null,
            string storagePath = null)
        {
            this.resourceState = resourceState;
            this.sanitizer = sanitizer;
            var root = Path.Combine(Application.persistentDataPath, "unimetry");
            this.storagePath = storagePath ?? Path.Combine(root, "events.json");
            keyPath = Path.Combine(root, "offline.key");
            records = new EventRecord[capacity];
            for (var index = 0; index < records.Length; index++)
            {
                records[index].Tags = new EventTag[MaxTags];
                records[index].CommonTags = new EventTag[EventAttributes.MaxCount];
            }

            LoadFromDisk();
        }

        public int Dropped
        {
            get
            {
                lock (gate)
                {
                    return dropped;
                }
            }
        }

        public void WriteHandle(EventHandle handle, long endUnixNano)
        {
            lock (gate)
            {
                if (count >= records.Length)
                {
                    dropped++;
                    return;
                }

                CopyHandle(handle, endUnixNano, ref records[tail]);
                SanitizeException(ref records[tail], endUnixNano);
                records[tail].Resource = handle.Resource;
                tail++;
                if (tail == records.Length)
                {
                    tail = 0;
                }

                count++;
                SaveToDiskLocked();
            }
        }

        public void WritePoint(string name, long unixNano)
        {
            lock (gate)
            {
                if (count >= records.Length)
                {
                    dropped++;
                    return;
                }

                ref var slot = ref records[tail];
                ClearSlot(ref slot);
                slot.Name = name;
                slot.StartUnixNano = unixNano;
                slot.EndUnixNano = unixNano;
                slot.SeverityNumber = (int)UnimetrySeverity.Info;
                EventAttributes.Apply(ref slot, explicitCount: 0);
                slot.Resource = resourceState?.Capture();
                tail++;
                if (tail == records.Length)
                {
                    tail = 0;
                }

                count++;
                SaveToDiskLocked();
            }
        }

        public int CopyOldest(EventRecord[] destination)
        {
            lock (gate)
            {
                var copyCount = count;
                if (copyCount > destination.Length)
                {
                    copyCount = destination.Length;
                }

                for (var index = 0; index < copyCount; index++)
                {
                    var sourceIndex = head + index;
                    if (sourceIndex >= records.Length)
                    {
                        sourceIndex -= records.Length;
                    }

                    CopyRecord(ref records[sourceIndex], ref destination[index]);
                }

                return copyCount;
            }
        }

        internal ResourceSnapshot CaptureResource()
        {
            return resourceState?.Capture();
        }

        public void Commit(int commitCount)
        {
            lock (gate)
            {
                if (commitCount > count)
                {
                    commitCount = count;
                }

                for (var index = 0; index < commitCount; index++)
                {
                    ClearSlot(ref records[head]);
                    head++;
                    if (head == records.Length)
                    {
                        head = 0;
                    }

                    count--;
                }

                if (commitCount > 0)
                {
                    SaveToDiskLocked();
                }
            }
        }

        private static void CopyHandle(EventHandle handle, long endUnixNano, ref EventRecord slot)
        {
            slot.Name = handle.Name;
            slot.StartUnixNano = handle.StartUnixNano;
            slot.EndUnixNano = endUnixNano;
            slot.SeverityNumber = handle.ExceptionType == null
                ? (int)UnimetrySeverity.Info
                : (int)UnimetrySeverity.Error;
            slot.ExceptionType = handle.ExceptionType;
            slot.ExceptionMessage = handle.ExceptionMessage;
            slot.ExceptionStack = handle.ExceptionStack;
            slot.ExceptionEscaped = handle.ExceptionEscaped;
            slot.DroppedTags = handle.DroppedTags;
            var tagCount = handle.TagCount;
            if (tagCount > MaxTags)
            {
                tagCount = MaxTags;
            }

            slot.TagCount = tagCount;
            for (var index = 0; index < tagCount; index++)
            {
                slot.Tags[index] = handle.Tags[index];
            }

            EventAttributes.Apply(ref slot, tagCount);
        }

        private static void CopyRecord(ref EventRecord source, ref EventRecord destination)
        {
            destination.Name = source.Name;
            destination.StartUnixNano = source.StartUnixNano;
            destination.EndUnixNano = source.EndUnixNano;
            destination.SeverityNumber = source.SeverityNumber;
            destination.ExceptionType = source.ExceptionType;
            destination.ExceptionMessage = source.ExceptionMessage;
            destination.ExceptionStack = source.ExceptionStack;
            destination.ExceptionEscaped = source.ExceptionEscaped;
            destination.DroppedTags = source.DroppedTags;
            destination.TagCount = source.TagCount;
            for (var index = 0; index < source.TagCount; index++)
            {
                destination.Tags[index] = source.Tags[index];
            }

            destination.CommonTagCount = source.CommonTagCount;
            destination.Resource = source.Resource;
            if (destination.CommonTags == null || source.CommonTags == null)
            {
                return;
            }

            var commonCount = source.CommonTagCount;
            if (commonCount > destination.CommonTags.Length)
            {
                commonCount = destination.CommonTags.Length;
            }

            for (var index = 0; index < commonCount; index++)
            {
                destination.CommonTags[index] = source.CommonTags[index];
            }
        }

        private static void ClearSlot(ref EventRecord slot)
        {
            slot.Name = null;
            slot.StartUnixNano = 0;
            slot.EndUnixNano = 0;
            slot.SeverityNumber = 0;
            slot.ExceptionType = null;
            slot.ExceptionMessage = null;
            slot.ExceptionStack = null;
            slot.ExceptionEscaped = false;
            slot.DroppedTags = 0;
            slot.Resource = null;
            var tagCount = slot.TagCount;
            slot.TagCount = 0;
            if (slot.Tags == null)
            {
                return;
            }

            for (var index = 0; index < tagCount; index++)
            {
                slot.Tags[index] = default;
            }

            var commonCount = slot.CommonTagCount;
            slot.CommonTagCount = 0;
            if (slot.CommonTags == null)
            {
                return;
            }

            for (var index = 0; index < commonCount; index++)
            {
                slot.CommonTags[index] = default;
            }
        }

        private void SanitizeException(ref EventRecord record, long capturedAtUnixNano)
        {
            if (sanitizer == null || string.IsNullOrEmpty(record.ExceptionType))
            {
                return;
            }

            try
            {
                var capturedAt = DateTimeOffset.FromUnixTimeMilliseconds(capturedAtUnixNano / 1_000_000L);
                var captured = new CapturedError(
                    record.ExceptionMessage,
                    CapturedErrorSeverity.Exception,
                    CapturedErrorSource.Event,
                    record.ExceptionType,
                    record.ExceptionStack,
                    string.Empty,
                    record.ExceptionEscaped,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    capturedAt);
                var sanitized = sanitizer(captured) ?? captured;
                record.ExceptionType = sanitized.ExceptionType;
                record.ExceptionMessage = sanitized.Message;
                record.ExceptionStack = sanitized.StackTrace;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Unimetry event sanitizer failed: " + exception.Message);
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
                var envelope = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<EventRecordEnvelope>(json);
                if (envelope?.Items == null)
                {
                    return;
                }

                dropped = envelope.Dropped;
                var start = Math.Max(0, envelope.Items.Length - records.Length);
                for (var index = start; index < envelope.Items.Length; index++)
                {
                    ref var slot = ref records[tail];
                    CopyLoaded(envelope.Items[index], ref slot);
                    tail = (tail + 1) % records.Length;
                    count++;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Unimetry failed to load persisted events: " + exception.Message);
            }
        }

        private void SaveToDiskLocked()
        {
            try
            {
                if (count == 0)
                {
                    AtomicFile.TryDelete(storagePath);
                    return;
                }

                var envelope = new EventRecordEnvelope
                {
                    Items = new EventRecord[count],
                    Dropped = dropped,
                };
                for (var index = 0; index < count; index++)
                {
                    var sourceIndex = (head + index) % records.Length;
                    envelope.Items[index] = records[sourceIndex];
                }

                var directory = Path.GetDirectoryName(storagePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(
                    storagePath,
                    OfflineProtector.Protect(JsonUtility.ToJson(envelope), keyPath));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Unimetry failed to persist events: " + exception.Message);
            }
        }

        private static void CopyLoaded(EventRecord source, ref EventRecord destination)
        {
            ClearSlot(ref destination);
            destination.Name = source.Name;
            destination.StartUnixNano = source.StartUnixNano;
            destination.EndUnixNano = source.EndUnixNano;
            destination.SeverityNumber = source.SeverityNumber;
            destination.ExceptionType = source.ExceptionType;
            destination.ExceptionMessage = source.ExceptionMessage;
            destination.ExceptionStack = source.ExceptionStack;
            destination.ExceptionEscaped = source.ExceptionEscaped;
            destination.DroppedTags = source.DroppedTags;
            destination.Resource = source.Resource;
            destination.TagCount = Math.Min(source.TagCount, MaxTags);
            for (var index = 0; index < destination.TagCount && source.Tags != null; index++)
            {
                destination.Tags[index] = source.Tags[index];
            }

            destination.CommonTagCount = Math.Min(source.CommonTagCount, EventAttributes.MaxCount);
            for (var index = 0; index < destination.CommonTagCount && source.CommonTags != null; index++)
            {
                destination.CommonTags[index] = source.CommonTags[index];
            }
        }
    }
}
