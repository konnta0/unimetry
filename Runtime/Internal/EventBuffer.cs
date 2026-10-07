namespace Unimetry.Internal
{
    internal sealed class EventBuffer
    {
        public const int MaxTags = 8;

        private readonly EventRecord[] records;
        private readonly object gate = new object();
        private int head;
        private int tail;
        private int count;
        private int dropped;

        public EventBuffer(int capacity)
        {
            records = new EventRecord[capacity];
            for (var index = 0; index < records.Length; index++)
            {
                records[index].Tags = new EventTag[MaxTags];
            }
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
                tail++;
                if (tail == records.Length)
                {
                    tail = 0;
                }

                count++;
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
                tail++;
                if (tail == records.Length)
                {
                    tail = 0;
                }

                count++;
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
        }
    }
}
