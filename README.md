# Unimetry

**English** | [日本語](README.ja.md)

A crash and error instrumentation library for Unity. It treats managed exceptions and Unity logs as OpenTelemetry **Logs** and **Traces**, and sends them to a Collector over **OTLP/HTTP (JSON)**.

It does not depend on the standard OpenTelemetry .NET SDK. A minimal OTLP exporter is implemented in this package so it can run under Unity's runtime limits (no `Meter`, limited `DiagnosticSource`, and similar constraints).

## Current scope (v0.8.0)

| Area | Status |
| --- | --- |
| Unity `LogType.Error` / `Exception` / `Assert` | Supported |
| `AppDomain.UnhandledException` | Supported |
| `TaskScheduler.UnobservedTaskException` | Supported |
| Manual `UnimetryClient.Report(...)` | Supported |
| `[Event]` / `UnimetryEvent.Begin` / `Start` / `Write` / common attributes | Supported |
| OTLP Logs (`/v1/logs`) | Supported |
| OTLP Traces (`/v1/traces`, error and gameplay spans) | Supported |
| OTLP Metrics (`/v1/metrics`) | Supported |
| W3C `traceparent` / `UnimetryTrace` | Supported |
| Offline persistent queue + retry | Supported |
| Native crashes on Windows, macOS, iOS, and Android | Supported (opt-in, uploaded on the next launch) |

## Architecture

Layering and data flow: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

```
Unity App
  ├─ ErrorCapture (log / unhandled / task hooks)
  ├─ PersistentQueue (persistentDataPath/unimetry/queue.json)
  ├─ BackgroundFlusher (batch + retry)
  └─ OtlpExporter (OTLP/HTTP JSON)
           │
           ▼
   OpenTelemetry Collector (:4318)
           │
           ▼
   Jaeger / Tempo / Loki / SigNoz / etc.
```

### Mapping to OTel

| Unity event | OTel Logs | OTel Traces |
| --- | --- | --- |
| Exception | `severityNumber=17/21`, `body=message` | `unity.error` span, `status=ERROR` |
| Attributes | `exception.type`, `exception.message`, `exception.stacktrace`, `exception.escaped` | span attributes + `exception` event |
| Resource | `service.name`, `service.version`, `deployment.environment` | same as Logs |

`unimetry.source` and `unimetry.fingerprint` are library-specific attributes.

## Install

### Local sample project

```bash
git clone https://github.com/konnta0/unimetry.git
```

Open `samples~/UnitySample` in Unity Hub. See [samples~/UnitySample/README.md](samples~/UnitySample/README.md). To send Unity requests into an Aspire dashboard, start [samples~/Aspire](samples~/Aspire/README.md) and point Unimetry at `http://localhost:18890`.

### Add to another project

1. Add this repository to the Unity project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.konnta0.unimetry": "https://github.com/konnta0/unimetry.git?path=/"
  }
}
```

2. Initialize at startup:

```csharp
using Unimetry;

UnimetryClient.Initialize(new UnimetryOptions
{
    Endpoint = "http://localhost:4318",
    ServiceName = "my-game",
    ServiceVersion = Application.version,
    DeploymentEnvironment = "development",
    AllowInsecureTls = true,
    CaptureBuiltInMetrics = false, // Optional: application metrics remain enabled.
}.WithConsoleLog());
```

`WithConsoleLog()` writes error logs and events through `Debug.unityLogger`, including a log handler the user has already installed. Use `WithLog` to forward them to your own logger.

```csharp
options.WithLog(static (in UnimetryLogEntry entry) =>
{
    MyLogger.Info(entry.Name);
});
```

### Optional UniTask integration

When UniTask 2.x is installed through UPM, Unimetry detects `com.cysharp.unitask` and automatically uses native `UniTask` throughout its periodic flush and OTLP request pipeline. Existing `Task` APIs remain available for compatibility. Call the UniTask-specific APIs to avoid the final `Task` compatibility allocation:

```csharp
using Cysharp.Threading.Tasks;

await UnimetryClient.FlushUniTaskAsync(cancellationToken);
await UnimetryClient.ShutdownUniTaskAsync(cancellationToken);
```

Projects that imported UniTask as an asset rather than a UPM package can enable the same path with the `UNIMETRY_UNITASK` scripting define. Without UniTask, Unimetry compiles and behaves as before using `Task`.

## Event

An event is an OTLP log record with `eventName`. `timeUnixNano` is the start, `observedTimeUnixNano` is the end, and `unimetry.event.duration_ns` is the elapsed time. When the client is not initialized, or when `CaptureEvents = false`, synchronous `Begin` does not allocate.

```csharp
[Event("match.load")]
public async Task LoadAsync([EventTag("match.region")] string region)
{
}

