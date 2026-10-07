using System;
using System.Text;
using System.Threading;

namespace Unimetry.Internal
{
    internal sealed class BreadcrumbLog
    {
        private const int MaxMessageLength = 256;
        private const long FlushIntervalMs = 250;

        private readonly object gate = new();
        private readonly Entry[] entries;
        private readonly long windowMs;
        private readonly string snapshotPath;
        private readonly StringBuilder formatBuilder = new();
        private Func<long> clock;
        private int head;
        private int count;
        private long lastFlushMs = long.MinValue;
        private volatile string published = string.Empty;

        public string SnapshotPath => snapshotPath;

        public BreadcrumbLog(int capacity, TimeSpan window, string snapshotPath)
            : this(capacity, window, snapshotPath, null)
        {
        }

        public BreadcrumbLog(int capacity, TimeSpan window, string snapshotPath, Func<long> clock)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (window <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(window));
            }

            entries = new Entry[capacity];
            windowMs = (long)window.TotalMilliseconds;
            this.snapshotPath = snapshotPath ?? string.Empty;
            this.clock = clock ?? DefaultClock;
        }

        public void Add(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            message = message.Replace('\r', ' ').Replace('\n', ' ');
            if (message.Length > MaxMessageLength)
            {
                message = message.Substring(0, MaxMessageLength);
            }

            lock (gate)
            {
                var now = clock();
                while (count > 0 && now - entries[OldestIndex()].UnixMs > windowMs)
                {
                    count--;
                }

                entries[head] = new Entry
                {
                    UnixMs = now,
                    Message = message,
                };
                head = (head + 1) % entries.Length;
                if (count < entries.Length)
                {
                    count++;
                }

                published = FormatLocked(now);
                if (lastFlushMs == long.MinValue || now - lastFlushMs >= FlushIntervalMs)
                {
                    lastFlushMs = now;
                    AtomicFile.TryWrite(snapshotPath, published);
                }
            }
        }

        public string CaptureText(long nowUnixMs)
        {
            if (!Monitor.TryEnter(gate, 0))
            {
                return published;
            }

            try
            {
                return FormatLocked(nowUnixMs);
            }
            finally
            {
                Monitor.Exit(gate);
            }
        }

        public void DeleteSnapshot()
        {
            AtomicFile.TryDelete(snapshotPath);
            AtomicFile.TryDelete(snapshotPath + ".tmp");
        }

        private string FormatLocked(long nowUnixMs)
        {
            formatBuilder.Clear();
            var index = count == 0 ? 0 : OldestIndex();
            for (var entryIndex = 0; entryIndex < count; entryIndex++)
            {
                var entry = entries[index];
                index = (index + 1) % entries.Length;
                if (nowUnixMs - entry.UnixMs > windowMs)
                {
                    continue;
                }

                if (formatBuilder.Length > 0)
                {
                    formatBuilder.Append('\n');
                }

                formatBuilder.Append(entry.UnixMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
                formatBuilder.Append('\t');
                formatBuilder.Append(entry.Message);
            }

            if (formatBuilder.Length <= CrashArtifact.MaxBreadcrumbLength)
            {
                return formatBuilder.ToString();
            }

            var overflow = formatBuilder.Length - CrashArtifact.MaxBreadcrumbLength;
            var start = overflow;
            for (var scan = overflow; scan < formatBuilder.Length; scan++)
            {
                if (formatBuilder[scan] == '\n')
                {
                    start = scan + 1;
                    break;
                }
            }

            return formatBuilder.ToString(start, formatBuilder.Length - start);
        }

        private int OldestIndex()
        {
            return (head - count + entries.Length) % entries.Length;
        }

        private static long DefaultClock()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private struct Entry
        {
            public long UnixMs;
            public string Message;
        }
    }
}
