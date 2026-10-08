using System;
using UnityEditor;
using UnityEngine;

namespace Unimetry.Sample.Editor
{
    internal static class UnimetrySampleMenu
    {
        [MenuItem("Unimetry/Sample/Initialize For Play Mode")]
        private static void InitializeForPlayMode()
        {
            EnsureInitialized();
            Debug.Log("Unimetry sample ready for Play Mode.");
        }

        [MenuItem("Unimetry/Sample/Report Test Exception")]
        private static void ReportTestException()
        {
            EnsureInitialized();
            UnimetryClient.Report(new InvalidOperationException("Unimetry editor menu test exception"));
        }

        [MenuItem("Unimetry/Sample/Emit Test Error Log")]
        private static void EmitTestErrorLog()
        {
            EnsureInitialized();
            Debug.LogError("Unimetry editor menu test error log");
        }

        [MenuItem("Unimetry/Sample/Emit Test Event")]
        private static void EmitTestEvent()
        {
            EnsureInitialized();
            using (var scope = UnimetryEvent.Begin("sample.menu"))
            {
                scope.SetTag("source", "editor-menu");
            }
        }

        [MenuItem("Unimetry/Sample/Call Aspire Match Load")]
        private static void CallAspireMatchLoad()
        {
            var bootstrap = EnsureBootstrap(useAspireDashboard: true);
            bootstrap.CallAspireMatchLoad();
        }

        [MenuItem("Unimetry/Sample/Call Aspire Error")]
        private static void CallAspireError()
        {
            var bootstrap = EnsureBootstrap(useAspireDashboard: true);
            bootstrap.CallAspireError();
        }

        private static void EnsureInitialized(bool useAspireDashboard = false)
        {
            if (UnimetryClient.IsInitialized)
            {
                return;
            }

            UnimetryClient.Initialize(new UnimetryOptions
            {
                Endpoint = useAspireDashboard ? "http://localhost:18890" : "http://localhost:4318",
                ServiceName = "unimetry-unity-sample-editor",
                ServiceVersion = Application.version,
                DeploymentEnvironment = "development",
                AllowInsecureTls = true,
            });
        }

        private static UnimetrySampleBootstrap EnsureBootstrap(bool useAspireDashboard = false)
        {
            EnsureInitialized(useAspireDashboard);
            var existing = UnityEngine.Object.FindObjectOfType<UnimetrySampleBootstrap>();
            if (existing != null)
            {
                return existing;
            }

            var gameObject = new GameObject("UnimetrySampleBootstrap");
            return gameObject.AddComponent<UnimetrySampleBootstrap>();
        }
    }
}
