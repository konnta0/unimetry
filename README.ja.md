# Unimetry

[English](README.md) | **日本語**

Unity 向けのクラッシュ / エラー計装ライブラリ。管理例外と Unity ログを OpenTelemetry の **Logs** と **Traces** として解釈し、**OTLP/HTTP (JSON)** で Collector へ送信します。

標準の OpenTelemetry .NET SDK に依存せず、Unity のランタイム制約（`Meter` 非対応、`DiagnosticSource` 限定など）を回避するため、最小限の OTLP エクスポーターを自前実装しています。

## 現状のスコープ (v0.6.0)

| 対象 | 状態 |
| --- | --- |
| Unity `LogType.Error` / `Exception` / `Assert` | 対応 |
| `AppDomain.UnhandledException` | 対応 |
| `TaskScheduler.UnobservedTaskException` | 対応 |
| 手動 `UnimetryClient.Report(...)` | 対応 |
| `[Event]` / `UnimetryEvent.Begin` / `Start` / `Write` / 共通属性 | 対応 |
| OTLP Logs (`/v1/logs`) | 対応 |
| OTLP Traces (`/v1/traces`, エラー / ゲームプレイ span) | 対応 |
| OTLP Metrics (`/v1/metrics`) | 対応 |
| W3C `traceparent` / `UnimetryTrace` | 対応 |
| オフライン永続キュー + 再送 | 対応 |
| Windows / macOS / iOS / Android のネイティブクラッシュ | 対応 (opt-in、次回起動時に送信) |

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

### UniTask 連携（任意）

UniTask 2.x が UPM で導入されている場合、Unimetry は `com.cysharp.unitask` を検出し、定期 flush から OTLP リクエストまでの内部処理をネイティブ `UniTask` へ自動的に切り替えます。互換性のため既存の `Task` APIも残ります。最後の `Task` 互換変換による割り当ても避ける場合は、UniTask専用APIを呼びます。

```csharp
using Cysharp.Threading.Tasks;

await UnimetryClient.FlushUniTaskAsync(cancellationToken);
await UnimetryClient.ShutdownUniTaskAsync(cancellationToken);
```

UPMではなくアセットとしてUniTaskを導入したプロジェクトでは、`UNIMETRY_UNITASK` scripting defineを設定すると同じ経路を有効にできます。UniTaskが存在しないプロジェクトでは、従来どおり`Task`でコンパイル・動作します。

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

`[Event]` は Unity の IL Post Processor が織ります。`Task`、`ValueTask`、UniTask の async state machine に対応します。同期メソッドはユーザー本体を複製せず、ブレークポイントが実際に走る IL に載ります。capture が無効なときは割り当てしません。generic method、iterator、`async void`、local function は `UM004` compiler error にして、計測が静かに消えないようにします。`UNIMETRY_DISABLE_EVENT_WEAVE` を定義すると織り込みを止めます。タグに使える型は `string`、`bool`、`int`、`long`、`double` です。

完了した Event は `persistentDataPath/unimetry` 以下へ暗号化して永続化し、`/v1/logs` に送ります。上限を超えた分は捨てて件数を数えます。`Shutdown` 後も未送信分はディスクに残ります。最後のネットワーク flush を待てる場合は `await UnimetryClient.ShutdownAsync()` を使ってください。

resource 属性は error、span、Event の記録時にスナップショットします。別アカウントの未送信データが、後からログインしたアカウントの identity で送られることはありません。再初期化せずに identity を更新できます。

```csharp
UnimetryClient.SetResourceAttribute("app.user_id", userId); // null で削除
```

`Sanitizer` は Event に付いた例外にも適用されます。`ErrorFilter` を使うと、自動捕捉の経路を永続キューへ入る前に停止できます。

