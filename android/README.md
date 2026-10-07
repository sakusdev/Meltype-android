# Meltype for Android

Android の正式な IME (`InputMethodService`) として Meltype.Core を動かす移植版です。

## 現在の状態

初期 MVP では次の部分を実装しています。

- Android の入力方法として登録できる IME service
- QWERTY ソフトウェアキーボード
- `MeltypeSession` へのキー入力橋渡し
- `InputConnection.setComposingText()` を使った未確定文字列
- Meltype の候補一覧を表示する候補バー
- 前後 20 文字を Meltype の文脈判定へ渡す処理
- かな / ABC（直接入力）切り替え
- GitHub Actions で APK を生成

## まだ未実装

Android から利用できる漢字変換エンジンです。

デスクトップ版の `MozcConverter` は別プロセスの `meltype_mozc_helper` と標準入出力で通信しますが、
Android 版ではこの方式を使わず、Mozc のネイティブライブラリを JNI / P/Invoke で接続する予定です。

それまでは `AndroidFallbackConverter` が読みをそのまま返します。
この状態でも Meltype の英語 / 日本語判定、ローマ字入力、composition の流れは確認できます。

## ビルド

.NET 10 SDK と Android workload が必要です。

```bash
dotnet workload install android
dotnet publish src/Meltype.Android/Meltype.Android.csproj \
  -c Release \
  -f net10.0-android \
  -p:AndroidPackageFormat=apk
```

GitHub Actions の `Android` workflow でも APK artifact を生成します。

## インストール後

1. Meltype for Android を開く
2. 「Meltype を有効にする」から入力方法を有効化
3. 「Meltype を選択する」から Meltype に切り替える
4. 任意のテキスト欄で入力する
