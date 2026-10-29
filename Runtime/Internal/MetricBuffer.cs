using System;
using System.Collections.Generic;
using System.Text;

namespace Unimetry.Internal
{
    internal enum MetricKind
    {
        Counter,
        UpDownCounter,
        Gauge,
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
        public EventTag[] Attributes;
        public int AttributeCount;
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
        internal const int MaxAttributes = 4;
        private const int MaxNameLength = 255;
        private const int MaxUnitLength = 63;
        private const int MaxAttributeKeyLength = 256;
        private const int MaxAttributeValueLength = 4096;

        // OpenTelemetry SDK default explicit histogram boundaries.
        internal static readonly double[] DefaultHistogramBounds =
        {
            0d, 5d, 10d, 25d, 50d, 75d, 100d, 250d, 500d, 750d, 1000d, 2500d, 5000d, 7500d, 10000d,
        };

        private readonly object gate = new object();
        private readonly Dictionary<string, InstrumentDefinition> definitions =
            new Dictionary<string, InstrumentDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, Instrument> instruments =
            new Dictionary<string, Instrument>(StringComparer.Ordinal);
        private long pendingStartUnixNano;

        public MetricBuffer(long startTimeUnixNano)
        {
            pendingStartUnixNano = startTimeUnixNano;
        }