```csharp
options.ErrorFilter = error => error.Source != CapturedErrorSource.UnityLog;
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

## ネイティブクラッシュ

Windows、macOS、iOS、Android プレイヤーは、ネイティブクラッシュを記録し、次回起動時に送信できます。ゲーム側の同意 UI のあとで `CaptureNativeCrashes` を有効にするまで、キャプチャはオフです。Editor の Play ではネイティブハンドラは入りません。

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

`CaptureMinidumps` はアーティファクトの隣にミニダンプを書きます。上限は `MaxMinidumpBytes` (既定 4 MiB、最大 32 MiB) です。ダンプはディスクに残り、OTLP ペイロードには埋め込みません。ミニダンプは Windows のみです。macOS、iOS、Android プレイヤーは `Plugins/` のネイティブヘルパーで `SIGSEGV`、`SIGBUS`、`SIGABRT`、`SIGILL`、`SIGFPE` を記録します（macOS / Android は `libunimetry_crash`、iOS は静的ライブラリ）。`os.type` は `windows`、`darwin`、`ios`、`android` です。プラグインバイナリは `Native~/build.sh` で作ります（Android は NDK が必要。`ANDROID_NDK_HOME` か SDK の `ndk/`）。キャプチャを無効にした起動では、残っているアーティファクト、ミニダンプ、breadcrumb ファイルを削除します。送信前に `Sanitizer` がクラッシュ本文、managed stack、breadcrumb テキストへ適用されます。

## トレース、baggage、メトリクス

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

`ExtractTraceParent` は W3C `traceparent` を受けます。`FormatTraceParent` は現在の span を outgoing HTTP 用の `00-{trace}-{span}-01` にします。`Activity.Current` にトレースがあるときは、その trace id と親 span をエラーに使います。`System.Diagnostics.DiagnosticSource` があるときは `Runtime/link.xml` が IL2CPP 向けに `Activity` を残します。ゲームプレイ span は status OK の OTLP trace として送ります。baggage は span attribute にコピーします。

`CaptureMetrics` (既定オン) は `unity.fps`、`unity.memory.used_bytes`、`unity.startup.duration_s` の gauge に加え、アプリケーションの counter / up-down counter / gauge / histogram を `/v1/metrics` へ送ります。`AddCounter` は正の差分を単調増加のDelta Sumとして送信します。`AddUpDownCounter` は加減算される現在値向けに正負の差分を受け取ります。`RecordGauge` はflush前に記録された最新値を送信します。`RecordHistogram` は有限値をOpenTelemetryの既定明示境界で集計します。すべてのアプリケーションメトリクスAPIで型付きの `MetricAttribute` を指定でき、メトリクス名と正規化済み属性セットの組み合わせごとに別の時系列として集約します。カーディナリティを低く保つため、1計測につき最大4属性、初期化ごとに最大128時系列を保持します。129番目の時系列を新規作成する計測は無視し、既存時系列への計測は引き続き集約します。同じメトリクス名のkindとunitは固定です。メトリクスはflush間で集約し、送信失敗時はバッチを戻して再試行します。

オフラインキューは AES-256-CBC と HMAC-SHA256 で暗号化します。鍵はキューの隣の `persistentDataPath/unimetry/offline.key` です。`queue.json` をそのまま読めなくします。ディレクトリごとコピーされた場合の保護にはなりません。

**Project Settings > Unimetry** から `Assets/Resources/UnimetrySettings.asset` を作れます。`UNIMETRY_OTLP_ENDPOINT` が無いときは、起動時にこのアセットを読みます。`samples/collector` の Collector はトレースを Jaeger (`http://localhost:16686`) へ転送します。OpenUPM はこのリポジトリ直下の `package.json` (`com.konnta0.unimetry`) から公開できます。

## 継続的インテグレーション

pushとpull requestでは、Unityライセンスを使わずにUPMメタデータ、バージョン表記、Unity `.meta`ファイル、GUID重複、Aspireサンプルのビルドを検証します。Unity Editorの起動には認証が必要なため、EditModeテストは任意の手動workflowです。認証secretがなければテストをスキップしたことを表示して正常終了します。実行する場合は`UNITY_LICENSE`、`UNITY_LICENSING_SERVER`、またはUnityアカウント認証情報を設定します。

## ライセンス

MIT
