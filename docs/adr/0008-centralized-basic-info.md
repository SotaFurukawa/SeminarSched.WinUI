# ADR 0008: 生徒・講師・科目等の基本情報をプロジェクト横断で一括管理する

- Status: Accepted for v1.2.0
- Date: 2026-10-14

## 背景・ユーザーの要望

> プロジェクト設定としてではなく、生徒・講師・講師指導可能科目・通常授業担当設定などを基本情報と
> してプロジェクトごとの管理ではなく、一括での管理としたい。ホームと1プロジェクト設定の間に、
> 0基本情報設定を作っておき、これはプロジェクトを開いていなくても触れるようにする。
> また、逆方向の操作ができるようにしたい。お願いします。書き戻すときに、変更されたものは即座に
> excelに反映するようにしてください。

これに先立つやり取りで、ユーザーから「プロジェクト設定から講師指導可能科目を変更した場合、
Excelの方にも反映されますか？」という質問があり、回答は「反映されない」だった（共通名簿Excel
（`SharedRosterStore`）とプロジェクトのDBは別管理で、Excel→DBの一方向（`ApplyImportAsync`→
`CopyIntoProjectAsync`）のみが存在し、逆方向は無かった）。本ADRはこの質問を受けての機能追加。

## Decision

- **新しい左ナビ項目「0 基本情報設定」を、既存の`SetupPage`（左ナビ「1 プロジェクト設定」）と
  同じPageクラス・同じXAML・同じTabViewインスタンスを共有する形で追加した。** 生徒・講師・
  科目・講師指導可能科目・通常授業担当設定の5タブ（`SetupPage.xaml`のTabView内では元々
  絶対インデックス0,1,2,3,6）を「0 基本情報設定」側として扱い、残る3タブ（コマ・開校日・
  スケジュール設定・出力設定、絶対インデックス4,5,7）を引き続き「1 プロジェクト設定」側として
  扱う。TabViewItem自体は8個とも常に存在させ、`SetupPage.ApplyModeVisibility()`がモードに応じて
  `Visibility`だけを切り替える（TabViewの絶対インデックスがモードで変わらないようにするため）。
  新規にPageクラスを複製しなかった理由は、既存の`SetupPage`がstudent/teacher/subject/
  qualification/regularLessonの保存・削除・一覧更新ロジックを`ExecuteAsync(Func<string,Task>,
  string)`という単一の「対象パス解決→実行→再読込→通知」経路に既に集約していたため
  （`ResolvePath()`を新設してこの1箇所だけを「共通正本DBかプロジェクトDBか」で分岐させれば、
  既存の保存・削除ハンドラーは一切変更せずにそのまま両モードで動く）。
- **「0 基本情報設定」の対象データは、[ADR 0007](0007-auto-update-check.md)より前から存在する
  共通正本DB（`SharedRosterStore`が`%LOCALAPPDATA%\SeminarSched.WinUI\Workspace\SharedRoster\
  roster.db`に保持している、プロジェクトと同一スキーマのSQLiteファイル）をそのまま使う。**
  これは元々共通名簿Excelの読み書き専用のバックエンドとして存在していたが、スキーマが
  プロジェクトDBと完全に同一（`SqliteProjectRepository().CreateAsync`で作成するため）なので、
  既存の`IMasterDataRepository`（`GetStudentsAsync`/`SaveStudentAsync`等、パスを引数に取る）を
  このDBパスに対してそのまま呼び出すだけで、プロジェクトを一切介さずに直接編集できた。
  `ISharedRosterStore`に`DatabasePath`を新設して、このパスをUI層へ公開した。
- **プロジェクトを開いていなくても使えるようにする**ため、`SetupPage.ActivateModeAsync()`は
  「0 基本情報設定」モードのときだけ`EnsureProject`（プロジェクト必須チェック）を迂回する。
  「1 プロジェクト設定」モードは従来どおりプロジェクトが必須。
