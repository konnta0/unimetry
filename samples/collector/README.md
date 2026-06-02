# Unimetry local collector

```bash
docker compose up
```

OTLP HTTP endpoint: `http://localhost:4318`

Unity 側設定例:

```csharp
UnimetryClient.Initialize(new UnimetryOptions
{
    Endpoint = "http://localhost:4318",
    ServiceName = "unimetry-sample",
    AllowInsecureTls = true,
});
```

テスト手順:

1. Collector を起動
2. Unity プロジェクトで Unimetry を初期化
3. 意図的に `throw new Exception("unimetry test");` を実行
4. Collector の debug exporter 出力に `/v1/logs` と `/v1/traces` が流れることを確認
