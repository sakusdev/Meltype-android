# Meltype for Android

Android の正式な IME (`InputMethodService`) として `Meltype.Core` を動かす移植版です。
元の Meltype は雪代 / Yukishiro 氏の GPL-3.0-or-later のプロジェクトです。この fork でも元の表示と GPL を保持します。

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
- 起動画面から読める GPL・辞書・依存部品のライセンス通知
- `android-v*` タグで署名 APK と対応ソースを同じ GitHub Release に添付

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
最低 OS は Android 8.0 (API 26) です。実機での検証は別途必要です。

x86_64 emulator などの追加 ABI は今後対応予定です。

## ビルド

.NET 10 SDK、Android workload、Bazel/Bazelisk と Mozc の Android build dependencies が必要です。

通常は GitHub Actions の `Android` workflow を利用するのが簡単です。workflow は Mozc bridge と `mozc.data` を生成してから APK を publish します。

手元でビルドする場合は、先にネイティブ部品と通知を生成します。Linux での例です。
`bazelisk` は workflow と同じ v1.29.0 を PATH に置いてください。Mozc 側の設定が必要な Android NDK を取得します。

```bash
dotnet workload install android
BAZEL=bazelisk native/mozc/build-android.sh "$HOME/mozc-android"
dotnet restore src/Meltype.Android/Meltype.Android.csproj
python3 android/distribution.py prepare --mozc "$HOME/mozc-android" --bazel bazelisk
dotnet publish src/Meltype.Android/Meltype.Android.csproj \
  -c Release \
  -f net10.0-android \
  -r android-arm64 \
  -p:AndroidPackageFormat=apk
```

この手元の例は開発用のテスト署名を使います。配布用の署名は以下の Secrets を設定した workflow を使用します。
生成された `Assets/licenses/`・`mozc.data`・`jniLibs/` は Git に追加しません。
既存の Mozc checkout が `MOZC_COMMIT` と異なる場合は停止するため、別の作業ディレクトリを使ってください。

Android の入力方針・カーソル同期・Unicode 削除と、共通セッションの回帰テストは Android SDK なしでも実行できます。

```bash
dotnet run --project src/Meltype.Core.Tests/Meltype.Core.Tests.csproj -c Release
python3 -m unittest discover -s android/tests -v
```

## 署名済み APK と GitHub Releases

`android-v0.2.0` のような **Android 専用タグ**を push すると、固定の配布用キーで署名した APK と SHA-256 チェックサムを GitHub Releases に添付します。
Windows 版の `v*` タグとは別のタグを使います。タグは Android 実装とこの workflow を含むコミットに付けてください（現在は `work/android-ime`）。

### 初回の署名設定

既存の配布用 keystore がある場合は、そのキーを使います。新規作成する場合は Java の `keytool` で作成できます。
パスワードはコマンドの対話入力で指定します。

```bash
keytool -genkeypair -v -storetype JKS \
  -keystore meltype-android-release.jks -alias meltype \
  -keyalg RSA -keysize 3072 -validity 10000
```

[Repository settings → Secrets and variables → Actions](https://github.com/sakusdev/Meltype-android/settings/secrets/actions) に次の Repository Secrets を登録します。

| Secret | 値 |
| --- | --- |
| `ANDROID_KEYSTORE_BASE64` | keystore ファイル全体の Base64 |
| `ANDROID_KEYSTORE_PASSWORD` | keystore のパスワード |
| `ANDROID_KEY_ALIAS` | キーの alias（上の例では `meltype`） |
| `ANDROID_KEY_PASSWORD` | キーのパスワード（同じパスワードなら keystore と同じ値） |

Base64 は Linux では `base64 -w 0 meltype-android-release.jks`、Windows PowerShell では次で取得できます。

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes((Resolve-Path './meltype-android-release.jks')))
```

配布用 keystore とパスワードは保管し、更新時も同じキーを使います。keystore は Git に追加しません。
以前の CI のテスト署名 APK が入っている端末では、配布用キーへ切り替える初回だけ旧 APK のアンインストールが必要です（その際、端末内の Meltype の設定・学習データは削除されます）。

### 公開

```bash
git switch work/android-ime
git pull --ff-only
git tag android-v0.2.0
git push origin android-v0.2.0
```

workflow が成功すると [Releases](https://github.com/sakusdev/Meltype-android/releases) に次を添付します。

- `Meltype-Android-0.2.0-arm64-v8a.apk`
- `Meltype-Android-0.2.0-arm64-v8a.apk.sha256`
- `Meltype-Android-0.2.0-source.tar.gz` とその `.sha256`
- `DEPENDENCIES.json` (依存関係の版・source revision・取得 URL)
- `NOTICE.txt` (同梱部品の著作権・ライセンス通知)

バージョン名はタグから設定し、Android の `versionCode` は `major * 1000000 + minor * 1000 + patch` で設定します（`0.2.0` → `2000`）。更新ではバージョンを上げます。minor / patch は 999 以下にします。
キーの不足・署名検証の失敗・設定した証明書との不一致・依存ソースや通知の取得失敗があれば、Release 公開は実行しません。

GPL の対応ソースの範囲と点検結果は [LICENSE-COMPLIANCE.md](LICENSE-COMPLIANCE.md) を参照してください。
同じ Release に APK と source archive を維持します。source archive 内の `Meltype/` から上のコマンドでビルドでき、
使用した Mozc・依存ライブラリのソースも `third-party/` に含みます。
私的な変更版は自分の keystore で署名してインストールできます。配布用の秘密鍵の公開は必要ありません。

通常の branch / PR / 手動ビルドは Artifacts に APK を保存します。4 つの署名 Secret が揃っていれば配布用キーを使い、Secret を利用できない fork PR ではテスト署名でビルドします。
Actions の手動実行では `version` を指定でき、空欄なら project の `ApplicationDisplayVersion` を使います。

## インストール後

1. Meltype for Android を開く
2. 「Meltype を有効にする」から入力方法を有効化
3. 「Meltype を選択する」から Meltype に切り替える
4. 任意のテキスト欄で入力する

実機で問題が起きた場合は、まず次で IME のクラッシュログを確認できます。

```bash
adb logcat | grep -i meltype
```
