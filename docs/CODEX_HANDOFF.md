# SeminarSched Codex引き継ぎ書

最終更新: 2026-09-17
Python参照版: v1.9.5 / commit `1d323a4`
Pythonリポジトリ: `https://github.com/SotaFurukawa/SeminarSched`

## 0. WinUI版の現在地点

Current Version: `v0.1.0 (beta)`（実装中・未Release）
Latest Development Checkpoint: checkpoint 35（v0.1.0, ①生徒/講師/科目一覧に検索box追加。コミットhashは本checkpoint末尾を参照）
Latest Draft Release: `v0.0.0`（GitHub上にDraftとして作成済み）
Tooling note: 本プロジェクトはCodex CLIからClaude Code CLIへ運用を切り替えた（2026-09-17）。バージョン管理・push・Draft Releaseの運用ルールは変更なし。Claudeが行ったcheckpointは見出しに明記する。
Next Version Rule:

- v0.1.0開発中の追加・修正 -> 同一作業単位として`v0.1.0`へ集約
- v0.1.0 Draft Release後のbug fix / minor change -> `v0.1.1`
- v0.1.0 Draft Release後のnew feature -> `v0.2.0`
- `v1.0.0` -> ユーザーの明示指示がある場合のみ

### 実装済み

- 独立した`SeminarSched.WinUI.sln`と独立`.git`
- .NET 10.0.401 / Windows App SDK 2.4.0 / packaged WinUI 3 shell
- Home、About、Settingsの初期NavigationView
- `Domain`、`Application`、`Infrastructure`、`Optimization`、`Reporting`のproject境界
- `Directory.Build.props`を正本とするversion一元管理
- About画面のversion正本連動表示
- Windows CI、xUnit、repository privacy gate
- `docs/FEATURE_PARITY.md`、ADR 0001、Privacy、Security、暫定license

### 検証済み

- `dotnet build SeminarSched.WinUI.sln --configuration Release -p:Platform=x64`: warning 0 / error 0
- `dotnet test ... --no-build -p:Platform=x64`: 63 tests passed
- `scripts/Test-RepositoryPrivacy.ps1`: passed
- Python参照repoはcommit `1d323a4`のまま。WinUI作業による変更なし

### Git / GitHub

- GitHub CLI 2.101.0: `C:\Users\sota1\.local\gh\bin\gh.exe`
- Repository: `https://github.com/SotaFurukawa/SeminarSched.WinUI`（private）
- `main`と`v0.0.0`はpush済み。
- `v0.0.0 (beta)` Draft Release作成済み。公開していない。
- 直近push `e43fd9e`のCI run `35132344985`は成功。今後もpush直後にrun受付と即時失敗だけ確認し、長時間監視は行わない。

### 未完了・blocker

- `dotnet run`による起動を試行したが、端末のWindows Developer Modeが無効なためpackaged app登録前に停止した。buildとXAML compileは成功済み。
- ReadyToRunとtrimは、RID別配布profileとWinRT trim検証を設計するまで無効化している。

### 次に行うこと

1. Phase W1を`v0.1.0`として開始し、業務flowのNavigationViewとproject lifecycleの最小縦sliceを実装する。
2. `.jukuschedule`互換、SQLite migration、配布形式、最終licenseは個別ADRを先に作成する。
3. UI実機確認が必要になった時点でWindows Developer Modeを有効化する。

### v0.1.0実装方針（2026-09-17開始）

- ユーザー指示により、Python版v1.9.5の全利用者向け機能と最適化品質スライダーを`v0.1.0`へまとめる。
- 機能単位でbuild/test/commit/pushするが、`v0.1.0` Draft Releaseは全対象の完了後に作成する。
- 問題箇所は保留理由と再開条件を本書・Feature Parityへ記録し、独立して進められる実装を継続する。
- 最適化の探索戦略、validator、transaction保存はUIから分離し、品質レベルは設定profileとして管理する。

### v0.1.0 checkpoint 1

- version正本を`0.1.0-beta`へ更新。MSIX/app manifestも`0.1.0.0`へ同期。
- ADR 0002で5段階品質profile、トーナメント探索、共通辞書式評価、best採用/cancel分離を決定。
- `OptimizationProfileCatalog`へLevel 1〜5の時間予算、停滞終了、stage、strategy構成を集約。
- `ScheduleEvaluation`はhard violation、未配置、重要希望違反、主要penalty、講師空き、分散、その他penalty、objectiveの順で比較する。
- Release/x64 build: warning 0 / error 0。全15 tests passed。

### v0.1.0 checkpoint 2

- 時間割自動作成ページへ1〜5でsnapするWinUI Sliderと、名称・推定時間・strategy数・説明のリアルタイム表示を追加。
- 初回はLevel 3。最後に選んだlevelを`%LOCALAPPDATA%\SeminarSched.WinUI\settings.json`へ原子的に保存・復元する。
- settings書込は一時file成功後のreplaceで、slider連続操作は250ms debounceする。
- Release/x64 build: warning 0 / error 0。全18 tests passed。

### v0.1.0 checkpoint 3

- Home画面へ年度・講習区分・期間を指定する新規project作成、既存projectのopen、closeを追加。
- WinUI schema v1としてmetadata、project定義、全開講日をSQLiteへ保存するproject repositoryを追加。
- 新規作成は一時DBでschema作成・整合性検証を完了してから、上書きなしで目的地へ移動する。
- open時はSQLite integrity、product marker、schema version、project定義と開講日の整合性を検証する。
- Python版`.jukuschedule`はその場でmigration・上書きせず拒否する。copy-first import設計が完成するまで保留。
- Release/x64 build: warning 0 / error 0。全22 tests passed。

### v0.1.0 checkpoint 4

- SQLite backup APIを使用し、WAL状態でも一貫したproject snapshotを作成するrepository APIを追加。
- backupは一時fileへ作成し、integrityとWinUI schemaを検証後に上書きなしで配置する。
- restoreは置換用DBを先に検証し、`File.Replace`とrollback copyで原子的に差し替える。置換後検証失敗時は元DBへ戻す。
- Release/x64 build: warning 0 / error 0。全24 tests passed。
- Home画面から保存先folderを選ぶbackup作成と、確認dialog付きrestoreを実行できる。

### v0.1.0 checkpoint 5

- 最近使ったprojectを最大10件、最終open順でsettingsへ保存し、Homeから再openできる。
- 同じpathは重複させず先頭へ移動し、移動・削除済みfileを選んだ場合は履歴から除去する。
- 品質level保存時にrecent project設定を失わないmerge保存へ修正。
- Release/x64 build: warning 0 / error 0。全26 tests passed。

### v0.1.0 checkpoint 6

- restore直前の現行projectを自動snapshotし、`*_before_restore_*`として新しい3世代を保持する。
- 自動snapshotにもSQLite backup API、integrity検証、WinUI schema検証を適用する。
- Release/x64 build: warning 0 / error 0。全26 tests passed。

### v0.1.0 checkpoint 7

- profileのstageと時間予算に従ってstrategyを実行するgeneric `ScheduleOptimizer`を追加。
- 全候補を共通辞書式Evaluatorで比較し、stageごとに上位候補だけを次段hintへ進める。
- 最大時間、strategy別時間、停滞早期終了、progress、通常cancel、現在best採用を分離した。
- 実CP-SATの複数strategyは未実装。ユーザー方針によりPython v1.9.5 parityを先に進め、後段へ保留する。
- Release/x64 build: warning 0 / error 0。全29 tests passed。

### v0.1.0 checkpoint 8

- Python v1.9.5の定義に合わせてStudent、Teacher、Subject、TeacherQualification、RegularLessonProfileを追加。
- external ID/code一意性、blank禁止、連続上限正数、科目順正数、担当優先度1〜5、外部キーをSQLiteでも強制する。
- master data CRUDと指導可否・通常担当upsertを追加。旧v0.1.0開発DBには不足tableだけを加算作成する。
- 管理UIは次checkpoint。実在データを使わず架空fixtureのみで検証。
- Release/x64 build: warning 0 / error 0。全34 tests passed。