        public void AddCounter(string name, double delta, string unit, MetricAttribute[] attributes = null)
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
                AddSum(GetOrCreate(name, unit, MetricKind.Counter, attributes), delta);
            }
        }

        public void AddUpDownCounter(string name, double delta, string unit, MetricAttribute[] attributes = null)
        {
            if (!TryPrepareIdentity(ref name, ref unit) || double.IsNaN(delta) || double.IsInfinity(delta))
            {
                return;
            }

            lock (gate)
            {
                AddSum(GetOrCreate(name, unit, MetricKind.UpDownCounter, attributes), delta);
            }
        }

        public void RecordGauge(string name, double value, string unit, MetricAttribute[] attributes = null)
        {
            if (!TryPrepareIdentity(ref name, ref unit) || double.IsNaN(value) || double.IsInfinity(value))
            {
                return;
            }

            lock (gate)
            {
                var instrument = GetOrCreate(name, unit, MetricKind.Gauge, attributes);
                if (instrument == null)
                {
                    return;
                }

                instrument.Sum = value;
                instrument.HasValue = true;
            }
        }

        public void RecordHistogram(string name, double value, string unit, MetricAttribute[] attributes = null)
        {
            if (!TryPrepareIdentity(ref name, ref unit) || double.IsNaN(value) || double.IsInfinity(value))
            {
                return;
            }

            lock (gate)
            {
                var instrument = GetOrCreate(name, unit, MetricKind.Histogram, attributes);
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
                    instrument.Min = Math.Min(instrument.Min, value);
                    instrument.Max = Math.Max(instrument.Max, value);
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
                    if (instrument.Kind == MetricKind.Histogram)
                    {
                        CollectHistogram(instrument, batch.Metrics);
                    }
                    else if (instrument.HasValue)
                    {
                        batch.Metrics.Add(CreateSnapshot(instrument));
                        instrument.Sum = 0d;
                        instrument.HasValue = false;
                    }
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
                    var instrument = GetOrCreatePrepared(
                        snapshot.Name,
                        snapshot.Unit,
                        snapshot.Kind,
                        snapshot.Attributes,
                        snapshot.AttributeCount);
                    if (instrument == null)
                    {
                        continue;
                    }

                    if (snapshot.Kind == MetricKind.Gauge)
                    {
                        // A value recorded while the failed request was in flight is newer.
                        if (!instrument.HasValue)
                        {
                            instrument.Sum = snapshot.Sum;
                            instrument.HasValue = true;
                        }

                        continue;
                    }

                    if (snapshot.Kind == MetricKind.Histogram)
                    {
                        RequeueHistogram(instrument, snapshot);
                    }
                    else
                    {
                        AddSum(instrument, snapshot.Sum);
                    }
                }
            }
        }

        private static void AddSum(Instrument instrument, double delta)
        {
            if (instrument == null)
            {
                return;
            }

            var sum = instrument.Sum + delta;
            if (double.IsNaN(sum) || double.IsInfinity(sum))
            {
                return;
            }

            instrument.Sum = sum;
            instrument.HasValue = true;
        }

        private static MetricSnapshot CreateSnapshot(Instrument instrument)
        {
            return new MetricSnapshot
            {
                Kind = instrument.Kind,
                Name = instrument.Name,
                Unit = instrument.Unit,
                Sum = instrument.Sum,
                Attributes = instrument.Attributes,
                AttributeCount = instrument.AttributeCount,
            };
        }

        private static void CollectHistogram(Instrument instrument, List<MetricSnapshot> destination)
        {
            if (instrument.Count <= 0)
            {
                return;
            }

            var bucketCounts = new long[instrument.BucketCounts.Length];
            Array.Copy(instrument.BucketCounts, bucketCounts, bucketCounts.Length);
            destination.Add(new MetricSnapshot
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
                Attributes = instrument.Attributes,
                AttributeCount = instrument.AttributeCount,
            });
            instrument.Sum = 0d;
            instrument.Count = 0;
            instrument.Min = 0d;
            instrument.Max = 0d;
            Array.Clear(instrument.BucketCounts, 0, instrument.BucketCounts.Length);
        }

        private static void RequeueHistogram(Instrument instrument, MetricSnapshot snapshot)
        {
            var sum = instrument.Sum + snapshot.Sum;
            if (!double.IsNaN(sum) && !double.IsInfinity(sum))
            {
                instrument.Sum = sum;
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

        private Instrument GetOrCreate(string name, string unit, MetricKind kind, MetricAttribute[] sourceAttributes)
        {
            PrepareAttributes(sourceAttributes, out var attributes, out var attributeCount);
            return GetOrCreatePrepared(name, unit, kind, attributes, attributeCount);
        }

        private Instrument GetOrCreatePrepared(
            string name,
            string unit,
            MetricKind kind,
            EventTag[] attributes,
            int attributeCount)
        {
            var hasDefinition = definitions.TryGetValue(name, out var definition);
            if (hasDefinition)
            {
                if (definition.Kind != kind || !string.Equals(definition.Unit, unit, StringComparison.Ordinal))
                {
                    return null;
                }
            }

            var seriesKey = BuildSeriesKey(name, attributes, attributeCount);
            if (instruments.TryGetValue(seriesKey, out var existing))
            {
                return existing;
            }

            if (instruments.Count >= MaxInstruments)
            {
                return null;
            }

            if (!hasDefinition)
            {
                definitions.Add(name, new InstrumentDefinition { Kind = kind, Unit = unit });
            }

            var created = new Instrument
            {
                Kind = kind,
                Name = name,
                Unit = unit,
                Attributes = attributes,
                AttributeCount = attributeCount,
                BucketCounts = kind == MetricKind.Histogram
                    ? new long[DefaultHistogramBounds.Length + 1]
                    : null,
            };
            instruments.Add(seriesKey, created);
            return created;
        }

        private static void PrepareAttributes(MetricAttribute[] source, out EventTag[] attributes, out int count)
        {
            attributes = null;
            count = 0;
            if (source == null || source.Length == 0)
            {
                return;
            }

            var prepared = new EventTag[MaxAttributes];
            for (var sourceIndex = 0; sourceIndex < source.Length; sourceIndex++)
            {
                var tag = source[sourceIndex].Tag;
                if (!TryPrepareAttribute(ref tag))
                {
                    continue;
                }

                var existing = -1;
                for (var index = 0; index < count; index++)
                {
                    if (string.Equals(prepared[index].Key, tag.Key, StringComparison.Ordinal))
                    {
                        existing = index;
                        break;
                    }
                }

                if (existing >= 0)
                {
                    prepared[existing] = tag;
                }
                else if (count < prepared.Length)
                {
                    prepared[count++] = tag;
                }
            }

            if (count == 0)
            {
                return;
            }

            Array.Sort(prepared, 0, count, EventTagComparer.Instance);
            attributes = prepared;
        }

        private static bool TryPrepareAttribute(ref EventTag tag)
        {
            if (string.IsNullOrWhiteSpace(tag.Key))
            {
                return false;
            }

            tag.Key = tag.Key.Trim();
            if (tag.Key.Length > MaxAttributeKeyLength)
            {
                tag.Key = tag.Key.Substring(0, MaxAttributeKeyLength);
            }

            if (tag.Kind == EventTagKind.String)
            {
                if (tag.Text == null)
                {
                    return false;
                }

                if (tag.Text.Length > MaxAttributeValueLength)
                {
                    tag.Text = tag.Text.Substring(0, MaxAttributeValueLength);
                }
            }
            else if (tag.Kind == EventTagKind.Double)
            {
                var value = tag.ReadDouble();
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    return false;
                }
            }

            return true;
        }

        private static string BuildSeriesKey(string name, EventTag[] attributes, int count)
        {
            var builder = new StringBuilder(name.Length + count * 24);
            builder.Append(count).Append('|').Append(name.Length).Append(':').Append(name);
            for (var index = 0; index < count; index++)
            {
                var tag = attributes[index];
                builder.Append('|').Append(tag.Key.Length).Append(':').Append(tag.Key);
                builder.Append(':').Append((int)tag.Kind).Append(':');
                if (tag.Kind == EventTagKind.String)
                {
                    builder.Append(tag.Text.Length).Append(':').Append(tag.Text);
                }
                else
                {
                    builder.Append(tag.Bits);
                }
            }

            return builder.ToString();
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

            unit = string.IsNullOrEmpty(unit) ? string.Empty : unit.Trim();
            if (unit.Length > MaxUnitLength)
            {
                unit = unit.Substring(0, MaxUnitLength);
            }

            return true;
        }

        private sealed class InstrumentDefinition
        {
            public MetricKind Kind;
            public string Unit;
        }

        private sealed class Instrument
        {
            public MetricKind Kind;
            public string Name;
            public string Unit;
            public double Sum;
            public bool HasValue;
            public long Count;
            public double Min;
            public double Max;
            public long[] BucketCounts;
            public EventTag[] Attributes;
            public int AttributeCount;
        }

        private sealed class EventTagComparer : IComparer<EventTag>
        {
            public static readonly EventTagComparer Instance = new EventTagComparer();

            public int Compare(EventTag left, EventTag right)
            {
                return string.CompareOrdinal(left.Key, right.Key);
            }
        }
    }
}
