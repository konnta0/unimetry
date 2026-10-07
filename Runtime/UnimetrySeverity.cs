namespace Unimetry
{
    /// <summary>
    /// OpenTelemetry log severity numbers used by Unimetry logs and events.
    /// </summary>
    public enum UnimetrySeverity
    {
        /// <summary>Trace severity (1).</summary>
        Trace = 1,

        /// <summary>Debug severity (5).</summary>
        Debug = 5,

        /// <summary>Informational severity (9).</summary>
        Info = 9,

        /// <summary>Warning severity (13).</summary>
        Warn = 13,

        /// <summary>Error severity (17).</summary>
        Error = 17,

        /// <summary>Fatal severity (21).</summary>
        Fatal = 21,
    }
}
