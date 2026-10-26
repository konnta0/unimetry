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

        // The wall clock only gives millisecond precision here, so concurrent events and spans that start
        // in the same millisecond get identical timestamps (log backends such as Loki then dedupe them).
        // Anchor the wall clock to Stopwatch once and add the Stopwatch elapsed time from there.
        // Re-anchor periodically so the value does not drift away from the wall clock in long sessions.
        private static readonly long ResyncIntervalTimestamp = Stopwatch.Frequency * 60L;

        // A re-anchor can step back by the drift accumulated since the last anchor (a few milliseconds).
        // Steps smaller than this are absorbed so values stay unique and ordered; larger ones mean the
        // wall clock itself was set back, and are accepted.
        private const long MaxAbsorbedBackwardStepNanos = 1_000_000_000L;

        private static readonly object Gate = new object();
        private static bool anchored;
        private static long anchorUnixNano;
        private static long anchorTimestamp;
        private static long lastUnixNano;

        public static long UnixNano()
        {
            lock (Gate)
            {
                var now = Stopwatch.GetTimestamp();
                if (!anchored || now - anchorTimestamp >= ResyncIntervalTimestamp)
                {
                    anchorUnixNano = WallClockUnixNano();
                    anchorTimestamp = now;
                    anchored = true;
                }

                var value = anchorUnixNano + ElapsedNanos(anchorTimestamp, now);
                if (value <= lastUnixNano && lastUnixNano - value < MaxAbsorbedBackwardStepNanos)
                {
                    value = lastUnixNano + 1;
                }

                lastUnixNano = value;
                return value;
            }
        }

        private static long WallClockUnixNano()
        {
            return (DateTimeOffset.UtcNow.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) * 100L;
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
