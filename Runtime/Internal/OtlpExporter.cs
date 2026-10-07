using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Unimetry.Internal
{
    internal sealed class OtlpExporter
    {
        private readonly UnimetryOptions options;
        private readonly string logsEndpoint;
        private readonly string tracesEndpoint;

        public OtlpExporter(UnimetryOptions options)
        {
            this.options = options;
            var endpoint = options.Endpoint.TrimEnd('/');
            logsEndpoint = endpoint + "/v1/logs";
            tracesEndpoint = endpoint + "/v1/traces";
        }

        public Task<bool> ExportAsync(IReadOnlyList<PendingExport> items, CancellationToken cancellationToken)
        {
            return ExportAsync(items, null, 0, cancellationToken);
        }

        public async Task<bool> ExportAsync(
            IReadOnlyList<PendingExport> items,
            EventRecord[] events,
            int eventCount,
            CancellationToken cancellationToken)
        {
            var errorCount = items == null ? 0 : items.Count;
            if (errorCount == 0 && eventCount <= 0)
            {
                return true;
            }

            var logsPayload = OtlpJsonWriter.BuildLogsPayload(items, events, eventCount, options);
            var logsSent = await SendAsync(logsEndpoint, logsPayload, cancellationToken).ConfigureAwait(false);
            if (!logsSent)
            {
                return false;
            }

            var hasSpans = false;
            if (items != null)
            {
                for (var index = 0; index < items.Count; index++)
                {
                    if (items[index].IncludeSpan)
                    {
                        hasSpans = true;
                        break;
                    }
                }
            }

            if (!hasSpans || !options.ExportErrorSpans)
            {
                return true;
            }

            var tracesPayload = OtlpJsonWriter.BuildTracesPayload(items, options);
            return await SendAsync(tracesEndpoint, tracesPayload, cancellationToken).ConfigureAwait(false);
        }

        private async Task<bool> SendAsync(string url, string payload, CancellationToken cancellationToken)
        {
            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            var body = Encoding.UTF8.GetBytes(payload);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            if (options.Headers != null)
            {
                foreach (var header in options.Headers)
                {
                    if (!string.IsNullOrWhiteSpace(header.Key))
                    {
                        request.SetRequestHeader(header.Key, header.Value ?? string.Empty);
                    }
                }
            }

            if (options.AllowInsecureTls)
            {
                request.certificateHandler = new AcceptAllCertificatesHandler();
            }

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    request.Abort();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                await Task.Yield();
            }

#if UNITY_2020_2_OR_NEWER
            var succeeded = request.result == UnityWebRequest.Result.Success;
#else
            var succeeded = !request.isNetworkError && !request.isHttpError;
#endif
            if (!succeeded)
            {
                Debug.LogWarning($"Unimetry OTLP export failed: {request.responseCode} {request.error}");
            }

            return succeeded;
        }

        private sealed class AcceptAllCertificatesHandler : CertificateHandler
        {
            protected override bool ValidateCertificate(byte[] certificateData)
            {
                return true;
            }
        }
    }
}
