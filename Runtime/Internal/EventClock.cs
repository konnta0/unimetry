using System;
using System.Diagnostics;

namespace Unimetry.Internal
{
    internal static class EventClock
    {
        public static long Timestamp()
        {
            return Stopwatch.GetTimestamp();
        }

        public static long UnixNano()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L;
        }

        public static long AddElapsedNanos(long startUnixNano, long startTimestamp, long endTimestamp)
        {
            return startUnixNano + ElapsedNanos(startTimestamp, endTimestamp);
        }

        public static long ElapsedNanos(long startTimestamp, long endTimestamp)
        {
            var delta = endTimestamp - startTimestamp;
            if (delta <= 0)
            {
                return 0;
            }

            var frequency = Stopwatch.Frequency;
            return (delta / frequency) * 1_000_000_000L + (delta % frequency) * 1_000_000_000L / frequency;
        }
    }
}