using (UnimetryEvent.Begin("player.jump"))
{
}

using (var handle = UnimetryEvent.Start("match.load"))
{
    await LoadAsync(region);
}

UnimetryEvent.Write("checkpoint.reached");
```

`SetAttribute` stores attributes that are copied onto every later event, including `Write` and `[Event]`. Set them once after login. An explicit tag with the same key replaces the common value for that event. At most eight common attributes are kept. A `null` string removes the key. `ClearAttributes` removes all of them, for example on logout.

```csharp
UnimetryEvent.SetAttribute("user.id", userId);

using (var click = UnimetryEvent.Begin("ui.button.click"))
{
    click.SetTag("ui.button", "play");
}
```

`[Event]` is woven by Unity's IL Post Processor. `Task`, `ValueTask`, and UniTask async state machines are supported. Woven synchronous methods keep a single body so breakpoints bind to the IL that actually runs; disabled capture still allocates nothing. Generic methods, iterators, `async void`, and local functions are rejected with the `UM004` compiler error so instrumentation cannot silently disappear. Define `UNIMETRY_DISABLE_EVENT_WEAVE` to disable weaving. Tag types are `string`, `bool`, `int`, `long`, and `double`.

Completed events are encrypted and persisted under `persistentDataPath/unimetry`, then sent to `/v1/logs`. Overflow is dropped and counted. `Shutdown` leaves unsent data on disk; prefer `await UnimetryClient.ShutdownAsync()` when a final network flush is possible.

Resource attributes are snapshotted when each error, span, or event is recorded. This prevents offline telemetry from one signed-in account being exported with a later account's identity. Update identity without reinitializing:

```csharp
UnimetryClient.SetResourceAttribute("app.user_id", userId); // null removes it
```

`Sanitizer` also applies to exceptions attached to events. `ErrorFilter` can suppress automatic capture routes before they enter the persistent queue:

```csharp
options.ErrorFilter = error => error.Source != CapturedErrorSource.UnityLog;
```

### Automatic initialization from environment variables

| Variable | Description |
| --- | --- |
| `UNIMETRY_OTLP_ENDPOINT` | Auto-initialize only when this is set |
| `UNIMETRY_SERVICE_NAME` | Defaults to `Application.productName` |
| `UNIMETRY_SERVICE_VERSION` | Defaults to `Application.version` |
| `UNIMETRY_DEPLOYMENT_ENVIRONMENT` | Defaults to `production` |
| `UNIMETRY_ALLOW_INSECURE_TLS` | Set to `1` or `true` to disable TLS verification |

## Example Collector config

```yaml
receivers:
  otlp:
    protocols:
      http:
        endpoint: 0.0.0.0:4318

exporters:
  debug:
    verbosity: detailed

service:
  pipelines:
    logs:
      receivers: [otlp]
      exporters: [debug]
    traces:
      receivers: [otlp]
      exporters: [debug]
```

## Native crashes

Windows, macOS, iOS, and Android players can record a native crash and upload it on the next launch. Capture stays off until the game sets `CaptureNativeCrashes` after its own consent UI. Editor play mode does not install a native handler.

```csharp
UnimetryClient.Initialize(new UnimetryOptions
{
    Endpoint = "http://localhost:4318",
    ServiceName = "my-game",
    CaptureNativeCrashes = true,
    CaptureMinidumps = true,
}.WithConsoleLog());

