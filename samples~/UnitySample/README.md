# Unity Sample Project

**English** | [日本語](README.ja.md)

A Unity project for checking that Unimetry works.

## Requirements

- Unity **2021.3 LTS** or later
- Local package reference: `Packages/manifest.json` → `"com.konnta0.unimetry": "file:../../"`

## Open the project

1. Open `samples~/UnitySample` in Unity Hub
2. After the first import, attach `Assets/UnimetrySample/UnimetrySampleBootstrap.cs` to a GameObject in the scene
3. Optional: start the Collector in `samples/collector` ([README](../../samples/collector/README.md))

```bash
cd samples/collector
docker compose up
```

## Sample actions

### Play Mode

- `UnimetrySampleBootstrap` initializes Unimetry on startup
- From the Inspector you can call `ReportSampleException`, `EmitSampleErrorLog`, `EmitSampleEvent`, and `FlushNow`

### Editor menu

- `Unimetry/Sample/Initialize For Play Mode`
- `Unimetry/Sample/Report Test Exception`
- `Unimetry/Sample/Emit Test Error Log`
- `Unimetry/Sample/Emit Test Event`

## Tests

EditMode tests live in `Assets/Tests/EditMode`.

### From the Unity Editor

`Window > General > Test Runner > EditMode > Run All`

### CLI

```bash
/Applications/Unity/Hub/Editor/2021.3.12f1/Unity.app/Contents/MacOS/Unity \
  -batchmode \
  -nographics \
  -projectPath samples~/UnitySample \
  -runTests \
  -testPlatform editmode \
  -assemblyNames Unimetry.Tests.Editor \
  -testResults TestResults/results.xml \
  -logFile /tmp/unimetry-test.log
```

## What the tests cover

| Test | Coverage |
| --- | --- |
| `OtlpJsonWriterTests` | OTLP JSON payload and semantic conventions |
| `UtilitiesTests` | Fingerprint, trace id, and stack trace formatting |
| `PersistentQueueTests` | Persistent queue order, trim, and re-queue |
| `UnimetryClientTests` | Initialization and manual report |
| `EventTests` | Event start/end, tags, console output, and `[Event]` weaving |
| `CrashTests` | Native-crash artifacts, breadcrumbs, and next-launch OTLP logs |
| `RoadmapTests` | Trace context, gameplay spans, metrics JSON, and queue encryption |
