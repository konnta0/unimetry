using System;
using System.Collections.Generic;

namespace Unimetry.Internal
{
    internal enum MetricKind
    {
        Counter,
        Histogram,
    }

    internal sealed class MetricSnapshot
    {
        public MetricKind Kind;
        public string Name;
        public string Unit;
        public double Sum;
        public long Count;
        public double Min;
        public double Max;
        public long[] BucketCounts;
        public double[] ExplicitBounds;
    }

    internal sealed class MetricBatch
    {
        public long StartTimeUnixNano;
        public long TimeUnixNano;
        public List<MetricSnapshot> Metrics;

        public bool IsEmpty => Metrics == null || Metrics.Count == 0;
    }

    internal sealed class MetricBuffer
    {
        internal const int MaxInstruments = 128;
        private const int MaxNameLength = 255;
        private const int MaxUnitLength = 63;

        // OpenTelemetry SDK default explicit histogram boundaries.
        internal static readonly double[] DefaultHistogramBounds =
        {
            0d, 5d, 10d, 25d, 50d, 75d, 100d, 250d, 500d, 750d, 1000d, 2500d, 5000d, 7500d, 10000d,
        };

        private readonly object gate = new object();
        private readonly Dictionary<string, Instrument> instruments =
            new Dictionary<string, Instrument>(StringComparer.Ordinal);
        private long pendingStartUnixNano;

        public MetricBuffer(long startTimeUnixNano)
        {
            pendingStartUnixNano = startTimeUnixNano;
        }

        public void AddCounter(string name, double delta, string unit)
        {
            if (!TryPrepareIdentity(ref name, ref unit) ||
                delta <= 0d ||
                double.IsNaN(delta) ||
                double.IsInfinity(delta))
            {
                return;
            }

            lock (gate)
            {
                var instrument = GetOrCreate(name, unit, MetricKind.Counter);
                if (instrument != null)
                {
                    var sum = instrument.Sum + delta;
                    if (!double.IsNaN(sum) && !double.IsInfinity(sum))
                    {
                        instrument.Sum = sum;
                    }
                }
            }
        }

        public void RecordHistogram(string name, double value, string unit)
        {
            if (!TryPrepareIdentity(ref name, ref unit) || double.IsNaN(value) || double.IsInfinity(value))
            {
                return;
            }

            lock (gate)
            {
                var instrument = GetOrCreate(name, unit, MetricKind.Histogram);
                if (instrument == null)
                {
                    return;
                }

                var sum = instrument.Sum + value;
                if (double.IsNaN(sum) || double.IsInfinity(sum))
                {
                    return;
                }

                instrument.Sum = sum;
                if (instrument.Count == 0)
                {
                    instrument.Min = value;
                    instrument.Max = value;
                }
                else
                {
                    if (value < instrument.Min)
                    {
                        instrument.Min = value;
                    }

                    if (value > instrument.Max)
                    {
                        instrument.Max = value;
                    }
                }

                instrument.Count++;
                var bucket = 0;
                while (bucket < DefaultHistogramBounds.Length && value > DefaultHistogramBounds[bucket])
                {
                    bucket++;
                }

                instrument.BucketCounts[bucket]++;
            }
        }

        public MetricBatch Collect(long timeUnixNano)
        {
            lock (gate)
            {
                var batch = new MetricBatch
                {
                    StartTimeUnixNano = pendingStartUnixNano,
                    TimeUnixNano = timeUnixNano,
                    Metrics = new List<MetricSnapshot>(),
                };

                foreach (var pair in instruments)
                {
                    var instrument = pair.Value;
                    if (instrument.Kind == MetricKind.Counter)
                    {
                        if (instrument.Sum <= 0d)
                        {
                            continue;
                        }

                        batch.Metrics.Add(new MetricSnapshot
                        {
                            Kind = MetricKind.Counter,
                            Name = instrument.Name,
                            Unit = instrument.Unit,
                            Sum = instrument.Sum,
                        });
                        instrument.Sum = 0d;
                        continue;
                    }

                    if (instrument.Count <= 0)
                    {
                        continue;
                    }

                    var bucketCounts = new long[instrument.BucketCounts.Length];
                    Array.Copy(instrument.BucketCounts, bucketCounts, bucketCounts.Length);
                    batch.Metrics.Add(new MetricSnapshot
                    {
                        Kind = MetricKind.Histogram,
                        Name = instrument.Name,
                        Unit = instrument.Unit,
                        Sum = instrument.Sum,
                        Count = instrument.Count,
                        Min = instrument.Min,
                        Max = instrument.Max,
                        BucketCounts = bucketCounts,
                        ExplicitBounds = DefaultHistogramBounds,
                    });
                    instrument.Sum = 0d;
                    instrument.Count = 0;
                    instrument.Min = 0d;
                    instrument.Max = 0d;
                    Array.Clear(instrument.BucketCounts, 0, instrument.BucketCounts.Length);
                }

                pendingStartUnixNano = timeUnixNano;
                return batch;
            }
        }

        public void Requeue(MetricBatch batch)
        {
            if (batch == null || batch.IsEmpty)
            {
                return;
            }

            lock (gate)
            {
                if (batch.StartTimeUnixNano < pendingStartUnixNano)
                {
                    pendingStartUnixNano = batch.StartTimeUnixNano;
                }

                for (var index = 0; index < batch.Metrics.Count; index++)
                {
                    var snapshot = batch.Metrics[index];
                    var instrument = GetOrCreate(snapshot.Name, snapshot.Unit, snapshot.Kind);
                    if (instrument == null)
                    {
                        continue;
                    }

                    instrument.Sum += snapshot.Sum;
                    if (snapshot.Kind != MetricKind.Histogram)
                    {
                        continue;
                    }

                    if (instrument.Count == 0)
                    {
                        instrument.Min = snapshot.Min;
                        instrument.Max = snapshot.Max;
                    }
                    else
                    {
                        instrument.Min = Math.Min(instrument.Min, snapshot.Min);
                        instrument.Max = Math.Max(instrument.Max, snapshot.Max);
                    }

                    instrument.Count += snapshot.Count;
                    for (var bucket = 0; bucket < instrument.BucketCounts.Length; bucket++)
                    {
                        instrument.BucketCounts[bucket] += snapshot.BucketCounts[bucket];
                    }
                }
            }
        }

        private Instrument GetOrCreate(string name, string unit, MetricKind kind)
        {
            if (instruments.TryGetValue(name, out var existing))
            {
                return existing.Kind == kind ? existing : null;
            }

            if (instruments.Count >= MaxInstruments)
            {
                return null;
            }

            var created = new Instrument
            {
                Kind = kind,
                Name = name,
                Unit = unit ?? string.Empty,
                BucketCounts = kind == MetricKind.Histogram
                    ? new long[DefaultHistogramBounds.Length + 1]
                    : null,
            };
            instruments.Add(name, created);
            return created;
        }

        private static bool TryPrepareIdentity(ref string name, ref string unit)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            name = name.Trim();
            if (name.Length > MaxNameLength)
            {
                name = name.Substring(0, MaxNameLength);
            }

            if (!string.IsNullOrEmpty(unit))
            {
                unit = unit.Trim();
                if (unit.Length > MaxUnitLength)
                {
                    unit = unit.Substring(0, MaxUnitLength);
                }
            }

            return true;
        }

        private sealed class Instrument
        {
            public MetricKind Kind;
            public string Name;
            public string Unit;
            public double Sum;
            public long Count;
            public double Min;
            public double Max;
            public long[] BucketCounts;
        }
    }
}
