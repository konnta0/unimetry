namespace Unimetry
{
    /// <summary>
    /// Kind of record delivered to a <see cref="UnimetryLogWriter"/>.
    /// </summary>
    public enum UnimetryLogKind
    {
        /// <summary>Captured error or exception log.</summary>
        Log = 0,

        /// <summary>Duration or point event.</summary>
        Event = 1,
    }

    /// <summary>
    /// Receives a log or event that Unimetry is also exporting.
    /// </summary>
    /// <param name="entry">Record describing the log or event. The value is not retained.</param>
    public delegate void UnimetryLogWriter(in UnimetryLogEntry entry);

    /// <summary>
    /// Value passed to a user log writer. Formatting is left to the writer so the hot path can skip it.
    /// </summary>
    public readonly struct UnimetryLogEntry
    {
        /// <summary>
        /// Initializes a log entry.
        /// </summary>
        /// <param name="kind">Whether this record is an error log or an event.</param>
        /// <param name="name">Event name, or <c>error</c> for captured errors.</param>
        /// <param name="message">Human-readable message.</param>
        /// <param name="severity">OpenTelemetry severity.</param>
        /// <param name="startUnixNano">Start time in Unix epoch nanoseconds.</param>
        /// <param name="endUnixNano">End time in Unix epoch nanoseconds. Equal to the start for point records.</param>
        public UnimetryLogEntry(
            UnimetryLogKind kind,
            string name,
            string message,
            UnimetrySeverity severity,
            long startUnixNano,
            long endUnixNano)
        {
            Kind = kind;
            Name = name ?? string.Empty;
            Message = message ?? string.Empty;
            Severity = severity;
            StartUnixNano = startUnixNano;
            EndUnixNano = endUnixNano;
        }

        /// <summary>Whether this record is an error log or an event.</summary>
        public UnimetryLogKind Kind { get; }

        /// <summary>Event name, or <c>error</c> for captured errors.</summary>
        public string Name { get; }

        /// <summary>Human-readable message.</summary>
        public string Message { get; }

        /// <summary>OpenTelemetry severity.</summary>
        public UnimetrySeverity Severity { get; }

        /// <summary>Start time in Unix epoch nanoseconds.</summary>
        public long StartUnixNano { get; }

        /// <summary>End time in Unix epoch nanoseconds.</summary>
        public long EndUnixNano { get; }
    }
}
