using System;
using System.Runtime.InteropServices;

namespace Unimetry.Internal
{
    /// <summary>
    /// Installs the POSIX signal handler for macOS and iOS players.
    /// The handler writes the same crash artifact JSON the managed ingest path already reads.
    /// </summary>
    internal static class PosixCrashHandler
    {
        public static void Install(CrashArtifactStore store, BreadcrumbLog breadcrumbs, CrashSession session)
        {
#if (UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR
            if (store == null || session == null)
            {
                return;
            }

            var breadcrumbPath = breadcrumbs == null ? string.Empty : breadcrumbs.SnapshotPath;
            unimetry_install(
                store.DirectoryPath,
                breadcrumbPath,
                session.DeviceModel ?? string.Empty,
                session.OsType ?? string.Empty,
                session.BuildId ?? string.Empty);
#else
            _ = store;
            _ = breadcrumbs;
            _ = session;
#endif
        }

        public static void Uninstall()
        {
#if (UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR
            unimetry_uninstall();
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
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
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
