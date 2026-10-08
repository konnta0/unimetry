# Unimetry Aspire sample

**English** | [日本語](README.ja.md)

An Aspire AppHost that runs a small game API and the Aspire dashboard. The Unity sample can send HTTP requests to the API with a W3C `traceparent`, and can export Unimetry OTLP/HTTP JSON to the dashboard.

This folder lives under `samples~` so Unity does not import the .NET projects or their build output.

## Requirements

- .NET SDK **9.0** or later

## Run

```bash
cd samples~/Aspire
dotnet run --project Unimetry.AppHost --launch-profile http
```

| Service | URL |
| --- | --- |
| Aspire dashboard | printed in the AppHost output |
| API | `http://localhost:5288` |
| Dashboard OTLP/HTTP JSON | `http://localhost:18890` |

The http launch profile leaves dashboard OTLP unsecured so Unity can POST without `x-otlp-api-key`. Use that only on localhost.

```bash
curl http://localhost:5288/match/load
curl -X POST http://localhost:5288/error
```

## Unity

1. Start the AppHost.
2. Open `samples~/UnitySample`.
3. On `UnimetrySampleBootstrap`, set **Endpoint** to `http://localhost:18890` and keep **Api Base Url** as `http://localhost:5288`.
4. Enter Play Mode and call **Call Aspire Match Load** or **Call Aspire Error**. Editor menus under `Unimetry/Sample` do the same.

The Unity client starts a `UnimetryTrace` span, sends `traceparent` on the HTTP request, and records a `UnimetryEvent`. The API continues that trace. Both sides show up in the Aspire dashboard.

The Collector sample at `http://localhost:4318` still works. Use that endpoint when the AppHost is not running.

If a hosted dashboard requires an API key, set `UnimetryOptions.Headers` to include `x-otlp-api-key`.
