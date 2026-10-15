using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Unimetry.Internal
{
    /// <summary>
    /// Installs the POSIX signal handler for macOS, iOS, and Android players.
    /// The handler writes the same crash artifact JSON the managed ingest path already reads.
    /// </summary>
    internal static class PosixCrashHandler
    {
        /// <summary>
        /// Installs the native signal handler. Missing plugin binaries log a warning and leave capture disabled.
        /// </summary>
        public static void Install(CrashArtifactStore store, BreadcrumbLog breadcrumbs, CrashSession session)
        {
#if (UNITY_STANDALONE_OSX || UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
            if (store == null || session == null)
            {
                return;
            }

            try
            {
                var breadcrumbPath = breadcrumbs == null ? string.Empty : breadcrumbs.SnapshotPath;
                unimetry_install(
                    store.DirectoryPath,
                    breadcrumbPath,
                    session.DeviceModel ?? string.Empty,
                    session.OsType ?? string.Empty,
                    session.BuildId ?? string.Empty);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Unimetry failed to install the POSIX crash handler: " + exception.Message);
            }
#else
            _ = store;
            _ = breadcrumbs;
            _ = session;
#endif
        }

        /// <summary>Restores the previous signal handlers when a native plugin is present.</summary>
        public static void Uninstall()
        {
#if (UNITY_STANDALONE_OSX || UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
            try
            {
                unimetry_uninstall();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Unimetry failed to uninstall the POSIX crash handler: " + exception.Message);
            }
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void unimetry_install(
            string crashDirectory,
            string breadcrumbPath,
            string deviceModel,
            string osType,
            string buildId);

        [DllImport("__Internal")]
        private static extern void unimetry_uninstall();
#elif (UNITY_STANDALONE_OSX || UNITY_ANDROID) && !UNITY_EDITOR
        [DllImport("unimetry_crash")]
        private static extern void unimetry_install(
            string crashDirectory,
            string breadcrumbPath,
            string deviceModel,
            string osType,
            string buildId);

        [DllImport("unimetry_crash")]
        private static extern void unimetry_uninstall();
#endif
    }
}
