using System;

namespace Unimetry.Internal
{
    internal static class EventHandlePool
    {
        private const int Capacity = 16;

        [ThreadStatic]
        private static EventHandle[] handles;

        [ThreadStatic]
        private static int count;

        public static EventHandle Rent(string name, long startTimestamp, long startUnixNano)
        {
            EventHandle handle;
            if (handles != null && count > 0)
            {
                count--;
                handle = handles[count];
                handles[count] = null;
            }
            else
            {
                handle = new EventHandle();
            }

            handle.Activate(name, startTimestamp, startUnixNano);
            return handle;
        }

        public static void Return(EventHandle handle)
        {
            if (handle == null)
            {
                return;
            }

            handle.Clear();
            if (handles == null)
            {
                handles = new EventHandle[Capacity];
            }

            if (count >= handles.Length)
            {
                return;
            }

            handles[count] = handle;
            count++;
        }
    }
}