### v0.1.0 checkpoint 9

- Python版の正本どおり、NavigationViewを①設定、②アンケート作成、③アンケート取込み、④事前確定、⑤時間割、⑥出力へ変更。
- workflow page共通のproject未open guardを追加。未接続機能はボタンを有効化せず「実装準備中」と明示する。
- ①設定へproject概要、生徒・講師・科目の追加・一覧UIを接続。入力はdomain validationとSQLite制約を必ず通る。
- ①のコマ・開校日詳細編集、②〜④、⑥の実処理は未実装であり、完成扱いにしない。
- Release/x64 build: warning 0 / error 0。全34 tests passed。privacy gate passed。

### v0.1.0 checkpoint 10

- ①設定へコマ追加、表示順、開始・終了時刻と、期間内日付の開校/休校・日別有効コマ設定を追加。
- schema準備前にWinUI product markerを検証し、追加table作成をtransaction内で実行するよう精査修正。
- 休校日に変更すると日別コマ対応を同一transactionで削除する。
- Release/x64 build: warning 0 / error 0。全39 tests passed。
- Release境界を更新: Python v1.9.5 parity完成=`v0.1.0` Draft、追加の複数strategy最適化完成=`v0.2.0` Draft。

### v0.1.0 checkpoint 11

- ②アンケート作成で、①の開校日・有効コマ・科目を埋め込んだGoogle Apps Script kitを生成可能。
- 出力は新規一時folderへCode.gs/READMEを書き、全成功後だけ最終folder名へ移動する。同名上書きなし。
- 現段階のフォームは生徒ID・科目・参加可能コマ、講師ID・勤務可能コマの基本版。Python版の学年別分岐等は未完。
- Release/x64 build: warning 0 / error 0。全40 tests passed。

### v0.1.0 checkpoint 12

- ③で生徒・講師のUTF-8 CSVを2つ選択し、全行のmaster参照、必要回数、日付・コマをpreview検証可能。
- エラー0件の場合だけ反映を有効化し、LessonRequestと講師勤務不可を単一SQLite transactionで保存する。
- preview時の両file SHA-256を保持し、反映直前に再hashして差替えを拒否する。
- XLSX、CP932、Google Forms生CSVの列mapping、生徒availability、AuditLogは未実装。
- Release/x64 build: warning 0 / error 0。全41 tests passed。

### v0.1.0 checkpoint 13

- ④で受講希望・講師・開校日コマを選び、固定Assignmentを追加・解除可能。
- 保存前に開校コマ、講師の明示的な指導可、同時刻の同一生徒・同一講師重複をtransaction内で検証する。
- ⑤の自動作成は`IsLocked=1`を固定入力として扱う設計境界になった。
- Release/x64 build: warning 0 / error 0。全43 tests passed。

### v0.1.0 checkpoint 14

- Google.OrTools 9.15.6755を固定し、ADR 0003へsolver/validator/transaction境界を記録。
- SQLite正本から候補を構築し、単一CP-SATで要求回数最大化、生徒・講師同時刻衝突回避、固定授業保持を実装。
- solver結果は純粋validator後、DB正本に対して開校コマ・指導可・勤務不可・衝突・回数超過を再検証し、未固定Assignmentだけをatomic置換する。
- ⑤UIから非同期実行し、配置数・未配置数・時間を表示する。複数strategyはv0.2.0。
- Release/x64 build: warning 0 / error 0。全46 tests passed。

### v0.1.0 checkpoint 15

- ClosedXML 0.105.1とPDFsharp-MigraDoc 6.2.4をMIT license確認のうえ固定し、ADR 0004へ記録。
- ⑥でDB integrity再確認後、全体時間割と未配置一覧を共通snapshotからExcel/PDFへ生成する。
- 一時folderですべて成功した場合だけ最終folderへ移動し、既存出力を上書きしない。
- Windows日本語TTF resolverを追加し、実PDF signatureとXLSXサイズをtest。Python版の5帳票すべては未完。
- Release/x64 build: warning 0 / error 0。全47 tests passed。

### v0.1.0 checkpoint 16

- 共通帳票snapshotへ学年を追加し、Excelへ生徒別sheetと講師別sheetを生成。
- PDFへ生徒別配布sectionと講師別配布sectionを改ページ生成。生徒順は学年・氏名、講師順は氏名。
- Python版の週calendar、欠席一覧、講師別folder packetは未完。
- Release/x64 build: warning 0 / error 0。帳票実ファイルtest passed。

### v0.1.0 checkpoint 17

- Homeから現在のprojectを別名複製し、検証済みの複製先へ作業対象を切り替えられる。
- 複製はExplorerの通常copyではなくSQLite backup APIを使い、同名ファイルは上書きしない。
- 元projectが不変であること、既存の複製先を保護することをapplication testで固定。
- Release/x64 build: warning 0 / error 0。49 tests passed。

### v0.1.0 checkpoint 18

- CI 19〜22の帳票test失敗を調査。Windows runnerにHG系日本語TTFがなく、従来resolverがMigraDoc内部のCourier NewまでTTCへ誤mappingしていたことが原因。
- 帳票用日本語face、sans、monoを分離し、最小CI imageでもTrueType fontに解決できるfallbackを追加。配布用日本語fontの同梱は別途ライセンス確認後に行う。
- project schemaをv2へ更新し、Import、Validation、AuditLog、OptimizationRun、OutputSettingと可用性の共通tableを一元管理。個別serviceの無検証DDLを廃止。
- v1を開く際はSQLite backup APIで`*_before_migration_v1_*`を作成してからtransaction migrationし、失敗時はbackupから復元する。
- Release/x64 build: warning 0 / error 0。50 tests passed。Privacy gate passed。

### v0.1.0 checkpoint 19

- ③取込みでCSVのBOM付きUTF-8・UTF-8・CP932自動判定とXLSX先頭sheet読み込みを追加。
- 従来の必要回数/勤務不可形式に加え、生徒・講師の日付×コマの可用性level 0/1/2、生徒の第1〜3希望講師を全行検証・反映。
- 日付・コマ検証を`OpenDateTimeSlot`まで含むよう修正し、日別に無効なコマを拒否。
- 取込原本のBLOB/SHA-256 snapshot、ImportBatch、個人情報をメッセージに含めないAuditLogを可用性反映と同一transactionで保存。
- CP932講師CSVとXLSX生徒回答の混在取込みtestで可用性・希望講師・snapshot・監査履歴を検証。

### v0.1.0 checkpoint 20

- CP-SATへ生徒/講師availability、講師同時2名、1対1授業の2枠消費、通常担当優先度1〜5の最低担当率、最大連続コマ、空き時間禁止を追加。
- 全開講コマと固定配置をsolver inputへ明示し、固定授業を含む講師容量・生徒連続コマ・空き時間を制約化。
- solverとは独立したvalidatorで候補外配置、回数超過、生徒衝突、講師容量、通常担当最低数、最大連続、空き時間を再検証。
- 事前確定授業にも資格・可用性・必要回数・同時2名/1対1規則を適用し、SessionIndexを保存。
- OptimizationRunへ時間制限、input/result概要、未配置数、経過時間を保存し、未固定配置と同一transactionで確定。
- commit `c7848c0`。Release/x64 build: warning 0 / error 0。全59 tests passed。Privacy gate passed。

### v0.1.0 checkpoint 21

- ①設定へ共通基本情報Excelの出力・検証preview・確認後取込みUIを追加。
- Python版と同じ生徒・講師・科目・講師対応科目・受講希望の5シートを扱い、例示行、必須列、空欄既定値、型/範囲、重複、参照、通常担当資格を検証。
- preview後の原本SHA-256を再確認し、全masterを単一transactionでupsert。途中DB失敗時は全変更をrollback。
- ImportBatch、原本BLOB/SHA-256 snapshot、個人情報を含めないAuditLogを同一transactionで保存。
- commit `3453c05`。Release/x64 build: warning 0 / error 0。全63 tests passed。Privacy gate passed。

