# ADR 0007: 自動アップデート確認（GitHub Releases API）

- Status: Accepted for v0.16.0
- Date: 2026-10-02

## 背景・ユーザーの要望

> 新規でバージョンが出てきたときのために自動アップデート機能を追加しておきたい。これは
> github以外で別でサーバーを立てておく必要があるのでしょうか。もしそうでないなら作ってほしい。
> 週に1度、アップデートがないかのチェックを行い、もしあるならアップデートをするかの警告を
> 出すようにする。また、設定の画面にもアップデートがあるかのチェックを行えるようにしておき、
> アップデートの確認ボタンが押されたら、最新バージョンか、アップデートされていないものが
> あるかどうかを書くようにする形です。

「別サーバーが必要か」という問いに対しては、**不要**と回答した。理由は以下のDecisionのとおり、
本リポジトリ（`SeminarSched.WinUI`）が[ADR 0006](0006-product-key-licensing.md)の経緯で
publicへ切り替え済みのため、GitHub Releases APIの読み取り専用エンドポイントを無認証で
直接利用できるため。

## Decision

- **サーバーは立てない。** `GET https://api.github.com/repos/SotaFurukawa/SeminarSched.WinUI/releases/latest`
  を無認証で呼び出す。このエンドポイントは**公開済み（Draft/Prereleaseを除く）の最新release**
  のみを返す。本リポジトリの開発中の運用（AGENTS.md「ユーザーの明示指示なしにReleaseを公開
  しない。開発中は必ずDraftとする」）では公開済みreleaseが存在しない期間が続くため、その間は
  常に`404 Not Found`が返る。これを「最新バージョンです」と同義に扱う
  （`GitHubUpdateCheckService`がこの404を正常系として`UpdateCheckResult.UpToDate()`へ変換する）。
  実機確認（2026-10-02時点）でも実際に404が返ることを確認済み。
- **実装の境界:** HTTP通信そのもの（`GitHubUpdateCheckService`、`SeminarSched.Infrastructure`）と、
  レスポンスJSONの解釈・バージョン比較（`GitHubReleaseParser.Parse`、純粋関数）を分離した。
  後者は実際のネットワーク通信なしに`tests/SeminarSched.Infrastructure.Tests/Updates/
  GitHubReleaseParserTests.cs`で検証できる（新しいtag・同じ/古いtag・html_url欠落時の
  フォールバックURL・tag_name欠落/空・不正なtag形式の5パターン、計7テスト）。
  バージョン比較自体は`ApplicationVersion.IsNewerThan`（Major.Minor.Patchのみで比較し、
  Prerelease文字列は順序に関与させない）に実装し、`ApplicationVersionTests`で検証した。
- **週次の自動チェック:** `MainWindow`の起動時チェック（プロダクトキー認証後）で、
  `AppSettings.LastUpdateCheckUtc`が未設定、または7日以上前なら`CheckForUpdateAsync`を呼ぶ。
  **問い合わせが成功した場合のみ`LastUpdateCheckUtc`を更新する**（失敗時は次回起動時に再試行
  させるため、タイムスタンプは据え置く）。アップデートが見つかった場合のみ`ContentDialog`
  （「ダウンロードページを開く」/「後で」）を表示する。見つからない場合は何も表示しない
  （ユーザーの要望どおり、警告は「ある場合のみ」）。
- **設定画面の手動確認ボタン:** `SettingsPage`に「アップデートを確認」ボタンを追加した。
  押すと即座に問い合わせ、「最新バージョンです（vX.Y.Z）」または「新しいバージョン
  （vX.Y.Z）が利用可能です」＋「ダウンロードページを開く」ボタンを表示する。通信失敗時は
  エラーメッセージをそのまま表示する。
