# Unity Sample Project

Unimetry の動作確認用 Unity プロジェクトです。

## 前提

- Unity **2021.3 LTS** 以降
- ローカル package 参照: `Packages/manifest.json` → `"com.konnta0.unimetry": "file:../../"`

## 開き方

1. Unity Hub で `samples~/UnitySample` を開く
2. 初回 import 後、`Assets/UnimetrySample/UnimetrySampleBootstrap.cs` をシーン内の GameObject にアタッチ
3. 任意: `samples/collector` の Collector を起動

```bash
cd samples/collector
docker compose up
```

## サンプル操作

### Play Mode

- `UnimetrySampleBootstrap` が起動時に Unimetry を初期化
- Inspector から `ReportSampleException` / `EmitSampleErrorLog` / `FlushNow` を呼び出し可能

### Editor Menu

- `Unimetry/Sample/Initialize For Play Mode`
- `Unimetry/Sample/Report Test Exception`
- `Unimetry/Sample/Emit Test Error Log`

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