### v0.1.0 checkpoint 22

- ①設定で生徒・講師・科目・コマを一覧から選択し、全項目の更新と使用停止/再有効化を行えるようにした。
- 生徒の最大連続コマ/空きコマ、講師の空きコマ、科目略称/順序、コマ時刻/順序も画面編集に対応。
- 担当設定tabを追加し、講師対応科目の指導可否と、通常授業の担当講師・優先度1〜5・1対1必須を保存・一覧確認可能にした。
- repositoryの更新/使用停止、資格・通常授業のread-backを自動テストで確認。
- commit `b79ba4b`。Release/x64 build: warning 0 / error 0。全63 tests passed。Privacy gate passed。

### v0.1.0 checkpoint 23 (Claude)

- ここからCodex CLIに代わりClaude Code CLIが実装・build/test・commit/push/Draft Release・本書更新を担当する（運用ルールは既存のまま踏襲）。
- Codexが使用量上限で中断した未commit分（`IsManual`永続化）を引き継ぎ、完成・検証まで実施。
- ⑤時間割自動作成画面へ「時間割の確認・手動配置」カードを追加。手動配置の追加・削除・ロック切替・自動配置だけリセットをUIから実行可能にした。
- `SqliteFixedLessonService.AddManualAsync`/`SqliteScheduleEditorService`を追加し、手動配置は`IsManual=1`として保存、監査ログ(`manual_assignment_added`等)を記録する。
- `SqliteScheduleRunService`の候補生成・既存配置抽出・自動配置クリアを、`IsLocked=1`と同様に`IsManual=1`も保持対象として扱うよう修正。再最適化しても手動配置は消えない。
- 精査で`SqliteScheduleEditorServiceTests`の1テストが失敗。原因はロジックのバグではなく、テスト用fixtureの`RequiredSessions=2`が同fixture内の開講コマ数(1コマのみ)と矛盾しており、`UnassignedLessons`が意図せず1になっていたこと。`RequiredSessions=1`へ修正し解消。
- Release/x64 build: warning 0 / error 0。全66 tests passed (2+7+9+19+29)。Privacy gate passed。

### v0.1.0 checkpoint 24 (Claude)

ユーザー指示「⑤の残り含め、全ての実装(②、③、⑥なども全て)を順番に実装していってください」を受け、⑤の中核機能である日別グリッド編集から着手。

- `IScheduleEditorService`へ`GetOpenDatesAsync`/`GetBoardAsync`/`GetUnplacedSessionsAsync`/`MoveAsync`/`SetTeacherUnavailableAsync`を追加。
- `GetBoardAsync`は指定日について、行=有効コマ、列=指導可能かつ当日全コマ不可ではない講師（`TeacherUnavailability`・`TeacherAvailability`を解析）のグリッドを返す。全コマ不可の講師も`extraTeacherIds`で強制表示可能（「+講師を表示」用）。
- `SqliteFixedLessonService`に`MoveAsync`を追加。ロック済みは移動不可、移動先で生徒衝突・講師資格・可用性・講師上限（2枠）を再検証し、成功時は`IsManual=1`へ設定（Python版の「手動移動したcardはis_manual=True」仕様に合わせた）。AuditLogに`manual_assignment_moved`を記録。
- `SetTeacherUnavailableAsync`は`TeacherUnavailability`行の追加/削除。既に配置がある(講師,日付,コマ)を不可にはできない。
- ⑤ OptimizationPageへ日付選択・生徒名検索・未配置一覧（ドラッグ元）・動的に構築するグリッド（ドラッグ先、右クリックでロック切替/削除、セル右上でその場出勤可否切替）を追加。既存のコンボボックス方式の手動配置UIはそのまま残し、二重の入力経路を確保。
- ビルド警告0・エラー0、全70テスト成功（既存66＋新規4: グリッド構造、移動の成功/ロック拒否/同一セル拒否/生徒衝突拒否、講師全コマ不可時の列非表示と`extraTeacherIds`強制表示、既存配置があるセルの出勤不可拒否）。Privacy gate成功。
- 実機起動でクラッシュがないことと`Application Error`イベントが記録されていないことは確認したが、UI自動操作の仕組みがこの環境にないため、プロジェクトを開いて⑤画面のドラッグ&ドロップを実際に操作しての目視確認はできていない。次回ユーザーが実機操作で確認することを推奨。
- 未実装のまま残る⑤の項目: Undo/Redo、grid virtualization、header/row/grid間のscroll同期、複数コマ・複数講師の一括availability操作。

### v0.1.0 checkpoint 25 (Claude)

- `IScheduleEditorService`へ`CaptureSnapshotAsync`/`RestoreSnapshotAsync`を追加。`Assignment`・`TeacherUnavailability`全行をレコード列として取得し、復元時はDELETE後に元のIdを含めて再INSERTする（AUTOINCREMENTシーケンスは自動的に追いつくため後続のID採番と衝突しない）。
- ⑤へ「元に戻す」「やり直す」ボタンを追加。自動作成の実行、手動配置の追加/削除/移動/ロック切替、自動配置リセット、講師出勤可否変更など、⑤内のすべての変更操作の直前に現在状態をsnapshotしてundo stackへ積む方式（操作単位の逆操作ではなく、テーブル全体のstate replaceなので実装・検証が単純で取りこぼしがない）。
- Undo/Redo履歴はページのmemory上のみで、プロジェクトを開き直す・アプリを終了すると失われる（Python版の「process内のみ」の仕様と同じ）。
- ビルド警告0・エラー0、全71テスト成功（既存70＋snapshot往復1件）。Privacy gate成功。
- 実機起動でのクラッシュなしとApplication Errorイベント無しは確認したが、ドラッグ&ドロップ同様、UI自動操作の手段がないためUndo/Redoボタンの実クリック確認はできていない。

### v0.1.0 checkpoint 26 (Claude)

⑤の主要項目が一段落したため、②アンケート作成（Google Forms作成kit）の生徒フォーム学年分岐へ着手。

- `Subject.SchoolLevel`の文字列を「小/中/高/その他」へ分類（部分一致: "小"→小学校,"中"→中学校,"高"→高等学校、いずれも含まない場合は「その他」）し、`CONFIG.subjectsByLevel`として生成JSONへ埋め込み。
- 生成する`Code.gs`の生徒フォームを、学年(`ListItem`、小1〜高3の12択)で各校種の科目選択ページへ分岐するmulti-page構成へ変更。各校種ページには「中高一貫などで他学年の授業も受講しますか」のはい/いいえ分岐を追加し、「はい」で全校種の科目を追加選択できる「他学年の受講科目」ページへ進む。校種に対応する科目が1つもない場合は空のCheckboxItemを作らず案内文を表示する（Apps Script側でのランタイムエラーを回避）。
- 特記事項・学力テスト希望の設問を、日時列（参加可能コマの各日Checkbox群）より前に追加し、回答sheetの列順で日時列より左に来るようにした（Google Formsの回答列順は設問追加順に一致するため）。
- 講師フォームは`createTeacherForm`として分離しただけで内容は現状維持。
- Google上で実際にスクリプトを実行しての動作確認はこの環境からはできない（外部ネットワーク到達不可）。Apps Script API仕様（`ListItem/MultipleChoiceItem.createChoice(value, destinationPageBreakItem)`によるpage分岐、選択肢配列は全ページ作成後にまとめて設定する定石パターン）に基づいて実装した。次回ユーザーが実際にGoogle上で試すことを推奨。
- ビルド警告0・エラー0、全71テスト成功（既存70＋学年分岐のJSON構造検証1件を既存テストへ追加、件数据え置き）。Privacy gate成功。
- 未実装のまま残る②の項目: 講師指導可能科目用の別Apps Script、回答sheetの列正規化・診断。

