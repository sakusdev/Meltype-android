# Meltype for Android

Android の正式な IME (`InputMethodService`) として `Meltype.Core` を動かす移植版です。

## 現在の状態

現在の Android port では次を実装しています。

- Android の入力方法として登録できる `InputMethodService`
- QWERTY ソフトウェアキーボード
- `MeltypeSession` へのキー入力橋渡し
- `InputConnection.setComposingText()` を使った未確定文字列
- Meltype / Mozc の候補一覧を表示する候補バー
- 前後 20 文字を Meltype の文脈判定へ渡す処理
- かな / ABC（直接入力）切り替え
- エディタの Search / Send / Done / Next などに対応する Enter 処理
- Android のライト / ダークテーマに合わせた Gboard 系のキー UI
- ジェスチャーナビゲーション / IME 切替ボタンとの重なりを避ける下部安全余白
- GitHub Actions で arm64-v8a APK を生成

## Mozc

Android 版はデスクトップ版の `meltype_mozc_helper` プロセスを使わず、Mozc をネイティブ共有ライブラリとして組み込みます。

```text
Meltype.Core
  ↓
MozcNativeConverter.cs
  ↓ P/Invoke
libmeltype_mozc.so
  ↓
Mozc Engine + mozc.data
```

GitHub Actions では pinned Mozc source から以下を生成します。

- `libmeltype_mozc.so` (`arm64-v8a`)
- OSS `mozc.data`
- 上記を含む Android APK

ネイティブ Mozc の初期化に失敗した場合は `AndroidFallbackConverter` にフォールバックします。

## 対応 ABI

現在の CI artifact は `arm64-v8a` 向けです。Pixel など一般的な現行 Android arm64 端末を対象にしています。

x86_64 emulator などの追加 ABI は今後対応予定です。

## ビルド

.NET 10 SDK、Android workload、Bazel/Bazelisk と Mozc の Android build dependencies が必要です。

通常は GitHub Actions の `Android` workflow を利用するのが簡単です。workflow は Mozc bridge と `mozc.data` を生成してから APK を publish します。

Android project 単体の publish は次の形式です（native assets が事前生成されている必要があります）。

```bash
dotnet workload install android
dotnet publish src/Meltype.Android/Meltype.Android.csproj \
  -c Release \
  -f net10.0-android \
  -p:AndroidPackageFormat=apk
```

## インストール後

1. Meltype for Android を開く
2. 「Meltype を有効にする」から入力方法を有効化
3. 「Meltype を選択する」から Meltype に切り替える
4. 任意のテキスト欄で入力する

実機で問題が起きた場合は、まず次で IME のクラッシュログを確認できます。

```bash
adb logcat | grep -i meltype
```
