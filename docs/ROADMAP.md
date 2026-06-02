# Unimetry ロードマップ

Unity の OTel 対応が実質的に難しい状況を前提に、段階的に「アプリとして必要な計装」を構築する計画です。

## 背景

- Unity では `System.Diagnostics.Metrics` や完全な .NET Diagnostics が使えないケースが多い
- 将来の CoreCLR 対応で改善される可能性はあるが、実運用投入は先
- ネイティブクラッシュ (IL2CPP, モバイル SIG*, Windows native) は標準 OTel だけではカバーできない
- そのため **OTLP 形式への変換レイヤー** と **Unity 固有の capture レイヤー** を分離する

## Phase 1 — 管理例外 MVP (v0.1, 現在)

**目的**: 開発中〜ステージングで、Collector 経由のエラー可視化をすぐ使える状態にする。

### 実装済み

- Unity ログ / 未処理例外 / Task 例外の capture
- OTel semantic conventions に沿った Log attributes
- エラー 1 件 = Log + (任意) Trace span
- OTLP/HTTP JSON (`/v1/logs`, `/v1/traces`)
- 永続キュー + バッチ flush

### 制約

- プロセス終了直前のネットワーク送信は best-effort
- 同一 fingerprint の短時間 dedupe あり
- JSON 手組みのため、protobuf より payload サイズは大きめ

## Phase 2 — ネイティブクラッシュ

**優先順位 (合意): Windows → iOS → Android (Android は後回し)**

**目的**: スタンドアロン / モバイルでの「本当のクラッシュ」を次回起動時に回収する。

### 設計案

```
Native signal / Unity crash handler
        │
        ▼
CrashArtifactWriter (disk, atomic write)
  - timestamp, signal, thread, registers (platform)
  - managed stack if available
  - last N seconds breadcrumbs
        │
        ▼ (next launch)
CrashArtifactUploader ──► OTLP Logs (record_type=crash)
```

### プラットフォーム別 (実装順)

| 順 | Platform | Capture | 備考 |
| --- | --- | --- | --- |
| 1 | **Windows** | Unhandled exception filter + minidump (optional) | 最初のターゲット。サイズ制限と同意 UI |
| 2 | **iOS** | PLCrashReporter または自前 signal handler | App Store 審査・シンボication 要検討 |
| 3 | Android | `libunimetry.so` + tombstone / Breakpad 相当 | Phase 2 後半 |
| - | macOS | Windows と同系統 | Windows 完了後に横展開 |

### OTel 表現

クラッシュは **Log** を主 signal とし、必要なら **Trace** は「クラッシュ span 1 本」に限定:

- `log.record.original` 相当の raw dump は object storage へ (Collector でルーティング)
- attributes: `exception.type`, `crash.signal`, `device.model`, `os.type`, `app.build_id`

### 工数見積 (優先順)

- Windows: 1〜2 週
- iOS: 2〜3 週
- Android: 2〜3 週 (後回し)
- 統合テスト + symbolication pipeline: 2 週

**合計: 約 2〜3 人月** (Android 除く初期リリースは約 1.5 人月)

## Phase 3 — Trace コンテキスト統合

**目的**: サーバー側 trace とクライアント error を `trace_id` で結ぶ。

### 方針

1. Unity 2022+ / 将来 CoreCLR で `ActivitySource` が使える環境では `Activity.Current` から `trace_id` / `span_id` を拾う
2. 使えない環境では HTTP ヘッダ (`traceparent`) からの inbound context を `AsyncLocal` 的に保持
3. ゲームプレイ API: `using var span = UnimetryTrace.Start("match.load");`

### OTel 互換

- W3C Trace Context propagation
- Baggage (user id, session id) は resource ではなく span attributes へ

## Phase 4 — Metrics

**目的**: SLI (FPS, memory, load time) を OTLP Metrics へ。

- Unity では `Meter` 非対応のため、Periodic OTLP metrics export を自前実装
- または Collector 側で logs から derived metrics を生成

## Phase 5 — 配信 / 運用

- UPM + OpenUPM 公開
- CI: Unity EditMode tests (JSON payload snapshot)
- Sample project + docker-compose (Collector + Jaeger)
- Editor 設定 UI (`ScriptableObject` or Project Settings)

## セキュリティ / プライバシー

- PII 自動収集しない (device id 等は opt-in)
- `UnimetryOptions.Sanitizer` で redaction
- TLS デフォルト有効、`AllowInsecureTls` は dev のみ
- オフラインファイルは `persistentDataPath`、暗号化は Phase 5

## 成功指標

1. Collector へ Log / Trace が到達し、Grafana / Jaeger で exception.stacktrace が検索できる
2. ネイティブクラッシュが次回起動 30 秒以内に 95% 以上アップロードされる (Phase 2)
3. 既存バックエンド OTel パイプラインへの追加設定が 30 分以内