### v0.1.0 checkpoint 27 (Claude)

③アンケート取込みへ、可用性形式（日付列あり）のdiff表示と削除候補の明示確認を追加。

- `ResponseImportPreview`へ`ResponseImportDiff`（生徒/講師それぞれの追加・変更・変更なし件数、削除候補`AvailabilityDiffKey`一覧）を追加。Previewの都度DBの既存`StudentAvailability`/`TeacherAvailability`と突き合わせて算出する。
- 削除候補＝取込対象に含まれる生徒・講師について、以前DBに登録済みだが新しい回答ファイルに含まれない日付。`IResponseImportService.ApplyAsync`へ`removeUnlistedAvailability`パラメータ（既定false）を追加し、trueの場合のみ削除候補を同一transactionで削除する。falseなら既存データはそのまま保持される（「削除候補は明示確認なしに削除しない」仕様どおり）。
- ③画面へdiff件数summary、削除候補一覧ListView、「削除候補を反映と同時に削除する」CheckBoxを追加。
- 希望講師ID（第1〜3希望）が未登録の場合の検証を、取込全体を止めるERRORからWARNING（IsError:false）へ引き下げた。Apply側は元々未登録IDをNULLとして無視する挙動だったため、実際の反映結果と検証結果の重大度を一致させた。ERROR/WARNINGは画面のissue一覧に`[エラー]`/`[警告]`として区別表示する。
- ビルド警告0・エラー0、全72テスト成功（既存71＋diff/削除候補confirmationの往復1件）。Privacy gate成功。
- 未実装のまま残る③の項目: 簡易形式（必要回数・勤務不可）側のdiff算出、Google Forms生回答の列mapping UI。

### v0.1.0 checkpoint 28 (Claude)

⑥出力の生徒配布・講師配布帳票を、単純な日付順一覧表からPython版仕様の「日曜始まり・土曜終わりの週calendar」形式へ変更。

- `SeminarSched.Reporting.Layout.WeeklyCalendarLayout`を新設。`Build(start,end,linesByDate)`が対象期間を含む日曜〜土曜の週群を生成し（`CalendarWeek`/`CalendarCell`）、`BuildStudentLabels`が「姓のみ表示、同姓のみ名の先頭1文字を付与」という生徒名表記規則を実装。Excel/PDF両rendererで共有するrenderer非依存レイアウトとして`SeminarSched.Reporting`側に置いた。
- `ScheduleReport`へ`StartDate`/`EndDate`（`CourseProject`から取得）、`AbsentStudents`（受講希望が1件もない生徒。講習に一度も参加しない生徒として「講習欠席一覧」へ列挙）、`ScheduleReportRow.SubjectShortName`（`Subject.ShortName`、未設定ならDisplayNameへfallback）を追加。
- `ExcelScheduleReportRenderer`/`PdfScheduleReportRenderer`の生徒別・講師別section/sheetを週calendarへ書き換え。生徒別セルは「科目略称 講師名t」、講師別セルは「科目略称 生徒表記」を表示。未配置・警告sheetの下に「講習欠席一覧」を追加。
- ビルド警告0・エラー0、全77テスト成功（既存72＋`WeeklyCalendarLayout`単体4件＋複数週・同姓生徒・欠席一覧を検証する結合test1件）。Privacy gate成功。
- 未実装のまま残る⑥の項目: 全体時間割（①行=コマ・列=講師のgrid、休校日除外、講師別コマ不可gray表示）は未着手。講師別folder packet（個別ファイル分割、通常担当優先→講習担当→その他の並び順、1ページ4名）も未実装。

### v0.1.0 checkpoint 29 (Claude)

ユーザー指示「全部とりあえず進めてみてください。元のpython仕様のもののv1.9.5の機能を全て持っている状態にしてください」を受け、⑥全体時間割を日付順の一覧表からPython版6.1仕様のgrid形式へ変更。

- `SeminarSched.Reporting.Layout.OverviewGridLayout`を新設。`Build(start,end,openDates,slotLabels,assignments)`が日曜〜土曜の週群を作り、休校日（`openDates`に含まれない日）は列から除外、各日は「その日に配置がある講師」だけを列として持つ。行はプロジェクトの有効コマ一覧、セルは学年・科目略称・生徒名。
- `ScheduleReport`へ`OpenDates`・`SlotLabels`を追加。Excel側は新しい「全体時間割」sheetをgrid形式で生成し、従来の日付順一覧は「配置一覧」sheetへ退避（削除はしていない）。PDF側は週ごとに独立したtable（列数が週によって異なるため）を生成し、日付header行をMigraDocの`MergeRight`でその日の講師列数ぶん結合。
- 1セル最大2名の表示は、Python版の「横分割」ではなく同一セル内の改行による縦積みとした（Excel/PDFとも複数列に動的に分岐するテーブル構造は実装コストが高く、視認性は保ちつつ実装の堅牢性を優先する判断）。コマ不可のgray表示（一部コマ不可の講師）は今回のscopeに含めていない。
- ビルド警告0・エラー0、全79テスト成功（既存77＋`OverviewGridLayout`単体2件）。Privacy gate成功。

### v0.1.0 checkpoint 30 (Claude)

③アンケート取込みへ、CSV/XLSX取込みとは独立した可用性の手動一括編集UIを追加。

- `IAvailabilityMatrixService`/`SqliteAvailabilityMatrixService`を新設。生徒・講師の一覧取得、開講日一覧、指定日の有効コマ一覧、指定日の複数対象×複数コマの現在値取得（`GetDayMatrixAsync`、未設定はlevel=1として扱う）、複数対象への一括level設定（`SetLevelAsync`）を提供。講師の場合はlevel=0で`TeacherUnavailability`にも反映し、level>0に戻すと解除する（CSV取込み時の同期ロジックと同一パターン）。
- ③画面へ、生徒/講師切替、複数選択ListView、日付・コマ・値(0/1/2)の選択と一括適用ボタン、選択中対象×選択日の現在値を表示する読み取り専用previewグリッドを追加。
- ビルド警告0・エラー0、全82テスト成功（既存79＋新規3: 一括適用の反映確認、講師level=0時のTeacherUnavailability同期と解除、開講外コマへの適用拒否）。Privacy gate成功。
- 未実装のまま残る項目: 複数日付・複数コマへの同時一括適用、週単位でのmatrix一覧編集。

### v0.1.0 checkpoint 31 (Claude)

Python版v1.9.5 `objectives.py`の辞書式目的（本引継ぎ書5.4節）のうち、未実装だった「3. 同一日への過度な集中を抑制」をCP-SAT解法へ追加。

- `CpSatScheduleSolver`のobjectiveへ、生徒ごとに「その日に1件でも配置があるか」を表す補助BoolVar（`AddMaxEquality`でその日の候補変数のORを取る）を導入し、使用日数に比例した加点を追加。同一生徒の複数受講希望を同じ日へ固めるより、別の日へ分散させる解を優先するようになる。
- 重みは1,000,000（配置数、最優先）を絶対に上書きしない範囲に収めつつ、希望講師一致の重み（PreferencePenalty×100、最大約1,000）より上位に設定（10,000/使用日）。CP-SATには真の逐次lexicographic解法はなく、単一のweighted-sum内でtierごとに重みの桁を分離する近似方式であることを明記。
- 新規test 1件（他条件が全く同じ場合、生徒の2つの受講希望が同日へ固まるより別日へ分かれる解を優先することを確認）を追加。既存19件のOptimization testはすべて挙動維持（目的関数を変えても既存の期待値に影響しない設計だったため）。
- ビルド警告0・エラー0、全83テスト成功。Privacy gate成功。
- 未実装のまま残る辞書式目的: 分散が悪い受講希望の改善、生徒週偏り最大値抑制、同一生徒・科目の担当講師分散抑制、講師稼働率の公平性、講師出勤日数圧縮。いずれも本項目と同様の「補助変数＋重み分離」方式で追加できるが、それぞれ独立した検証が必要なため個別checkpointとして扱う。

