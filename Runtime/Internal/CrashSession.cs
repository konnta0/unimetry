using System;
using UnityEngine;

namespace Unimetry.Internal
{
    internal sealed class CrashSession
    {
        public string DeviceModel { get; private set; } = string.Empty;

        public string OsType { get; private set; } = string.Empty;

        public string BuildId { get; private set; } = string.Empty;

        public static CrashSession FromUnity()
        {
            var session = new CrashSession();
            try
            {
                session.DeviceModel = SystemInfo.deviceModel ?? string.Empty;
            }
            catch (Exception)
            {
                session.DeviceModel = string.Empty;
            }

            try
            {
                session.OsType = MapOsType(SystemInfo.operatingSystemFamily);
            }
            catch (Exception)
            {
                session.OsType = string.Empty;
            }

            try
            {
                var build = Application.buildGUID;
                if (string.IsNullOrEmpty(build))
                {
                    build = Application.version;
                }

                session.BuildId = build ?? string.Empty;
            }
            catch (Exception)
            {
                session.BuildId = string.Empty;
            }

            return session;
        }

        private static string MapOsType(OperatingSystemFamily family)
        {
            switch (family)
            {
                case OperatingSystemFamily.Windows:
                    return "windows";
                case OperatingSystemFamily.MacOSX:
                    return "darwin";
                case OperatingSystemFamily.Linux:
                    return "linux";
                default:
                    return string.Empty;
            }
        }
    }
}
