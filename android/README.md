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
- 数字・電話番号・URL・メールアドレス欄に合わせた初期入力モード
- パスワード欄の直接入力とプレビュー・学習の抑制
- `IME_FLAG_NO_PERSONALIZED_LEARNING` を指定した入力欄での学習停止（日本語変換は利用可能）
- 句読点・貼り付けの前に未確定文字を確定し、文字の置き換えを防ぐ処理
- 変換中の左右キーによる文節選択と、エディタ側の選択変更との同期
- 選択範囲と絵文字・結合文字を考慮した Backspace
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

パスワード欄では日本語の自動判定・候補表示・キーの拡大プレビューを使用しません。
アプリが学習停止フラグを指定した入力欄では、Meltype の学習ファイルへの保存と Mozc への学習通知を停止します。
通常の入力欄へ戻ると、通常の学習動作へ戻ります。

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

Android の入力方針・カーソル同期・Unicode 削除と、共通セッションの回帰テストは Android SDK なしでも実行できます。

```bash
dotnet run --project src/Meltype.Core.Tests/Meltype.Core.Tests.csproj -c Release
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