### v0.1.0 checkpoint 32 (Claude)

⑥出力の「未配置・警告」へ、Python版5.3節の通常担当優先度目標割合を下回っている生徒・科目を「通常担当不足」として追加。

- `SqliteOutputPackageService`へ`LoadRegularTeacherShortfallsAsync`を追加。目標回数は`CpSatScheduleSolver.MinimumRegularTeacherSessions`と同じ切り上げ式（`((priority-1)*required+3)/4`）をレポート側でも再現し、実績（そのLessonRequestで通常担当講師に割り当てられた回数）が目標を下回る場合のみ一覧化する。
- `ScheduleReport`へ`RegularTeacherShortfalls`を追加し、Excel「未配置・警告」sheetとPDFの同セクションへ表示。
- ビルド警告0・エラー0、全84テスト成功（既存83＋新規1: 通常担当2回中0回のケースを検出）。Privacy gate成功。
- 未実装のまま残る診断: 学年別上限超過などPython版のその他の診断種別。

### v0.1.0 checkpoint 33 (Claude)

⑥出力へ講師別folder packetを追加。出力実行のたびに「講師別」sub folder内へ講師ごとの独立したExcelファイルを生成する。

- `ScheduleReportRow`へ`IsRegularTeacher`（そのAssignmentのTeacherIdが当該生徒・科目のRegularLessonProfile.RegularTeacherIdと一致するか）を追加。
- `ExcelScheduleReportRenderer.RenderTeacherPacket`を新設。講師ごとに「担当一覧」（通常担当を先に列挙→講習担当（その他）の順）と「時間割」（週calendar、既存のWriteCalendarを再利用）の2sheetを持つ独立workbookを生成する。
- `SqliteOutputPackageService.GenerateAsync`が出力ごとに`講師別`folderを作り、`{講師名}t.xlsx`（ファイル名重複時は連番を付与、ファイル名不正文字は除去）で各講師の個別ファイルを書き出す。`OutputPackageResult`へ`TeacherPacketDirectory`を追加。
- Python版の「その他区分は1ページ4名」という詳細なPDFページ割りはExcel中心の現構成には該当せず、実装していない（Python原典を参照できないため、担当一覧の並び順という確認可能な部分だけを再現し、ページ割りの数値までは推測で実装していない）。個別ファイルはExcelのみで、PDF側は共通PDF内のsectionのまま。
- ビルド警告0・エラー0、全84テスト成功（既存の通常担当不足testへ講師別folder生成・担当一覧の並び順・時間割sheet存在の確認を追加。新規[Fact]は追加していないため件数据え置き）。Privacy gate成功。

### v0.1.0 checkpoint 34 (Claude)

ユーザー確認: 「まだv1.9.5と完全に同等ではない。最適化部分は後回しでよいので、それ以外のgapを埋めてほしい」との指示。最適化(辞書式目的の残り・複数戦略)は保留し、それ以外の未実装項目を順に着手。

- Homeの「最近使ったプロジェクト」一覧へ「表示しない」ボタンを追加。既存の`RecentProjectService.RemoveAsync`（今まではファイル未検出時の自動削除にしか使われていなかった）をUIから明示的に呼び出せるようにした。`RemoveAsync`自体に単体testがなかったため追加。
- 設定・logging基盤を新設。`SeminarSched.Application.Logging.IAppLogger`と`SeminarSched.Infrastructure.Logging.FileAppLogger`を追加し、`%LocalAppData%\SeminarSched.WinUI\logs\app-yyyyMMdd.log`へ日次でoperational logを記録する（14日保持で起動時に古いfileを自動削除）。個人情報（生徒・講師名、ファイルパス）は記録せず、件数・成否・経過時間などの構造情報のみを記録する方針をコード内コメントとPrivacy観点で明記。
- `App.OnLaunched`で`UnhandledException`をhookし、未処理例外を種別・メッセージ付きでログへ記録するようにした（従来は無音でクラッシュしていた）。
- project作成/open/close/backup/restore（HomePage）、時間割自動作成（OptimizationPage）、出力実行（OutputPage）にログ呼び出しを追加。
- ビルド警告0・エラー0、全88テスト成功（既存84＋新規4: RemoveAsyncの動作確認1件、FileAppLoggerのINFO/ERROR記録・保持期間超過ファイルの自動削除3件）。Privacy gate成功。
- 未実装のまま残るlogging関連: ①設定の個別保存操作など他の画面はまだログ対象外。ログ閲覧・エクスポートUIはなし（ファイルを直接開く運用）。

### v0.1.0 checkpoint 35 (Claude)

①設定の生徒・講師・科目一覧へ、表示中の一覧をクライアント側で絞り込む検索boxを追加（`SetupPage`）。

- `ReloadAsync`で取得した全件を`_studentItems`/`_teacherItems`/`_subjectItems`としてページに保持し、検索box(`StudentSearch`/`TeacherSearch`/`SubjectSearch`)の`TextChanged`で`MasterItem<T>.Display`文字列への部分一致（大文字小文字を無視）でフィルタしてから`ListView.ItemsSource`へ反映する。サーバー側クエリではなくクライアント側filterなので、追加のSQLite queryは発生しない。
- WinUI UI層はこのプロジェクトの既存方針どおり自動テスト対象外（Infrastructure/Applicationのみ単体test対象）。ビルド成功のみで動作確認済み、実機でのタイプ確認は未実施。
- ビルド警告0・エラー0、全88テスト成功（UI限定の変更のためテスト件数は据え置き）。Privacy gate成功。
- 未実装のまま残る①の項目: ID自動採番wizard、講師の資格matrix一括操作、科目の校種preset。

### 次回最初に確認するファイル

- `AGENTS.md`
- `Directory.Build.props`
- `docs/FEATURE_PARITY.md`
- `docs/adr/0001-platform-and-architecture.md`
- `docs/releases/v0.0.0.md`

## 1. この文書の目的

この文書は、別のCodexチャットがSeminarSchedの文脈を失わず、既存Python版を参照しながら
C# + WinUI 3版を独立プロジェクトとして開発するための引き継ぎ資料である。

新しいチャットでは、最初にリポジトリ直下の`AGENTS.md`と本書を全文読むこと。その後に
必要な範囲だけPython版のコード、テスト、ADR、画面を調査する。

## 2. 絶対に守る方針

### 2.1 Python版を変更しない

現在の`SeminarSched`はPython 3.12、PySide6/QML、SQLite、SQLAlchemy、Alembic、
OR-Tools CP-SATで作られたv1.9.5 Betaの参照実装である。今後は読み取り専用とする。

- Python版のコード、テスト、migration、UI、installer、workflowを変更しない。
- Python版の`main`へWinUI関連コミットを入れない。
- Python版に新しいtagやreleaseを作らない。
- Python版で問題を見つけても、WinUI版側の設計・テスト・issueとして扱う。
- 本書と`AGENTS.md`を追加する今回の文書作業だけが凍結前の整理作業である。

### 2.2 WinUI版は完全に別プロジェクト・別Gitリポジトリ

新実装の正式な作業名は`SeminarSched.WinUI`とする。

- このPythonリポジトリから見た推奨相対位置: `..\SeminarSched.WinUI`
- Python版`seminarSched`の内側には作らない。
- 独立した`.sln`、`.gitignore`、`README`、`LICENSE`、CI、version、releaseを持たせる。
- GitHubにも`SotaFurukawa/SeminarSched.WinUI`という別リポジトリを作る。
- 既存Pythonリポジトリのbranchやsubmoduleとして管理しない。
- GitHub作成前に同名リポジトリの有無を確認する。存在する場合は新規作成せず内容を調べる。

### 2.3 原則完全移植

WinUI版は試作UIや一部機能版ではなく、Python版v1.9.5で利用者が操作できる全機能の
原則完全移植を目標とする。次を省略して「移植完了」としてはならない。

