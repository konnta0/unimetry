# Unity Sample Project

[English](README.md) | **日本語**

Unimetry の動作確認用 Unity プロジェクトです。

## 前提

- Unity **2021.3 LTS** 以降
- ローカル package 参照: `Packages/manifest.json` → `"com.konnta0.unimetry": "file:../../"`

## 開き方

1. Unity Hub で `samples~/UnitySample` を開く
2. 初回 import 後、`Assets/UnimetrySample/UnimetrySampleBootstrap.cs` をシーン内の GameObject にアタッチ
3. 任意: `samples/collector` の Collector を起動 ([README](../../samples/collector/README.ja.md))、または `samples~/Aspire` の Aspire AppHost を起動 ([README](../Aspire/README.ja.md))

```bash
cd samples/collector
docker compose up
```

## サンプル操作

### Play Mode

- `UnimetrySampleBootstrap` が起動時に Unimetry を初期化
- Inspector から `ReportSampleException` / `EmitSampleErrorLog` / `EmitSampleEvent` / `FlushNow` / `CallAspireMatchLoad` / `CallAspireError` を呼び出し可能
- Aspire を使うときは **Endpoint** を `http://localhost:18890` にし、**Api Base Url** は `http://localhost:5288` のままにする

### Editor Menu

- `Unimetry/Sample/Initialize For Play Mode`
- `Unimetry/Sample/Report Test Exception`
- `Unimetry/Sample/Emit Test Error Log`
- `Unimetry/Sample/Emit Test Event`
- `Unimetry/Sample/Call Aspire Match Load`
- `Unimetry/Sample/Call Aspire Error`

## テスト

EditMode tests は `Assets/Tests/EditMode` にあります。

### Unity Editor から

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

## テスト内容

| テスト | 内容 |
| --- | --- |
| `OtlpJsonWriterTests` | OTLP JSON payload と semantic conventions |
| `UtilitiesTests` | fingerprint / trace id / stack trace 整形 |
| `PersistentQueueTests` | 永続キュー順序・trim・再キュー |
| `UnimetryClientTests` | 初期化と手動 report |
| `EventTests` | Event の開始/終了、タグ、コンソール出力、`[Event]` 織り込み |
| `CrashTests` | ネイティブクラッシュの artifact、breadcrumb、次回起動時の OTLP Log |
| `RoadmapTests` | トレースコンテキスト、ゲームプレイ span、メトリクス JSON、キューの暗号化 |
