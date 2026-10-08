# Android 配布物のライセンスと対応ソース

確認日: 2026-10-08 (UTC)。対象: この fork の Android APK・ソース・署名 Release workflow。

## プログラムの由来と変更

このリポジトリは [雪代 / Yukishiro 氏の Meltype](https://github.com/yksr-melt/Meltype) を元にした Android 移植です。
元の著作権表示と [GNU GPL v3 本文](../LICENSE) を保持し、プログラムは **GPL-3.0-or-later** で提供します。
Android の IME、ネイティブ Mozc bridge、入力欄の保護、署名・配布フローを追加・変更しています。
2026-10-08 に入力処理の修正、上流 main の取り込み、配布用通知と対応ソースの生成を追加しました。
元作者・貢献者の権利を fork の作者へ移転したものではありません。

個人・社内で使うこと、私的な変更には GPL による一般公開義務はありません。
他者へ APK や改造版を配布する場合は GPL の条件に従い、受領者が対応ソースを取得・変更・再配布できるようにします。
非 GPL の条件でこの fork 全体を配布できるという許諾は、このリポジトリでは提供していません。

## 確認で見つかった不足と対応

| 確認事項 | 対応 |
| --- | --- |
| ルート README が Windows 版と上流の配布先を案内していた | Android fork 用に更新し、上流の README は `docs/UPSTREAM-README.md` に保持 |
| Android APK に GPL 本文・著作権・第三者通知が入っていなかった | `Assets/licenses/` に同梱し、起動画面の「ライセンスとソース」からオフラインで閲覧可能にする |
| ネイティブ Mozc・辞書・依存ライブラリの通知が Android に同梱されていなかった | 固定した Mozc の LICENSE・credits、実際の Bazel 依存ソースの通知、NDK の通知を取り込む |
| Material Components / AndroidX / Material アイコンの通知が不足していた | NuGet の実際の解決結果から通知を取り込み、アイコンに出典・Apache-2.0 表示を追加 |
| APK の Release が対応ソースを案内しない構成だった | 同じ Release に対応ソース archive・依存 manifest・通知を添付し、本文から案内 |
| 既存 Mozc checkout が固定 commit と異なっても使われる可能性 | `MOZC_COMMIT` と一致しない場合は停止し、別のソースを配布物に混ぜない |

## ライセンスを分けて扱うデータ・部品

| 部品 | 条件・出典 |
| --- | --- |
| Meltype のコード、Android bridge と追加スクリプト | GPL-3.0-or-later。元作者の表示を保持 |
| Mozc エンジン | BSD-3-Clause。Google の表示・免責を保持 |
| Mozc の OSS 辞書 | Mozc の LICENSE と credits にある IPAdic / NAIST / ICOT・沖縄辞書・Japanese Usage Dictionary の条件を保持。辞書を一律に GPL へ変更しない |
| Abseil / Protocol Buffers など | 実際にビルドしたソースのライセンス・通知を保持 |
| .NET runtime、.NET Android、Android 用バインディング | MIT 等。各配布元の第三者通知も保持 |
| Material Components、AndroidX、Kotlin、Material Design Icons | Apache-2.0 等。Microsoft のバインディング部分は MIT |
| JMdict 由来の translations / readings / loanwords | EDRDG・James William Breen の出典を保持し、派生データは CC BY-SA 4.0 のまま |
| meanings | ウィクショナリー執筆者・kaikki.org の出典を保持し、CC BY-SA 4.0 のまま |
| SCOWL / Unicode CLDR | 元の通知と許諾を保持。詳しくは `THIRD-PARTY-NOTICES.md` |

Apache-2.0 は GPL v3 と組み合わせられますが、著作権・ライセンス・NOTICE の保持は引き続き必要です。
辞書など独立したデータは元の条件で提供し、GPL のプログラム本体と区別します。
この表だけで全文の通知を置き換えず、APK 内の通知と Release の `NOTICE.txt` を配布物に伴わせます。

## Release に添付する対応ソース

`Meltype-Android-<version>-source.tar.gz` には次を入れます。

- `Meltype/`: APK と同じ commit のリポジトリ。変更したコード・辞書・ビルド／署名／インストール手順を含む
- `third-party/mozc/`: 固定 Mozc commit のソースと、実際に適用した Android bridge / Bazel 定義
- `third-party/native/`: ビルドに用いた Abseil、Protocol Buffers、関連データなどのソース
- `third-party/managed/`: 解決済み NuGet の repository commit の source archive、元の Maven 座標に対応する Java / Kotlin source JAR、および取得 URL
- `distribution/licenses/`: GPL、CC BY-SA、Apache の本文、通知、解決済み依存関係の一覧

`DEPENDENCIES.json` に NuGet の版、ライセンス、repository commit、Maven 座標、source URL を記録します。
SDK が APK に足す .NET Android の runtime glue についても、インストールした SDK pack の NuGet metadata から source commit を記録・添付します。
汎用の .NET SDK、Android SDK / NDK、Bazel といったビルドツールは、説明に従って取得します。Android OS と未改変のコンパイラ標準ライブラリのソースは、この archive には複製していません。
追加で独自のネイティブ／Java ライブラリを組み込む場合は、通知・ソース収集の対象も更新してください。

製品版の秘密鍵は archive に含めません。利用者は自分の鍵でビルドした APK をインストールできます。
署名が異なる場合は配布版をアンインストールしてから入れるため、設定・学習データは削除されます。
ソースの取得失敗、通知や source revision の不足、未確認の NuGet ライセンスがある場合は workflow を失敗させ、Release を公開しません。
既に配布した APK の対応ソースは、Release 資産として利用者が取得できる状態を維持してください。

## 確認の限界

これはリポジトリとビルドで確認できるライセンス・通知・対応ソースの技術的な点検です。権利帰属についての法律上の保証ではありません。
上流辞書の個々の語の来歴や、過去にこの仕組みを使わず配布した APK までは検証していません。
依存パッケージの更新や別の配布先を使う場合は、その版・配布方法についても確認してください。

参照: [GPL v3 第1・4・5・6条](https://www.gnu.org/licenses/gpl-3.0.en.html)、
[Apache と GPL の互換性](https://apache.org/licenses/GPL-compatibility.html)、
[EDRDG の配布・表示条件](https://www.edrdg.org/edrdg/licence.html)。