- プロジェクト・基本情報・設定
- Googleフォーム作成キットと回答取込
- 入力検証と警告
- OR-Toolsによる自動時間割作成
- 手動時間割編集、固定、Undo/Redo、再最適化
- Excel/PDF帳票
- バックアップ、復元、監査ログ
- Windows配布、installer/portable相当、テスト、利用者向け文書

ただし、Python版で意図的に停止中または未実装の機能まで、無断で新規実装する意味ではない。
特に集団授業の操作フローはv1.6.0以降停止中で、再導入時はv2.0.0相当の大きな機能として
別途設計する方針だった。WinUI初期版ではPython v1.9.5と同じ境界を再現する。

## 3. 現在のPython版の状態

### 3.1 バージョンと品質

- アプリversion: `1.9.5`
- release channel: Beta
- 最終確認commit: `1d323a4`
- 直近ローカル品質確認:
  - pytest: 536件成功
  - Ruff lint/format: 成功
  - mypy strict: 181 source files、issueなし
  - QML lint: 終了コード0。動的QML propertyに対する既知warningが3件
- `v1.9.5` tagは上記commitへ付け直してpush済み。Windows release workflowの完了監視は
  ユーザーのクレジット節約方針により行っていないため、GitHub Actionsとdraft releaseは
  WinUI着手前に必要に応じて確認する。

### 3.2 技術構成

| 領域 | Python参照版 |
|---|---|
| UI | PySide6 / Qt Quick / QML |
| 言語 | Python 3.12 |
| DB | SQLite |
| ORM / migration | SQLAlchemy 2 / Alembic |
| 最適化 | OR-Tools 9.14 CP-SAT |
| Excel | openpyxl / XlsxWriter |
| PDF | Qt系描画と共通帳票モデル |
| 設定・保存先 | YAML / platformdirs |
| 配布 | Nuitka、Portable ZIP、Inno Setup installer |
| 品質 | pytest、Ruff、mypy、QML lint、GitHub Actions |

ソースの主要境界は次のとおり。

- `src/summer_scheduler/domain`: 業務ルールと値の解釈
- `src/summer_scheduler/application`: use case、transaction境界、入力検証
- `src/summer_scheduler/infrastructure`: SQLite、Excel、export、設定、project file
- `src/summer_scheduler/optimization`: DTO、候補生成、CP-SAT、目的関数、独立validator
- `src/summer_scheduler/reporting`: Excel/PDF共通レイアウト
- `src/summer_scheduler/ui`: QMLとViewModel
- `tests`: unit、integration、scenario、UI contract
- `docs/adr`: 重要な設計判断

### 3.3 データモデル

主要テーブルは次のとおり。

- `ApplicationMetadata`
- `Campus`
- `CourseProject`
- `OutputSetting`
- `TimeSlot`
- `OpenDate`
- `Student`
- `Teacher`
- `Subject`
- `TeacherQualification`
- `RegularLessonProfile`
- `LessonRequest`
- `StudentAvailability`
- `TeacherAvailability`
- `GroupLesson` / `GroupLessonStudent`（互換保持。現在の操作UIは停止中）
- `ImportBatch`
- `ImportSourceSnapshot`
- `ValidationIssue`
- `AuditLog`
- `OptimizationRun`
- `Assignment`

Alembic revisionは`20260728_0001`から`20260912_0011`まで存在する。アプリ管理DBと
各`.jukuschedule`プロジェクトファイル内SQLite DBの責務を分離している。

## 4. 業務フローと画面

左ナビゲーションの中心フローは次の6段階である。

1. 設定
2. アンケート作成
3. アンケート取込
4. 時間割編集
5. 時間割自動作成
6. 出力

ホームには、プロジェクトを開いていなくても操作できる「生徒の基本情報」「講師の基本情報」
と共通基本情報Excelの導線がある。プロジェクト未選択時は業務フローだけをoverlayで無効化し、
基本情報画面は無効化しない。最近使用したプロジェクトには「開く」と「表示しない」がある。
進行済みstepはproject metadataへ保存し、再起動後も復元する。

ウィンドウ上部にはアプリ全体の「すべて保存」がある。画面ごとの保存ボタン乱立は避け、
編集操作は原則即時commitし、「すべて保存」は共通基本情報Excelの再読込・反映と安全な
復旧用保存点の作成を含む。

### 4.1 ホームとプロジェクト

- 新規作成時に年度、春期/夏期/冬期、開始日、終了日をプルダウン指定する。
- `2026夏期講習`のような名称を自動生成する。
- `.jukuschedule`はSQLiteを格納するproject fileである。
- 開く、別名保存、複製、手動バックアップ、最近使用、非表示を提供する。
- project open直後と設定間隔ごとに既定5世代の自動バックアップを作る。
- migration前backup、SQLite整合性確認、復旧候補、原子的復元を備える。
- 復元前backupを作れない場合は置換しない。

### 4.2 基本情報

共通基本情報Excelは`生徒・講師_基本情報.xlsx`で、次の5シートを持つ。

1. 生徒
2. 講師
3. 科目
4. 講師対応科目
5. 通常授業

機能:

- 新規template作成、既存正本をExcelで開く、検証preview、transaction反映
- 生徒・講師の個別追加・編集wizard
- 有効/退席の扱い
- 講師の指導可能科目
- 生徒の通常授業科目、通常担当講師、担当優先度、1対1必須
- 科目コード、表示名、帳票用略称
- 外部Excelを保存した後、「すべて保存」で再読込してDBと画面へ即時反映
- 取込前backupと正規化済み正本の保持

### 4.3 設定

- プロジェクト年度、講習区分、開始・終了日
- コマ名、表示名、開始・終了時刻、有効/無効、dragによる並べ替え
- 開校日・休校日
- 日付ごとに使用可能なコマを設定
- 複数日を選択し、異なる既存値があっても選択操作で更新可能
- 変更は自動反映し、画面単位の保存ボタンへ依存しない
- 科目、科目略称、校種/学年との対応

### 4.4 Googleフォーム作成キット

外部通信をせず、現在の設定から次をローカルフォルダーへ生成する。

- 生徒用Apps Script
- 講師勤務日時用Apps Script
- 講師指導可能科目用Apps Script
- Googleフォーム作成手順

画面にはApps Scriptサイトを開く「Google App Script」ボタンがあり、手順はモーダルから
別ウィンドウ表示へ切り替えられる。

生徒フォームの重要仕様:

- 氏名・学年入力ページに「中高一貫などで他学年の授業を受講する」checkを1つだけ置く。
- checkなしなら学年から学校区分を自動決定し、その校種の科目だけを表示する。
- checkありなら小・中・高の学校区分と科目を選べる。
- 回答sheetでは学校区分、受講教科、回数を教科番号ごとに正規化する。
- 回答列順は、受講不可日時の確認、特記事項、学力テストを日時列より左へ置く。
- 手入力済み行を上書きせず、フォーム回答は次の空行へ追記する。

### 4.5 アンケート取込

- 生徒回答と講師回答のxlsx/CSVを2ファイル同時に選ぶ。
- 「回答ファイルを選ぶ」「内容を確認する」「反映完了」の3stepを同じ幅で表示する。
- GoogleスプレッドシートからダウンロードしたCSV/XLSXを氏名・学年・日付・コマで照合する。
- 最大4科目、受講回数、不可日時を正規化する。
- UTF-8/CP932 CSV、Google Forms XLSX、旧列形式、複数check値を扱う。
- 追加・変更・変更なし・削除候補・error・warningをpreviewする。
- errorがあれば反映不可。削除候補は明示確認なしに削除しない。
- 反映直前に原本を再読込・再検証し、ImportBatch/AuditLogと同じtransactionで保存する。
- 取込後に複数生徒を選び、日付・コマ単位で参加可/不可を編集できる。
- 反映済み原本と統合XLSXをproject内へ保存する。

名詞は「取込」、動詞は「取り込む」で表記を統一する。

