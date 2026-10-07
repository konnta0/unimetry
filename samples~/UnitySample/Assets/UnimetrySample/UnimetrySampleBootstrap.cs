using System;
using UnityEngine;

namespace Unimetry.Sample
{
    /// <summary>
    /// Sample scene entry point that initializes Unimetry at runtime.
    /// </summary>
    public sealed class UnimetrySampleBootstrap : MonoBehaviour
    {
        [SerializeField]
        private string endpoint = "http://localhost:4318";

        [SerializeField]
        private string serviceName = "unimetry-unity-sample";

        [SerializeField]
        private string deploymentEnvironment = "development";

        [SerializeField]
        private bool allowInsecureTls = true;

        [SerializeField]
        private bool exportErrorSpans = true;

        [SerializeField]
        private bool consoleLog = true;

        private void Awake()
        {
            if (UnimetryClient.IsInitialized)
            {
                return;
            }

            var options = new UnimetryOptions
            {
                Endpoint = endpoint,
                ServiceName = serviceName,
                ServiceVersion = Application.version,
                DeploymentEnvironment = deploymentEnvironment,
                AllowInsecureTls = allowInsecureTls,
                ExportErrorSpans = exportErrorSpans,
            };
            if (consoleLog)
            {
                options.WithConsoleLog();
            }

            UnimetryClient.Initialize(options);

            Debug.Log("Unimetry sample initialized.");
        }

        /// <summary>
        /// UI button hook that reports a sample exception through Unimetry.
        /// </summary>
        public void ReportSampleException()
        {
            try
            {
                throw new InvalidOperationException("Unimetry sample exception");
            }
            catch (Exception exception)
            {
                UnimetryClient.Report(exception);
            }
        }

        /// <summary>
        /// UI button hook that emits a Unity error log captured by Unimetry.
        /// </summary>
        public void EmitSampleErrorLog()
        {
            Debug.LogError("Unimetry sample error log");
        }

        /// <summary>
        /// Records a sample event, both around the method and at an explicit point inside it.
        /// </summary>
        [Event("sample.ping")]
        public void EmitSampleEvent()
        {
            UnimetryEvent.Write("sample.ping.direct");
        }

        /// <summary>
        /// Forces a flush for manual verification against a local collector.
        /// </summary>
        public async void FlushNow()
        {
            await UnimetryClient.FlushAsync();
            Debug.Log("Unimetry flush completed.");
        }
    }
}
