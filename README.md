# Meltype for Android

ローマ字のまま日本語と英語を混ぜて入力できる、Android 用の日本語キーボードです。
[雪代 / Yukishiro 氏の Meltype](https://github.com/yksr-melt/Meltype) を元に、Android の入力方法として移植しています。

**開発中のプレビュー版です。Android 8.0 以降・arm64-v8a の端末に対応します。**
実機での入力・表示・安定性の確認は引き続き必要です。32bit ARM と x86_64 エミュレーター用の APK は提供していません。

## インストール

1. このリポジトリの [Releases](https://github.com/sakusdev/Meltype-android/releases) で、`android-v` から始まる版の APK を取得します。開発用署名の版は **Pre-release** と表示され、ファイル名に `-dev-signed` が付きます。
2. ダウンロードに使ったブラウザー／ファイルアプリに、Android の設定で APK のインストールを許可してインストールします。
3. Meltype for Android を開き、「入力方法の設定を開く」で Meltype を有効にします。
4. アプリへ戻り、「Meltype に切り替える」でキーボードを選びます。

Android のキーボードに関する確認画面は、入力方法を有効にする際の OS の案内です。
本アプリには、入力内容をネットワークへ送信する機能はありません。

同じ配布用キーで署名され、versionCode が上がった APK なら更新できます。
開発用署名では CI runner ごとにキーが生成されるため、別ビルドや配布用キーへの切り替え時は旧版のアンインストールが必要になる場合があります。その際、端末内の設定・学習データは削除されます。
APK の SHA-256 は同じ Release の `.apk.sha256` と比較できます。

## 入力する

- **かな / ABC** で日本語の自動判定と直接入力を切り替えます。
- ローマ字入力中、**Space** で漢字変換し、候補バーで候補を選びます。
- **Enter / Search / Send / Next / Done** は、変換中なら確定し、未変換なら入力欄のアクションを実行します。
- 変換中の **← / →** は文節を選びます。Space の左右スワイプは、文字を確定してから入力欄のカーソルを動かします。
- Backspace は選択範囲、絵文字、結合文字を考慮して削除します。長押しで連続削除します。
- 絵文字・クリップボード貼り付け、Shift、長押し数字入力に対応します。

変換は端末内の `Meltype.Core` と OSS Mozc で行います。Mozc が初期化できない場合は、簡易変換へフォールバックします。
PC 版のトレイメニュー・Windows IME 切替・自動更新などを、そのまま Android で提供するものではありません。

## プライバシー

設定・学習データはアプリの領域に保存します。
パスワード欄は直接入力にし、候補・キーの拡大プレビュー・学習を停止します。
入力先が `IME_FLAG_NO_PERSONALIZED_LEARNING` を指定した場合も、Meltype / Mozc の学習保存を停止し、日本語変換は利用できます。
ログを報告する場合は、入力内容や個人情報が含まれていないか確認してください。詳しくは [SECURITY.md](SECURITY.md)。

## ビルドと署名 Release

ビルド、開発用署名での公開、配布用の署名設定は [android/README.md](android/README.md) にあります。
`android-v0.2.0` のようなタグを push すると、GitHub Actions が署名を検証し、APK・チェックサム・対応ソース・通知を同じ Release に添付します。
署名 Secrets が未登録なら開発用署名で Pre-release を公開します。4つすべて登録すると配布用キーを使い、その証明書も検証します。一部だけ登録した場合は設定エラーとして停止します。
通常の branch / PR ビルドの APK は [Actions](https://github.com/sakusdev/Meltype-android/actions/workflows/android.yml) の Artifacts に保存されます。

共通ロジックと Android の入力方針のテストは Android SDK なしでも実行できます。

```bash
dotnet run --project src/Meltype.Core.Tests/Meltype.Core.Tests.csproj -c Release
python3 -m unittest discover -s android/tests -v
```

## ライセンスと出典

プログラムは **GNU GPL version 3 or later (GPL-3.0-or-later)** です。元作者の著作権表示を保持しています。
個人・社内で使え、変更・再配布も GPL の条件に従って行えます。私的な変更に一般公開の義務はありません。
配布する APK には GPL 本文・著作権・第三者通知を同梱し、起動画面の「ライセンスとソース」から閲覧できます。

- [LICENSE](LICENSE): GPL v3 本文
- [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md): Mozc・辞書・ランタイム・UI 部品の出典と個別の条件
- [android/LICENSE-COMPLIANCE.md](android/LICENSE-COMPLIANCE.md): 確認内容と対応ソースの配布方法

Mozc、Material Design Icons、JMdict などの第三者コード・データには、それぞれのライセンスが引き続き適用されます。
Google 日本語入力の非公開辞書を使用していません。
Android 移植と入力処理・署名・ライセンス配布の変更を追加しています (2026-10-08 UTC)。

## 不具合の報告と開発

Android 版の報告は [この fork の Issues](https://github.com/sakusdev/Meltype-android/issues) へ、APK の版、Android の版、端末、入力先アプリ、入力手順と期待した結果を記載してください。
コード・文書の貢献は [CONTRIBUTING.md](CONTRIBUTING.md)、構成は [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) を参照してください。

Windows / Mac / Linux 版の案内と協力者一覧は [上流 README](docs/UPSTREAM-README.md) に保持しています。
デスクトップ版の公式配布先は [元の Meltype リポジトリ](https://github.com/yksr-melt/Meltype) です。