### 4.6 時間割編集

- 行=コマ、列=当日出勤候補講師のgrid。
- 休校日は表示しない。
- 当日全コマ不可の講師は通常表示しない。
- 一部コマだけ可能な講師は列を表示し、不可コマをgrayにする。
- `+`から講師を一時表示し、コマごとの勤務可/不可を小さい操作で変更できる。
- 未配置cardをdrag/dropして配置する。
- 配置済みcardを別セルまたは未配置へ戻せる。
- cardはdrag中もgridより前面に表示する。
- 氏名検索中は一致cardを先頭へ移し、検索解除で元順序へ戻す。
- card文言は日本語の科目表示を使う。
- 指導可能科目外への手動配置は即拒否せず、warningと確認後に許可できる。
- hard constraint違反は配置不可。
- Undo/Redo、差分、詳細編集、note、lock/unlock、全配置resetを持つ。
- header、コマ行、講師列、本体gridのscroll位置を同期する。

#### 手動配置とロックの重要な違い

v1.9.5で明確化された重要仕様である。

- 手動配置・手動移動したcardは`is_manual=True`にする。
- 手動配置しただけでは`is_locked=True`にしない。利用者は後から別講師へ再移動できる。
- ただし次回の自動作成では、手動配置の日時・コマ・講師を保持して動かさない。
- `is_locked=True`は、画面上の手動移動も禁止する明示的な固定である。
- 自動作成後も手動配置metadataを失わない。

Python版の一部古いdocstringやREADMEには「手動配置を自動lock」と読める文が残っている可能性が
ある。実装コード、v1.9.5 tests、`CHANGELOG.md`を正とする。

## 5. 自動時間割作成・最適化

### 5.1 基本構造

- LessonRequestの必要回数を個別sessionへ展開する。
- open date、使用コマ、生徒/講師availability、講師資格等から疎なcandidateを生成する。
- greedy初期解を作り、独立検証済みのcomplete hintとしてCP-SATへ渡す。
- solver実行とUIはworker threadで分離する。
- cancelは`CpSolver.stop_search()`へ協調的に伝える。
- 入力DTOは不変化し、version付きJSONとSHA-256 fingerprintで実行前後を照合する。
- solver結果を直接DBへ保存せず、独立validator通過後に1 transactionでAssignmentを置換する。
- `UNKNOWN`、`INFEASIBLE`、`MODEL_INVALID`では未保証のsolver variableを読まない。

### 5.2 主なハード制約

- 各sessionはちょうど1つのcandidateへ配置、または未配置。
- 生徒は同一日時に重複不可。
- 講師は同一日時に最大2名。
- 1対1必須sessionがある枠へ別生徒を重ねない。
- 指導可能科目、開校日、有効コマ、availabilityを候補条件とする。
- 集団授業blockとの生徒・講師重複を禁止する。
- 生徒/講師の連続上限、空きコマ条件を扱う。
- explicit lockと手動配置保持を自動作成で動かさない。
- 通常担当優先度5は、生徒と通常担当講師の共通可能コマおよび講師容量が足りる範囲で必須。
  講師が全期間欠席、一部期間しか来られない、または容量不足の分だけ代講を許す。

### 5.3 通常担当講師の優先度

通常担当の最低担当割合は次の業務ルールとして扱う。

| 優先度 | 通常担当の目標/最低割合 |
|---:|---:|
| 5 | 100%（可能容量まではhard requirement） |
| 4 | 75% |
| 3 | 50% |
| 2 | 25% |
| 1 | 0% |

端数は切り上げる。通常担当が来られず割合を満たせない場合、授業を未配置にするのではなく、
資格と出勤条件を満たす代講講師へ配置し、対象生徒、科目、優先度、目標回数、実績回数を
「確認が必要な項目」および「未配置・警告」へ表示する。

優先度5と手動配置が衝突する場合は、利用者が明示的に置いた手動配置を保持する。勝手に通常担当へ
戻さず、必要な不足警告を出す。

### 5.4 辞書式目的の大まかな順序

現行コードの`optimization/objectives.py`を正本とする。概略は次の順序。

1. 未配置数の最小化
2. 通常担当不足（優先度5、4、3、2の順）
3. 同一日への過度な集中を抑制
4. 最も分散が悪い受講希望の改善
5. 生徒ごとの週偏り最大値を抑制
6. 通常担当・希望講師の一致
7. 同一生徒・科目で担当講師が増えすぎないこと
8. 受講希望ごとの期間内分散
9. 生徒単位の週・月偏り
10. 講師稼働率の公平性（設定有効時）
11. 講師の出勤日数圧縮と週分散
12. 稼働コマ数、希望日時、既存配置維持

分散は「誰か1人だけ良ければ全体scoreが上がる」方式にせず、最も悪い生徒/受講希望を先に
改善する。科目も同じ科目が連続して固まりすぎないようにする。講師は勤務可能枠に対する実際の
稼働率を公平化しつつ、同じ勤務コマ数なら出勤日数が少ない解を好む。

### 5.5 実行時間と進捗

- 高速: 30秒
- 標準: 120秒
- 高品質: 600秒

v1.9.5では各辞書式工程へ制限時間を配分し、最初の重い工程だけで全時間を使い切らない。
工程が`FEASIBLE`で最適性未証明なら、その値を悪化させない境界を追加して後続へ進み、最後に
残り時間で優先度の高い未完了目的を追加改善する。全目的が数学的に`OPTIMAL`と証明された場合は、
制限時間前でも終了してよい。進捗barは経過時間と工程番号の大きい方を使い、現在工程と
`経過/制限時間`を表示する。

## 6. 出力

出力画面は「配布物確認」を統合済みで、次の5種類を扱う。

1. 全体時間割
2. 生徒配布時間割
3. 講師配布時間割（学年順）
4. 講師配布時間割（講師別）
5. 未配置・警告一覧

ExcelとPDFを生成する。講師別出力はfolderを作り、その中へ`架空講師あおいt.xlsx`のような
講師別fileを生成する。実名はruntime dataから取得し、テスト・Gitへ固定で入れない。

### 6.1 全体時間割

- 日曜始まり・土曜終わりの週単位。
- 設定期間に重なる週数だけ行/sectionを作る。季節名から週数を決めない。
- 休校日を除き、週内の日付を横方向へ並べる。
- その日に出勤予定の講師だけを表示する。
- 一部コマ不可はgray表示。
- 1セル最大2名を横分割し、各生徒は「学年、科目略称、生徒名」を縦方向に表示する。
- 不要な4人ごとの隙間や`[未確定]`表示を入れない。

### 6.2 生徒・講師配布帳票

- Python版で確定したカレンダー形式を移植する。
- 期間に重なる日曜～土曜の週数を段数にする。
- 生徒名表記は空白なし。同姓がいる場合だけ名の先頭1文字を付ける。
- 学年は`H3`ではなく`高3`等の日本語表記。
- 科目は設定の略称を使う。
- 講習不参加の生徒は個別カレンダーを作らず、先頭の講習欠席一覧へ学年・氏名を表形式で出す。
- 講師配布版には、生徒名とカレンダーの間に`科目略称 通常担当講師名t`を表示する。
- 講師別packetの並びは、通常授業担当生徒、講習担当生徒、その他生徒の順。その他は1ページ4名。
- PDFはA4に収め、複数生徒を同一PDFにする場合も生徒/section単位で正しく改ページする。

### 6.3 安全性

- 出力直前に最新DBを再読込し、独立validatorを再実行する。
- hard violationがあれば出力しない。
- 同名fileを確認なしで上書きしない。
- temporary file成功後だけ原子的に置き換える。
- preview PDFは画面枠内へclipし、横幅不足時は下段へwrapする。
- 出力物は個人情報を含むため、Git、release、CI artifactへ含めない。

## 7. バックアップ、監査、データ安全