- **実機確認（2026-10-02）:** `dotnet run`起動で、(1) 起動直後の自動チェックが実際に
  `LastUpdateCheckUtc`を更新し設定画面に「前回確認: 2026-10-02 14:36」と表示されること、
  (2) 手動の「アップデートを確認」ボタンが実際にGitHub APIへ問い合わせ、「最新バージョンです
  （v0.15.0 (beta)）。」と表示されること、を確認した。「アップデートが見つかった場合」の
  UI（起動時ダイアログ・設定画面のダウンロードボタン）は、本リポジトリに公開済みreleaseが
  まだ1件も無いため実機では確認できず、`GitHubReleaseParserTests`によるユニットテストのみで
  検証している。

## Deferred

- 実際に公開releaseを1件作成した後の、「アップデートが見つかった場合」のUIの実機確認
  （次に正式な公開リリースを行う際に確認する）。
- レート制限対策（無認証のGitHub APIは60回/時/IPの制限があるが、週1回＋手動確認程度の
  頻度では問題にならない想定。必要になれば見直す）。

## Amendment（v1.1.0、2026-10-14）: アプリ内からの自動ダウンロード・自動インストール

当初Deferredとしていた「アプリ内からの自動ダウンロード・自動インストール」を実装した。
ユーザー指示：「今現状ではアップデートがあった場合、ダウンロードページへ誘導する形になりますが、
自動的にアップデートを行うようにしたい。ダウンロードページを開くのボタンを作るのではなく、
アップデートを行うのボタンを作る感じで、それが押されたら自動的にアップデート。」

- **`GitHubReleaseParser.Parse`** が、GitHub Releases APIレスポンスの`assets[]`から
  拡張子`.exe`の資産（Setup.exe）を探し、その`browser_download_url`を
  `UpdateCheckResult.InstallerDownloadUrl`として返すようにした（ファイル名の命名規則を
  決め打ちにせず、拡張子だけで探すことで将来の命名変更に耐える）。
- **新しい`IUpdateInstaller`**（`SeminarSched.Application.Updates`）を追加し、
  `GitHubUpdateInstaller`（`SeminarSched.Infrastructure.Updates`）で実装した。
  `DownloadAsync`が`InstallerDownloadUrl`を一時フォルダーへストリーミングダウンロードし
  （`IProgress<double>`で進捗を通知）、`Launch`がダウンロードしたSetup.exeを
  `UseShellExecute=true`で起動する。Setup.exeは`PrivilegesRequired=admin`
  （[ADR 0005](0005-windows-distribution.md)のAmendment参照）のため、起動時に
  WindowsがUACの昇格プロンプトを自動的に表示する。その先の実際のインストール
  （証明書信頼・このアプリの強制終了・Add-AppxPackage）はSetup.exe自身
  （`installer/Install-Package.ps1`の`-ForceApplicationShutdown`）が行うため、
  本クラスはダウンロードと起動だけを担う。
- **UI側**: `SettingsPage`の「ダウンロードページを開く」ボタンを「アップデートを行う」へ
  置き換え、押すとダウンロード進捗（%）→起動中→起動完了のメッセージへ本文が切り替わる。
  `MainWindow`の週次自動チェックダイアログも同様に、ダイアログを閉じずに
  （`ContentDialogButtonClickDeferral`）ダウンロード・起動まで行うよう変更した。
  `InstallerDownloadUrl`が取得できなかった場合（releaseにexe資産が無い等）のみ、
  従来どおりリリースページをブラウザで開く動作へフォールバックする。
- **検証**: `GitHubReleaseParser`の新しいテスト（assetsからexeを見つける／assetsが無い／
  exeが無い、の3パターン）で、URL抽出ロジックを確認した。`GitHubUpdateInstaller`自体は
  実際のHTTP通信・プロセス起動を伴うため、`GitHubUpdateCheckService`と同様に直接の
  ユニットテストは設けていない（本リポジトリの一貫した方針）。実機での
  ダウンロード→UAC昇格→インストール完了までの一連の流れは、次の正式公開リリースの際に
  実際にアップデート通知を受けて確認する（現時点では未検証）。
