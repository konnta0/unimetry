using System.Globalization;
using UnityEngine;

namespace Unimetry.Internal
{
    internal static class UnimetryConsoleLog
    {
        public static void Write(in UnimetryLogEntry entry)
        {
            Debug.unityLogger.Log(ToLogType(entry.Severity), Format(in entry));
        }

        private static string Format(in UnimetryLogEntry entry)
        {
            if (entry.Kind == UnimetryLogKind.Event)
            {
                var durationNanos = entry.EndUnixNano - entry.StartUnixNano;
                if (durationNanos < 0)
                {
                    durationNanos = 0;
                }

                var durationMicros = durationNanos / 1000L;
                return string.Concat(
                    "[Unimetry] ",
                    entry.Name,
                    " ",
                    durationMicros.ToString(CultureInfo.InvariantCulture),
                    "us");
            }

            return string.Concat("[Unimetry] ", entry.Severity.ToString(), " ", entry.Message);
        }

        private static LogType ToLogType(UnimetrySeverity severity)
        {
            if (severity == UnimetrySeverity.Warn)
            {
                return LogType.Warning;
            }

            if (severity == UnimetrySeverity.Error || severity == UnimetrySeverity.Fatal)
            {
                return LogType.Error;
            }

            return LogType.Log;
        }
    }
}
