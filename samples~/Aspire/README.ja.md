# Unimetry Aspire sample

[English](README.md) | **日本語**

小さなゲーム API と Aspire ダッシュボードを起動する AppHost です。Unity サンプルは W3C `traceparent` 付きで API へ HTTP 要求を送れます。Unimetry の OTLP/HTTP JSON もダッシュボードへ送れます。

このフォルダは `samples~` 配下にあり、Unity が .NET プロジェクトやビルド成果物を取り込みません。

## 前提

- .NET SDK **9.0** 以降

## 起動

```bash
cd samples~/Aspire
dotnet run --project Unimetry.AppHost --launch-profile http
```

| サービス | URL |
| --- | --- |
| Aspire ダッシュボード | AppHost の出力に表示 |
| API | `http://localhost:5288` |
| ダッシュボード OTLP/HTTP JSON | `http://localhost:18890` |

http 起動プロファイルでは、Unity が `x-otlp-api-key` なしで POST できるようにダッシュボード OTLP を認証なしにしています。localhost 専用です。

```bash
curl http://localhost:5288/match/load
curl -X POST http://localhost:5288/error
```

## Unity

1. AppHost を起動する。
2. `samples~/UnitySample` を開く。
3. `UnimetrySampleBootstrap` の **Endpoint** を `http://localhost:18890` にし、**Api Base Url** は `http://localhost:5288` のままにする。
4. Play Mode で **Call Aspire Match Load** または **Call Aspire Error** を呼ぶ。Editor の `Unimetry/Sample` メニューからも同じ操作ができる。

Unity 側は `UnimetryTrace` span を開始し、HTTP 要求に `traceparent` を付け、`UnimetryEvent` を記録します。API はそのトレースを続けます。両方とも Aspire ダッシュボードに出ます。

Collector サンプル (`http://localhost:4318`) もそのまま使えます。AppHost を動かしていないときは、その endpoint を使います。

ホストしているダッシュボードが API キーを要求する場合は、`UnimetryOptions.Headers` に `x-otlp-api-key` を入れてください。
