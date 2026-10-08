# Unimetry

[English](README.md) | **日本語**

Unity 向けのクラッシュ / エラー計装ライブラリ。管理例外と Unity ログを OpenTelemetry の **Logs** と **Traces** として解釈し、**OTLP/HTTP (JSON)** で Collector へ送信します。

標準の OpenTelemetry .NET SDK に依存せず、Unity のランタイム制約（`Meter` 非対応、`DiagnosticSource` 限定など）を回避するため、最小限の OTLP エクスポーターを自前実装しています。

## 現状のスコープ (v0.1)

| 対象 | 状態 |
| --- | --- |
| Unity `LogType.Error` / `Exception` / `Assert` | 対応 |
| `AppDomain.UnhandledException` | 対応 |
| `TaskScheduler.UnobservedTaskException` | 対応 |
| 手動 `UnimetryClient.Report(...)` | 対応 |
| `[Event]` / `UnimetryEvent.Begin` / `Start` / `Write` | 対応 |
| OTLP Logs (`/v1/logs`) | 対応 |
| OTLP Traces (`/v1/traces`, エラー span) | 対応 |
| オフライン永続キュー + 再送 | 対応 |
| Windows プレイヤーのネイティブクラッシュ | 対応 (opt-in、次回起動時に送信) |
| iOS / Android / macOS のネイティブクラッシュ | **未対応** |
| 既存 Trace コンテキストとの自動相関 | **未対応** (Phase 3) |

## アーキテクチャ

レイヤー分離とデータフローは [docs/ARCHITECTURE.ja.md](docs/ARCHITECTURE.ja.md) を参照してください。

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

Unity Hub で `samples~/UnitySample` を開いてください。詳細は [samples~/UnitySample/README.ja.md](samples~/UnitySample/README.ja.md) を参照。Aspire ダッシュボードへ Unity から要求を送るときは [samples~/Aspire](samples~/Aspire/README.ja.md) を起動し、Unimetry の endpoint を `http://localhost:18890` にしてください。

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
}.WithConsoleLog());
```

`WithConsoleLog()` はエラーログと Event を `Debug.unityLogger` に出します。ユーザーが差し替えたログハンドラをそのまま通ります。独自のロガーへ渡すときは `WithLog` を使います。

```csharp
options.WithLog(static (in UnimetryLogEntry entry) =>
{
    MyLogger.Info(entry.Name);
});
```

## Event

Event は OTLP Log の `eventName` 付きレコードです。`timeUnixNano` が開始、`observedTimeUnixNano` が終了、`unimetry.event.duration_ns` が経過時間です。未初期化、または `CaptureEvents = false` のとき、同期の `Begin` はアロケーションしません。

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

`SetAttribute` は、その後のすべての Event（`Write` と `[Event]` を含む）にコピーされる属性を保存します。ログイン後に一度設定します。同じキーの明示的なタグがあるイベントでは、そのイベントの値が優先されます。共通属性は最大 8 個です。文字列の `null` はそのキーを外します。ログアウト時は `ClearAttributes` でまとめて外します。

```csharp
UnimetryEvent.SetAttribute("user.id", userId);

using (var click = UnimetryEvent.Begin("ui.button.click"))
{
    click.SetTag("ui.button", "play");
}
```

`[Event]` は Unity の IL Post Processor が織ります。generic method、iterator、`async void`、local function は対象外で、警告を出してメソッドはそのまま残します。`UNIMETRY_DISABLE_EVENT_WEAVE` を定義すると織り込みを止めます。タグに使える型は `string`、`bool`、`int`、`long`、`double` です。

高頻度の Event はディスクへ書かず、メモリ上のリングバッファから `/v1/logs` に送ります。溢れた分は捨てて件数だけ数えます。`Shutdown` の前に `FlushAsync` を呼ぶと、残っている Event を送れます。

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

## ネイティブクラッシュ

Windows スタンドアロンプレイヤーは、ネイティブクラッシュを記録し、次回起動時に送信できます。ゲーム側の同意 UI のあとで `CaptureNativeCrashes` を有効にするまで、キャプチャはオフです。iOS、Android、macOS はまだ対象外です。

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

`AddBreadcrumb` は短い軌跡を保持します (既定は直近 30 秒、64 件)。ネイティブクラッシュキャプチャが無効のときは何もしません。未処理例外フィルタは Windows スタンドアロンプレイヤーにだけ入り、Editor では入りません。未処理のネイティブ例外は `persistentDataPath/unimetry/crashes/*.crash.json` に書かれます。次回起動時にキャプチャがまだ有効なら、`unimetry.record_type=crash`、`crash.signal`、`device.model`、`os.type`、`app.build_id` を付けた OTLP Log をキューへ入れます。`ExportErrorSpans` が true のときは `unity.crash` span を 1 本含めます。

`CaptureMinidumps` はアーティファクトの隣にミニダンプを書きます。上限は `MaxMinidumpBytes` (既定 4 MiB、最大 32 MiB) です。ダンプはディスクに残り、OTLP ペイロードには埋め込みません。macOS と iOS プレイヤーは `Plugins/` のネイティブヘルパーで `SIGSEGV`、`SIGBUS`、`SIGABRT`、`SIGILL`、`SIGFPE` を記録します。Android は後回しです。キャプチャを無効にした起動では、残っているアーティファクト、ミニダンプ、breadcrumb ファイルを削除します。送信前に `Sanitizer` がクラッシュ本文、managed stack、breadcrumb テキストへ適用されます。

## トレース、baggage、メトリクス

```csharp
UnimetryTrace.ExtractTraceParent(traceparentHeader);
UnimetryTrace.SetBaggage("user.id", "player-1");
using (var span = UnimetryTrace.Start("match.load"))
{
    var outgoing = UnimetryTrace.FormatTraceParent(span);
}
```

`ExtractTraceParent` は W3C `traceparent` を受けます。`FormatTraceParent` は現在の span を outgoing HTTP 用の `00-{trace}-{span}-01` にします。`Activity.Current` にトレースがあるときは、その trace id と親 span をエラーに使います。ゲームプレイ span は status OK の OTLP trace として送ります。baggage は span attribute にコピーします。`CaptureMetrics` (既定オン) は `unity.fps`、`unity.memory.used_bytes`、`unity.startup.duration_s` を `/v1/metrics` へ送ります。

オフラインキューは AES-256-CBC と HMAC-SHA256 で暗号化します。鍵はキューの隣の `persistentDataPath/unimetry/offline.key` です。`queue.json` をそのまま読めなくします。ディレクトリごとコピーされた場合の保護にはなりません。

**Project Settings > Unimetry** から `Assets/Resources/UnimetrySettings.asset` を作れます。`UNIMETRY_OTLP_ENDPOINT` が無いときは、起動時にこのアセットを読みます。`samples/collector` の Collector はトレースを Jaeger (`http://localhost:16686`) へ転送します。OpenUPM はこのリポジトリ直下の `package.json` (`com.konnta0.unimetry`) から公開できます。

## ライセンス

MIT
