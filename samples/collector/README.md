# Unimetry local collector

**English** | [日本語](README.ja.md)

```bash
docker compose up
```

OTLP HTTP endpoint: `http://localhost:4318`

Jaeger UI: `http://localhost:16686`

Example Unity setup:

```csharp
UnimetryClient.Initialize(new UnimetryOptions
{
    Endpoint = "http://localhost:4318",
    ServiceName = "unimetry-sample",
    AllowInsecureTls = true,
});
```

Test steps:

1. Start the Collector
2. Initialize Unimetry in the Unity project
3. Throw on purpose: `throw new Exception("unimetry test");`
4. Confirm that `/v1/logs` and `/v1/traces` show up in the Collector debug exporter output
