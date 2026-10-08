# Mozc の変換ヘルパー

## Android 版

Android は `build-android.sh` と `android/` の C ABI bridge を使い、`libmeltype_mozc.so` と OSS `mozc.data` を APK に組み込みます。
`MOZC_COMMIT` を固定し、その LICENSE / credits と依存部品の通知・ソースも配布します。
手順は [android/README.md](../../android/README.md)、配布条件は [android/LICENSE-COMPLIANCE.md](../../android/LICENSE-COMPLIANCE.md) を参照してください。
以下はデスクトップ版の外部プロセスによる変換の説明です。

Meltype の変換エンジン「Mozc」(設定の「変換エンジン」が 両方 / Mozc のとき) は、[Mozc](https://github.com/google/mozc)
(Google 日本語入力のオープンソース版、BSD-3-Clause) の変換エンジンを使う小さなプログラム `meltype_mozc_helper.exe` を別のプロセスで動かして使います。

| ファイル | 中身 |
| --- | --- |
| `meltype_mozc_helper.cc` | ヘルパーのソース。標準入出力で 変換 (`C`)・学習 (`L`)・保存 (`S`) を受け付ける (やり取りの形式はファイルの先頭に説明) |
| `BUILD.fragment` | Mozc の `src/converter/BUILD.bazel` に足す Bazel の定義 |
| `MOZC_COMMIT` | ビルドする Mozc の commit (動作を確かめた版に固定) |
| `Build-MozcHelper.ps1` | Mozc を取得してヘルパーをビルドし、`bin\` にライセンスと一緒に置く |
| `bin\` | ビルドしたヘルパー (Git には入れない)。`Build-Package.ps1` / `Install-Meltype.ps1` が `app\mozc\` に同梱する |

## ビルド

必要なもの: Visual Studio 2022 (C++ によるデスクトップ開発、Windows 11 SDK、C++ ATL)、Python 3.12 以降、[Bazelisk](https://github.com/bazelbuild/bazelisk)、Git。

```powershell
.\native\mozc\Build-MozcHelper.ps1 -Python python -Bazelisk C:\tools\bazelisk.exe
```

初回は Mozc の依存関係 (LLVM など約 1 GB) の取得とビルドで 30〜40 分ほどかかります。
GitHub Actions では `.github/workflows/build.yml` の `mozc` ジョブが同じスクリプトでビルドし、`package` ジョブが同梱します。

学習データ (Mozc が覚えた変換) は `%LOCALAPPDATA%\Meltype\mozc\` に保存されます。
