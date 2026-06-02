# Unimetry

Unity 向けのクラッシュ / エラー計装ライブラリ。管理例外と Unity ログを OpenTelemetry の **Logs** と **Traces** として解釈し、**OTLP/HTTP (JSON)** で Collector へ送信します。

標準の OpenTelemetry .NET SDK に依存せず、Unity のランタイム制約（`Meter` 非対応、`DiagnosticSource` 限定など）を回避するため、最小限の OTLP エクスポーターを自前実装しています。

## 現状のスコープ (v0.1)

| 対象 | 状態 |
| --- | --- |
| Unity `LogType.Error` / `Exception` / `Assert` | 対応 |
| `AppDomain.UnhandledException` | 対応 |
| `TaskScheduler.UnobservedTaskException` | 対応 |
| 手動 `UnimetryClient.Report(...)` | 対応 |
| OTLP Logs (`/v1/logs`) | 対応 |
| OTLP Traces (`/v1/traces`, エラー span) | 対応 |
| オフライン永続キュー + 再送 | 対応 |
| ネイティブクラッシュ (IL2CPP / iOS / Android SIG*) | **未対応** (Phase 2) |
| 既存 Trace コンテキストとの自動相関 | **未対応** (Phase 3) |

## アーキテクチャ

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

### OTel へのマッピング

| Unity イベント | OTel Logs | OTel Traces |
| --- | --- | --- |
| 例外 | `severityNumber=17/21`, `body=message` | `unity.error` span, `status=ERROR` |
| 属性 | `exception.type`, `exception.message`, `exception.stacktrace`, `exception.escaped` | span attributes + `exception` event |
| リソース | `service.name`, `service.version`, `deployment.environment` | 同上 |

`unimetry.source`, `unimetry.fingerprint` はライブラリ固有属性です。

## インストール

### ローカル sample プロジェクト

```bash
git clone https://github.com/konnta0/unimetry.git
```

Unity Hub で `samples~/UnitySample` を開いてください。詳細は [samples~/UnitySample/README.md](samples~/UnitySample/README.md) を参照。

### 他プロジェクトへの組み込み

1. このリポジトリを Unity プロジェクトの `Packages/manifest.json` に追加:

```json
{
  "dependencies": {
    "com.konnta0.unimetry": "https://github.com/konnta0/unimetry.git?path=/"
  }
}
```

2. 起動時に初期化:

```csharp
using Unimetry;

UnimetryClient.Initialize(new UnimetryOptions
{
    Endpoint = "http://localhost:4318",
    ServiceName = "my-game",
    ServiceVersion = Application.version,
    DeploymentEnvironment = "development",
    AllowInsecureTls = true,
});
```

### 環境変数による自動初期化

| 変数 | 説明 |
| --- | --- |
| `UNIMETRY_OTLP_ENDPOINT` | 設定時のみ自動初期化 |
| `UNIMETRY_SERVICE_NAME` | 省略時 `Application.productName` |
| `UNIMETRY_SERVICE_VERSION` | 省略時 `Application.version` |
| `UNIMETRY_DEPLOYMENT_ENVIRONMENT` | 省略時 `production` |
| `UNIMETRY_ALLOW_INSECURE_TLS` | `1` / `true` で TLS 検証を無効化 |

## Collector 設定例

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

## ロードマップ

詳細は [docs/ROADMAP.md](docs/ROADMAP.md) を参照してください。

- **Phase 2**: ネイティブクラッシュ (次回起動時送信用の minidump / tombstone 収集)
- **Phase 3**: `Activity.Current` との相関、ゲームプレイ span 用 API
- **Phase 4**: Metrics (FPS, memory) の OTLP export

## ライセンス

MIT
