using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Unimetry.Sample
{
    /// <summary>
    /// Sample scene entry point that initializes Unimetry at runtime.
    /// </summary>
    public sealed class UnimetrySampleBootstrap : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("OTLP/HTTP JSON base URL. Collector: http://localhost:4318. Aspire dashboard: http://localhost:18890.")]
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

        [SerializeField]
        [Tooltip("Aspire sample API. AppHost binds this to http://localhost:5288.")]
        private string apiBaseUrl = "http://localhost:5288";

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

        /// <summary>
        /// Sends a gameplay request to the Aspire sample API with a W3C <c>traceparent</c>.
        /// </summary>
        public async void CallAspireMatchLoad()
        {
            await SendAspireRequest("GET", "/match/load", "aspire.match.load");
        }

        /// <summary>
        /// Sends a request that the Aspire sample API fails on purpose.
        /// </summary>
        public async void CallAspireError()
        {
            await SendAspireRequest("POST", "/error", "aspire.sample.error");
        }

        private async Task SendAspireRequest(string method, string path, string spanName)
        {
            var url = apiBaseUrl.TrimEnd('/') + path;
            using var span = UnimetryTrace.Start(spanName);
            using var scope = UnimetryEvent.Start(spanName);
            scope?.SetTag("http.route", path);
            scope?.SetTag("server.address", apiBaseUrl);

            using var request = new UnityWebRequest(url, method);
            request.downloadHandler = new DownloadHandlerBuffer();
            var traceParent = UnimetryTrace.FormatTraceParent(span);
            if (!string.IsNullOrEmpty(traceParent))
            {
                request.SetRequestHeader("traceparent", traceParent);
            }

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

#if UNITY_2020_2_OR_NEWER
            var succeeded = request.result == UnityWebRequest.Result.Success;
#else
            var succeeded = !request.isNetworkError && !request.isHttpError;
#endif
            scope?.SetTag("http.status_code", (int)request.responseCode);
            if (succeeded)
            {
                Debug.Log("Aspire request succeeded: " + path + " " + request.downloadHandler.text);
                return;
            }

            var error = "Aspire request failed: " + path + " " + request.responseCode + " " + request.error;
            Debug.LogError(error);
            UnimetryClient.Report(new InvalidOperationException(error));
        }
    }
}