UnimetryClient.AddBreadcrumb("entered match");
```

`AddBreadcrumb` keeps a short trail (last 30 seconds, 64 entries by default). It does nothing unless native crash capture is enabled. The unhandled-exception filter is installed only in Windows standalone players, not in the Editor. An unhandled native exception writes `persistentDataPath/unimetry/crashes/*.crash.json`. On the next launch, if capture is still enabled, Unimetry enqueues an OTLP log with `unimetry.record_type=crash`, `crash.signal`, `device.model`, `os.type`, and `app.build_id`. One `unity.crash` span is included when `ExportErrorSpans` is true.

`CaptureMinidumps` writes a minidump beside the artifact, capped by `MaxMinidumpBytes` (default 4 MiB, maximum 32 MiB). The dump stays on disk and is not embedded in the OTLP payload. It is Windows-only. macOS, iOS, and Android players record `SIGSEGV`, `SIGBUS`, `SIGABRT`, `SIGILL`, and `SIGFPE` through the native helper in `Plugins/` (`libunimetry_crash` on macOS and Android, a static library on iOS). `os.type` is `windows`, `darwin`, `ios`, or `android`. Run `Native~/build.sh` to produce the plugin binaries (Android needs the NDK; `ANDROID_NDK_HOME` or the SDK `ndk/` folder). A launch that leaves capture disabled deletes leftover artifacts, minidumps, and the breadcrumb file. `Sanitizer` runs on the crash message, managed stack, and breadcrumb text before export.

## Traces, baggage, and metrics

```csharp
UnimetryTrace.ExtractTraceParent(traceparentHeader);
UnimetryTrace.SetBaggage("user.id", "player-1");
using (var span = UnimetryTrace.Start("match.load"))
{
    var outgoing = UnimetryTrace.FormatTraceParent(span);
}

UnimetryMetric.AddCounter(
    "game.match.started",
    1,
    "{match}",
    new MetricAttribute("region", "jp"));
UnimetryMetric.AddUpDownCounter(
    "game.players.active",
    -1,
    "{player}",
    new MetricAttribute("region", "jp"));
UnimetryMetric.RecordGauge(
    "game.queue.depth",
    7,
    "{item}",
    new MetricAttribute("queue", "ranked"));
UnimetryMetric.RecordHistogram(
    "game.match.duration",
    842.5,
    "ms",
    new MetricAttribute("mode", "ranked"));
```

`ExtractTraceParent` accepts a W3C `traceparent`. `FormatTraceParent` writes the current span as `00-{trace}-{span}-01` for outgoing HTTP. Where `Activity.Current` has a trace, errors use that trace id and parent span. `Runtime/link.xml` keeps `Activity` for IL2CPP when `System.Diagnostics.DiagnosticSource` is present. Gameplay spans export as OTLP traces with status OK. Baggage is copied onto span attributes.

`CaptureMetrics` (default on) exports built-in gauges plus application counters, up/down counters, gauges, and histograms to `/v1/metrics`. The built-in metrics are `unity.fps`, `unity.memory.used_bytes`, and `unity.startup.duration_s`. Set `CaptureBuiltInMetrics = false` to stop all three measurements and omit them from export; application metrics remain enabled. When no application metrics are pending, the empty metrics request is skipped as well. The same setting is available in the `UnimetrySettings` asset under **Project Settings > Unimetry**. `AddCounter` accepts positive deltas and exports a monotonic delta sum. `AddUpDownCounter` accepts positive and negative deltas for additive current values. `RecordGauge` exports the latest value recorded before each flush. `RecordHistogram` accepts finite observations and uses the OpenTelemetry default explicit bucket boundaries. All application metric APIs accept typed `MetricAttribute` values, and each metric name plus canonical attribute set is aggregated as a separate time series. Keep attributes low-cardinality: at most four are retained per measurement and at most 128 distinct time series are retained per initialization. A measurement that would create a 129th series is ignored, while known series continue to aggregate. A metric name has one fixed kind and unit. Metrics are aggregated between flushes; a failed request restores the batch for retry.

The offline queue is encrypted with AES-256-CBC and HMAC-SHA256. The key file is `persistentDataPath/unimetry/offline.key`, next to the queue, so this stops casual reading of `queue.json` and does not protect a copy of the whole directory.

Create `Assets/Resources/UnimetrySettings.asset` from **Project Settings > Unimetry**. If `UNIMETRY_OTLP_ENDPOINT` is unset, startup loads that asset. The sample Collector in `samples/collector` forwards traces to Jaeger at `http://localhost:16686`. OpenUPM can publish this repo from the root `package.json` (`com.konnta0.unimetry`).

## Continuous integration

Pushes and pull requests run license-free checks for UPM metadata, version alignment, Unity `.meta` files, GUID uniqueness, and the Aspire sample build. Unity EditMode tests are an optional manual workflow because starting the Unity Editor requires activation. When activation secrets are absent, that workflow reports that the tests were skipped and exits successfully. Configure `UNITY_LICENSE`, a `UNITY_LICENSING_SERVER`, or Unity account credentials to enable it.

## License

MIT
