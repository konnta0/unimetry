namespace Unimetry
{
    /// <summary>
    /// A typed OpenTelemetry attribute that identifies one metric time series.
    /// Keep values low-cardinality because each distinct attribute set creates a separate series.
    /// </summary>
    public readonly struct MetricAttribute
    {
        internal MetricAttribute(Internal.EventTag tag)
        {
            Tag = tag;
        }

        /// <summary>Creates a string metric attribute.</summary>
        public MetricAttribute(string key, string value)
            : this(Internal.EventTag.FromString(key, value))
        {
        }

        /// <summary>Creates a boolean metric attribute.</summary>
        public MetricAttribute(string key, bool value)
            : this(Internal.EventTag.FromBool(key, value))
        {
        }

        /// <summary>Creates an integer metric attribute.</summary>
        public MetricAttribute(string key, int value)
            : this(Internal.EventTag.FromInt(key, value))
        {
        }

        /// <summary>Creates a 64-bit integer metric attribute.</summary>
        public MetricAttribute(string key, long value)
            : this(Internal.EventTag.FromLong(key, value))
        {
        }

        /// <summary>Creates a floating-point metric attribute.</summary>
        public MetricAttribute(string key, double value)
            : this(Internal.EventTag.FromDouble(key, value))
        {
        }

        internal Internal.EventTag Tag { get; }
    }
}
