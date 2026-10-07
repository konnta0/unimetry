using System;

namespace Unimetry.Internal
{
    /// <summary>
    /// On-disk native crash record. Public fields are required by <c>JsonUtility</c>.
    /// <c>capturedAtUnixMs</c> stays in millisecond precision so the value survives Unity's JSON number parser.
    /// </summary>
    [Serializable]
    internal sealed class CrashArtifact
    {
        public const int MaxMessageLength = 1024;
        public const int MaxStackLength = 16 * 1024;
        public const int MaxBreadcrumbLength = 8 * 1024;
        public const int MaxRegisterLength = 2048;
        public const int MaxTypeLength = 256;
        public const int MaxThreadLength = 128;
        public const int MaxSignalLength = 32;
        public const int MaxDeviceLength = 128;
        public const int MaxOsLength = 32;
        public const int MaxBuildLength = 128;

        public int schema;
        public string id;
        public long capturedAtUnixMs;
        public string signal;
        public string exceptionType;
        public string message;
        public string threadName;
        public string registers;
        public string managedStack;
        public string breadcrumbs;
        public string deviceModel;
        public string osType;
        public string buildId;
        public string minidumpFile;
        public int minidumpBytes;

        public void Clamp()
        {
            schema = CrashArtifactStore.SchemaVersion;
            id = id ?? string.Empty;
            signal = Limit(signal, MaxSignalLength);
            exceptionType = Limit(exceptionType, MaxTypeLength);
            message = Limit(message, MaxMessageLength);
            threadName = Limit(threadName, MaxThreadLength);
            registers = Limit(registers, MaxRegisterLength);
            managedStack = Limit(managedStack, MaxStackLength);
            breadcrumbs = Limit(breadcrumbs, MaxBreadcrumbLength);
            deviceModel = Limit(deviceModel, MaxDeviceLength);
            osType = Limit(osType, MaxOsLength);
            buildId = Limit(buildId, MaxBuildLength);
            if (!CrashArtifactStore.IsSafeDumpName(id, minidumpFile) || minidumpBytes <= 0)
            {
                minidumpFile = string.Empty;
                minidumpBytes = 0;
            }
        }

        private static string Limit(string value, int max)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.Length <= max)
            {
                return value;
            }

            return value.Substring(0, max);
        }
    }
}