- **逆方向の反映（DB→Excel）**: `ISharedRosterStore`に`RefreshWorkbookAsync`を新設した。
  既存の`EnsureWorkbookAsync`は「無ければ作る、あれば触らない」（Excel側の編集中の内容を
  保護するため）だったが、`RefreshWorkbookAsync`は常に共通正本DBの現在の内容から共通名簿Excel
  を無条件に再生成する。「0 基本情報設定」での保存・削除が成功するたびに、`SetupPage`が
  `RefreshSharedRosterWorkbookIfBasicInfoAsync()`経由でこれを呼ぶ（「1 プロジェクト設定」側の
  保存では呼ばない）。Excelが開いていて上書きできない場合は`IOException`/
  `UnauthorizedAccessException`になるが、この場合もDBへの保存自体は既に成功しているため、
  「保存しました」の成功通知とは別に「共通名簿Excelへの反映に失敗しました（閉じてから保存し
  直してください）」という警告だけを追加で出す（保存の成功・失敗とExcelミラーの成功・失敗を
  混同しないため）。
- **「プロジェクトごとの管理」から「一括管理」への切替は、既存のプロジェクトDBのテーブル自体を
  削除・変更するものではない。** プロジェクトは引き続き自分自身のStudent/Teacher/Subject/
  TeacherQualification/RegularLessonProfileテーブルを持ち、時間割自動作成（最適化）はそこから
  読む（季節講習の実行中に共通正本側の変更でデータが動いてしまうことを避けるため、プロジェクト
  側は「その時点のスナップショット」を保持し続ける設計を維持した）。共通正本の最新内容を
  プロジェクトへ反映する手段は、既存の「共通名簿を取込む」（ホーム画面、`CopyIntoProjectAsync`）
  のままで変更していない。

## 影響範囲

- `SeminarSched.Application.MasterData.ISharedRosterStore`: `DatabasePath`・
  `RefreshWorkbookAsync`を追加。
- `SeminarSched.Infrastructure.MasterData.SharedRosterStore`: 上記2つを実装。
- `SeminarSched.WinUI`:
  - `SetupPageNavState`: `IsBasicInfoMode`・`RequestedBasicInfoMode`を追加。
  - `SetupPage.xaml`/`.xaml.cs`: 8タブすべてに`x:Name`を付与し、`ApplyModeVisibility`・
    `ActivateModeAsync`・`ResolvePath`・`ActivateMode`・
    `RefreshSharedRosterWorkbookIfBasicInfoAsync`を追加。既存の`SelectTab(int)`は
    `ActivateMode(bool, int?)`へ統合して置き換えた。
  - `MainWindow.xaml`: 左ナビへ「0 基本情報設定」（子項目0.1〜0.5）を新設し、「1 プロジェクト
    設定」の子項目を1.1〜1.3（コマ・開校日・スケジュール設定・出力設定）へ縮小した。
  - `MainWindow.xaml.cs`: ナビゲーション項目タグの振り分け（`setup`/`basicInfo`/
    `setupTab:N`/`basicInfoTab:N`）と、左ナビの展開・選択状態の同期（`RefreshSetupNavState`）を
    モード対応させた。
  - `OptimizationPage.xaml.cs`: 未配置理由の案内文言を、移動後の「0 基本情報設定」を指すよう
    修正した。

## 検証について

既存の全テスト（計267件、うち新規1件：`SharedRosterStoreTests.
RefreshWorkbookAsync_OverwritesExistingWorkbookWithCurrentDatabaseContent`）がすべて通過する
ことを確認済み。新規テストは「共通正本DBを直接編集→`EnsureWorkbookAsync`では反映されない→
`RefreshWorkbookAsync`で反映される」という、本機能の中核となるデータ層の挙動を検証している。

**未検証:** 「0 基本情報設定」画面そのもの（ナビゲーション・タブ切替・保存後のExcel即時反映）の
実機UI確認は、本バージョン作成時点では行えていない。この開発環境のスクリーンショット手段が
意図しないセッションの画面を捉えてしまう問題が別件（Excelチェックボックス調査時）で発生して
おり、同じ問題が再発するリスクを避けるため、今回はコードレビューとビルド・自動テストの通過のみ
で完了扱いとした。次回この環境でスクリーンショットが正しく機能することを確認できた際は、
実機でのUI確認を優先的に行うこと。
