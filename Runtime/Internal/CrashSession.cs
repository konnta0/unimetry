using System;
using UnityEngine;

namespace Unimetry.Internal
{
    /// <summary>
    /// Device and build fields copied onto a native crash artifact at handler install time.
    /// </summary>
    internal sealed class CrashSession
    {
        /// <summary>Unity <c>SystemInfo.deviceModel</c> at install time.</summary>
        public string DeviceModel { get; private set; } = string.Empty;

        /// <summary>Platform key written as <c>os.type</c> (<c>android</c>, <c>ios</c>, <c>darwin</c>, <c>windows</c>, <c>linux</c>).</summary>
        public string OsType { get; private set; } = string.Empty;

        /// <summary>Unity <c>Application.buildGUID</c>, or <c>Application.version</c> when the GUID is empty.</summary>
        public string BuildId { get; private set; } = string.Empty;

        /// <summary>
        /// Reads the current Unity player identity. Android maps to <c>android</c>, not <c>linux</c>.
        /// </summary>
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
                session.OsType = MapOsType();
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

        private static string MapOsType()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.Android:
                    return "android";
                case RuntimePlatform.IPhonePlayer:
                    return "ios";
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.OSXEditor:
                    return "darwin";
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor:
                    return "windows";
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxEditor:
                    return "linux";
                default:
                    return string.Empty;
            }
        }
    }
}
