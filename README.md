# Unimetry

**English** | [日本語](README.ja.md)

A crash and error instrumentation library for Unity. It treats managed exceptions and Unity logs as OpenTelemetry **Logs** and **Traces**, and sends them to a Collector over **OTLP/HTTP (JSON)**.

It does not depend on the standard OpenTelemetry .NET SDK. A minimal OTLP exporter is implemented in this package so it can run under Unity's runtime limits (no `Meter`, limited `DiagnosticSource`, and similar constraints).

## Current scope (v0.1)

| Area | Status |
| --- | --- |
| Unity `LogType.Error` / `Exception` / `Assert` | Supported |
| `AppDomain.UnhandledException` | Supported |
| `TaskScheduler.UnobservedTaskException` | Supported |
| Manual `UnimetryClient.Report(...)` | Supported |
| `[Event]` / `UnimetryEvent.Begin` / `Start` / `Write` | Supported |
| OTLP Logs (`/v1/logs`) | Supported |
| OTLP Traces (`/v1/traces`, error spans) | Supported |
| Offline persistent queue + retry | Supported |
| Native crashes on Windows players | Supported (opt-in, uploaded on the next launch) |
| Native crashes on iOS, Android, and macOS | **Not supported** |
| Automatic correlation with an existing trace context | **Not supported** (Phase 3) |

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

Open `samples~/UnitySample` in Unity Hub. See [samples~/UnitySample/README.md](samples~/UnitySample/README.md).

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
}.WithConsoleLog());
```

`WithConsoleLog()` writes error logs and events through `Debug.unityLogger`, including a log handler the user has already installed. Use `WithLog` to forward them to your own logger.

```csharp
options.WithLog(static (in UnimetryLogEntry entry) =>
{
    MyLogger.Info(entry.Name);
});
```

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

`[Event]` is woven by Unity's IL Post Processor. Generic methods, iterators, `async void`, and local functions are left unchanged and produce a warning. Define `UNIMETRY_DISABLE_EVENT_WEAVE` to disable weaving. Tag types are `string`, `bool`, `int`, `long`, and `double`.

High-frequency events are not written to disk. They are sent to `/v1/logs` from an in-memory ring buffer. Overflow is dropped and only the drop count is kept. Call `FlushAsync` before `Shutdown` to send events that are still buffered.

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

Windows standalone players can record a native crash and upload it on the next launch. Capture stays off until the game sets `CaptureNativeCrashes` after its own consent UI. iOS, Android, and macOS are not captured yet.

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

`CaptureMinidumps` writes a minidump beside the artifact, capped by `MaxMinidumpBytes` (default 4 MiB, maximum 32 MiB). The dump stays on disk and is not embedded in the OTLP payload. macOS and iOS players record `SIGSEGV`, `SIGBUS`, `SIGABRT`, `SIGILL`, and `SIGFPE` through the native helper in `Plugins/`. Android remains deferred. A launch that leaves capture disabled deletes leftover artifacts, minidumps, and the breadcrumb file. `Sanitizer` runs on the crash message, managed stack, and breadcrumb text before export.

## Traces, baggage, and metrics

```csharp
UnimetryTrace.ExtractTraceParent(traceparentHeader);
UnimetryTrace.SetBaggage("user.id", "player-1");
using (UnimetryTrace.Start("match.load"))
{
}
```

`ExtractTraceParent` accepts a W3C `traceparent`. Where `Activity.Current` has a trace, errors use that trace id and parent span. Gameplay spans export as OTLP traces with status OK. Baggage is copied onto span attributes. `CaptureMetrics` (default on) exports `unity.fps`, `unity.memory.used_bytes`, and `unity.startup.duration_s` to `/v1/metrics`.

The offline queue is encrypted with AES-256-CBC and HMAC-SHA256. The key file is `persistentDataPath/unimetry/offline.key`, next to the queue, so this stops casual reading of `queue.json` and does not protect a copy of the whole directory.

Create `Assets/Resources/UnimetrySettings.asset` from **Project Settings > Unimetry**. If `UNIMETRY_OTLP_ENDPOINT` is unset, startup loads that asset. The sample Collector in `samples/collector` forwards traces to Jaeger at `http://localhost:16686`. OpenUPM can publish this repo from the root `package.json` (`com.konnta0.unimetry`).

## License

MIT
