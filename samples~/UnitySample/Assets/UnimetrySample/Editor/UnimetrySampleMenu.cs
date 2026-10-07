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

        private static void EnsureInitialized()
        {
            if (UnimetryClient.IsInitialized)
            {
                return;
            }

            UnimetryClient.Initialize(new UnimetryOptions
            {
                Endpoint = "http://localhost:4318",
                ServiceName = "unimetry-unity-sample-editor",
                ServiceVersion = Application.version,
                DeploymentEnvironment = "development",
                AllowInsecureTls = true,
            });
        }
    }
}