- SQLite backup APIを使い、開いているDBをExplorerの通常copyで複製しない。
- project open、migration、明示保存、再最適化前に目的別backupを作る。
- 自動backupは世代管理する。
- 復元前backupを作ってからatomic replaceする。
- import、manual edit、optimization保存はAuditLogを残す。
- Undo/Redoはprocess内command stackで、再起動をまたがない。
- project fingerprintが外部変更を検出したら、古いUndo/Redo stackを破棄する。
- app終了時もcommit済みdataだけを残し、未commit transactionはrollbackする。

## 8. プライバシーと外部資料

ユーザーから開発中に実在のCSV/XLSX/PDF/画像が提供されたが、これらは仕様確認のための
ローカル参照資料であり、GitHubへ上げてはならない。特に氏名を含むファイル、プロジェクトDB、
最適化log、生成時間割をcommitしない。

テストは`架空`であることが明確な氏名、または匿名IDのみを使用する。CIとreleaseは実データが
なくても完結しなければならない。新しいWinUI repoでも同じprivacy gateを最初から設ける。

## 9. 現在の既知の未実装・保留事項

Python v1.9.5で未実装または正式受入未完了のもの:

- 選択日・選択生徒・選択講師周辺だけの部分再最適化
- セル、日付、講師、選択範囲単位の一括lock
- Undo/Redo履歴のアプリ再起動をまたぐ復元
- 集団授業の操作フロー（DB互換形式と内部serviceは残るが、UIは停止中。再導入はv2相当）
- Google APIによる直接作成/同期
- cloud同期、複数人同時編集
- code signing。現在は未署名配布方針
- 完成artifactの最終SBOM/第三者license監査
- 多様なclean Windows端末、DPI、実printer、upgrade installでの正式な通し受入
- すべての品質presetが全辞書式工程を常に`OPTIMAL`まで証明する保証

WinUI版では、これらを初期版へ勝手に追加せず、まずPython v1.9.5 parityを達成する。追加する
場合はversion、scope、acceptance criteriaを別途合意する。

## 10. 文書間の相違と正本

長期間に多数の仕様変更があったため、古いPhase文書やREADMEの一部は最新挙動と一致しない。
特に次は注意する。

- 手動配置は「UI上もlock」ではなく、「再編集可能だが自動作成では保持」が最新。
- `FEASIBLE`工程で即終了する古い説明より、v1.9.5の後続工程・追加改善動作が最新。
- 集団授業はDB/serviceが存在しても、現在の利用者向けflowでは停止中。
- 出力は独立した「配布物確認」ではなく、出力画面へ統合済み。

判断順序:

1. v1.9.5の実装コード
2. v1.9.5で通過しているtests
3. `CHANGELOG.md`の新しいversion
4. 本引き継ぎ書
5. ADR
6. README / specification / 過去Phase文書

相違を見つけた場合、Python版は直さず、WinUI版のparity matrixへ記録する。

## 11. WinUI版で先に決めるべき技術事項

以下はまだ実装開始前の判断事項である。勝手に確定せず、ADRへ残す。

1. 対象.NET versionとWindows App SDK/WinUI 3 version
2. installer/MSIXまたは別installer、portable相当の提供方法
3. SQLite access層とmigration方式
4. `.jukuschedule`をPython版と直接相互運用するか、読取import後にWinUI形式へ変換するか
5. OR-Tools .NETでのCP-SAT parityとsolver version固定
6. Excel libraryとPDF rendererのlicense・日本語font・印刷品質
7. PDF preview componentとoffline要件
8. logging、crash recovery、backup保存先
9. GPLv3のPython版を参照移植する際のWinUI版license。権利者の意向も含めて明示確認する
10. GitHub ActionsでのWindows build、test、artifact、release方式

特に`.jukuschedule`互換性は早期に決める。既存projectを開く必要がある場合も、最初はcopyを
作って読取検証し、Python版projectをその場でmigration/上書きしない。

## 12. WinUI版の推奨アーキテクチャ

Python版の責務分離を保ち、例えば次のSolution構成にする。名称はADRで確定してよい。

```text
SeminarSched.WinUI.sln
src/
  SeminarSched.WinUI/             # WinUI 3 views, navigation, ViewModels
  SeminarSched.Application/       # use cases, transaction orchestration
  SeminarSched.Domain/            # entities, value objects, business rules
  SeminarSched.Infrastructure/    # SQLite, files, Excel/PDF, settings
  SeminarSched.Optimization/      # immutable input, candidates, CP-SAT, validator
  SeminarSched.Reporting/         # renderer-independent layout model
tests/
  SeminarSched.Domain.Tests/
  SeminarSched.Application.Tests/
  SeminarSched.Optimization.Tests/
  SeminarSched.Infrastructure.Tests/
  SeminarSched.AcceptanceTests/
docs/
  adr/
  parity/
```

MVVM toolkit等を採用してもよいが、ViewModelからDBやOR-Toolsを直接呼ばない。solver input、
report layout、import previewはUI frameworkから独立した型にする。

## 13. 推奨移植手順

### Phase W0: 独立repoとparity inventory

- sibling directoryに新規SolutionとGitを作成
- GitHubへ`SotaFurukawa/SeminarSched.WinUI`を作成
- license/privacy/security/.gitignore/CIの土台を作る
- Python v1.9.5の画面、use case、DB、tests、帳票をparity matrixへ列挙
- 実データを使わないsynthetic acceptance datasetを作る

### Phase W1: shell、設定、project lifecycle

- WinUI navigation、window、icon、version表示
- app settings、workspace、recent projects
- SQLite schema/migration
- 新規/open/save-as/duplicate/backup/recovery

### Phase W2: master dataと設定

- 生徒、講師、科目、資格、通常授業
- 共通基本情報Excel
- project期間、コマ、開校日、日別使用コマ

### Phase W3: questionnaireとimport

- Apps Script kit
- 生徒/講師回答の2file検証
- diff/error/warning/transaction反映
- 取込後availability編集

### Phase W4: optimization core

- Python版DTOとcandidate条件をC#へ移植
- hard constraints、優先度、辞書式目的、progress/cancel
- 独立validator
- Python版scenarioに対応するgolden test

### Phase W5: schedule editor

- 大規模grid virtualization
- drag/drop、warning confirmation、manual/lock semantics
- undo/redo、reset、teacher availability編集
- 一部固定再最適化

### Phase W6: reporting

- 共通layout model
- 5種類のExcel/PDF
- preview、atomic save、teacher packet folder
- synthetic golden workbook/PDF構造test

### Phase W7: distribution and acceptance

- backup/recovery/auditのfailure injection test
- installer/portable相当
- clean Windows、offline、日本語path、OneDrive、DPI、長時間、印刷受入
- license/SBOM/checksum/privacy gate

各PhaseはPython版の該当scenarioと同等のテストを通してから次へ進む。UIだけ先に全画面を作り、
中身がplaceholderのまま完成扱いにしない。

## 14. 最初の新チャットで行うこと

1. Python repoでは`AGENTS.md`と本書を読む。
2. `git status`を確認し、Python repoを変更しない。
3. siblingの`SeminarSched.WinUI`とGitHub同名repoの存在を確認する。
4. 存在しなければ別directory・別Git・別GitHub repoとして作る。
5. W0のparity matrixとarchitecture ADRを最初に作る。
6. `.jukuschedule`互換、license、配布形式の未決事項を明示する。
7. synthetic dataのみで最小の縦sliceを実装し、CIを通す。

新しいチャットへの開始指示は次のとおり。

> `AGENTS.md`と`docs/CODEX_HANDOFF.md`を全文読み、これまでのSeminarSchedの文脈を
> 引き継いでください。既存Python版は読み取り専用とし、変更しないでください。
> C# + WinUI 3版`SeminarSched.WinUI`を、別directory・別Git・別GitHub repoとして
> 作成してください。Python v1.9.5の利用者向け機能を原則完全移植し、まずPhase W0の
> parity inventory、architecture ADR、CI基盤から開始してください。
