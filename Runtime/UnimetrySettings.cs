using System;
using UnityEngine;

namespace Unimetry
{
    /// <summary>
    /// Player settings loaded from a Resources asset named <c>UnimetrySettings</c>.
    /// The Editor project settings window creates <c>Assets/Resources/UnimetrySettings.asset</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "UnimetrySettings", menuName = "Unimetry/Settings")]
    public sealed class UnimetrySettings : ScriptableObject
    {
        /// <summary>OTLP/HTTP base endpoint.</summary>
        public string Endpoint = string.Empty;

        /// <summary>OpenTelemetry <c>service.name</c>.</summary>
        public string ServiceName = "unity-app";

        /// <summary>OpenTelemetry <c>service.version</c>.</summary>
        public string ServiceVersion = string.Empty;

        /// <summary>OpenTelemetry <c>deployment.environment</c>.</summary>
        public string DeploymentEnvironment = "production";

        /// <summary>Ignore TLS errors. Development only.</summary>
        public bool AllowInsecureTls;

        /// <summary>Export error and gameplay spans.</summary>
        public bool ExportErrorSpans = true;

        /// <summary>Record native crashes on supported players.</summary>
        public bool CaptureNativeCrashes;

        /// <summary>Write a size-capped minidump on Windows.</summary>
        public bool CaptureMinidumps;

        /// <summary>Export FPS, memory, and startup gauges.</summary>
        public bool CaptureMetrics = true;

        /// <summary>Builds runtime options from this asset.</summary>
        /// <returns>Options ready for <see cref="UnimetryClient.Initialize"/>.</returns>
        public UnimetryOptions ToOptions()
        {
            return new UnimetryOptions
            {
                Endpoint = Endpoint ?? string.Empty,
                ServiceName = string.IsNullOrWhiteSpace(ServiceName) ? "unity-app" : ServiceName,
                ServiceVersion = ServiceVersion ?? string.Empty,
                DeploymentEnvironment = string.IsNullOrWhiteSpace(DeploymentEnvironment) ? "production" : DeploymentEnvironment,
                AllowInsecureTls = AllowInsecureTls,
                ExportErrorSpans = ExportErrorSpans,
                CaptureNativeCrashes = CaptureNativeCrashes,
                CaptureMinidumps = CaptureMinidumps,
                CaptureMetrics = CaptureMetrics,
            };
        }
    }
}
