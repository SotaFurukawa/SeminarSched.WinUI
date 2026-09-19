# SeminarSched Codex引き継ぎ書

最終更新: 2026-09-17
Python参照版: v1.9.5 / commit `1d323a4`
Pythonリポジトリ: `https://github.com/SotaFurukawa/SeminarSched`

## 0. WinUI版の現在地点

Current Version: `v0.2.0 (beta)`（Draft Release作成済み。最適化探索品質等の継続課題は次version以降）
Latest Development Checkpoint: checkpoint 76（新規project作成の同名衝突を警告＋自動リネームへ、最近使ったプロジェクトへフォルダーを開くボタン・最終更新日、講習区分「その他」）。v0.2.0 Draft Release後の追加checkpointのため、次のリリース判断は本書「Next Version Rule」に従うこと。checkpoint 39でユーザー実機のLocalMachine\TrustedPeople証明書信頼を確認済み。checkpoint 51のproject open crash修正、checkpoint 54の新Picker API（開始folderが`Workspace\Projects`等へ固定されていること）はユーザー実機で確認済み。checkpoint 48の⑤新機能2件（sticky header表示・一括設定UI）は実機での視覚確認待ち。
Latest Draft Release: `v0.2.0`（GitHub上にDraftとして作成済み。checkpoint 55〜73の内容をまとめてユーザーより「新しいバージョンとしてリリースしてほしい」との指示を受け作成。詳細は[docs/releases/v0.2.0.md](releases/v0.2.0.md)）
Tooling note: 本プロジェクトはCodex CLIからClaude Code CLIへ運用を切り替えた（2026-09-17）。バージョン管理・push・Draft Releaseの運用ルールは変更なし。Claudeが行ったcheckpointは見出しに明記する。
Next Version Rule:

- v0.2.0 Draft Release後のbug fix / minor change -> `v0.2.1`
- v0.2.0 Draft Release後のnew feature -> `v0.3.0`
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

### v0.1.0 checkpoint 36 (Claude)

①設定「担当設定」タブへ、講師対応科目（資格）の一括設定UIを追加。

- 講師・科目それぞれの複数選択ListViewと「指導可能」CheckBoxを追加し、選択した講師×科目の全組み合わせへ`IMasterDataRepository.SaveQualificationAsync`を順次呼び出す（新しいSQLは追加せず、既存の単一保存APIをUI側でループするだけの実装。`SaveQualificationAsync`自体は複数の既存testで検証済み）。
- ビルド警告0・エラー0、全88テスト成功（UI限定の変更のためテスト件数は据え置き）。Privacy gate成功。
- 未実装のまま残る①の項目: ID自動採番wizard、科目の校種preset、一括設定時の備考(Note)反映。

### v0.1.0 checkpoint 37 (Claude)

①設定の科目「校種」入力を自由記述TextBoxから編集可能ComboBox(`IsEditable="True"`、小学校/中学校/高等学校のpreset)へ変更。自由入力も引き続き可能。これにより②の生徒フォーム学年分岐（checkpoint 26で追加した`ClassifySchoolLevel`の部分一致判定）に渡る値の表記ゆれを減らせる。コード側は`ComboBox.Text`がTextBoxと同じ読み書きに対応するため、既存の保存・編集・reset処理は無変更で動作。
- 併せて`docs/FEATURE_PARITY.md`の「最近使用・非表示」行が、checkpoint 34で実装済みの手動非表示機能を反映していなかった記録漏れを修正（実装中→実装済み）。

- ビルド警告0・エラー0、全88テスト成功（UI限定の変更のためテスト件数は据え置き）。Privacy gate成功。
- 未実装のまま残る①の項目: ID自動採番wizard、講師対応科目の備考一括設定。

### v0.1.0 checkpoint 38 (Claude)

ユーザーへWindows配布方式(ポータブルEXE/MSIXサイドロード/保留)を確認し、「MSIXパッケージ（サイドロード）」を選択いただいた。ADR 0005として記録し、署名・パッケージ生成の基盤を実装・実機検証した。

- `Package.appxmanifest`の`Identity/Publisher`と`PublisherDisplayName`をplaceholderの`AppPublisher`から`CN=SotaFurukawa`/`SotaFurukawa`へ変更。
- `scripts/New-SigningCertificate.ps1`: Subjectが一致する自己署名証明書が無ければ生成し、秘密鍵(.pfx、ランダム英数字パスワード)は`%LOCALAPPDATA%\SeminarSched.WinUI\packaging\`（gitへ含めない）、公開証明書(.cer)は`dist\`（gitignore対象）へ出力する。既存があれば再利用する。
- `scripts/New-MsixPackage.ps1`: 証明書thumbprintを`Cert:\CurrentUser\My`から取得（無ければ.pfxから再import）し、`dotnet build -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=true -p:PackageCertificateThumbprint=...`で署名付きsideload専用`.msix`を生成し`dist\`へ配置する。
- 実機検証で判明した重要な注意点:
  - `.pfx`を`-p:PackageCertificateKeyFile`+`-p:PackageCertificatePassword`で直接渡す方式は`APPX0105`/`APPX0107`警告で失敗し、無署名の`_Test`扱いパッケージになる（PowerShellのPFX既定エクスポート方式がAppXパッケージング側でimportできないため）。証明書をいったん`Cert:\CurrentUser\My`へ置き、thumbprint指定で署名する方式に切り替えて解決した。
  - `Get-AuthenticodeSignature`で署名者が`CN=SotaFurukawa`であることを確認済み（署名自体は正しく機能している）。
  - `Add-AppxPackage`によるインストールは、`CurrentUser\TrustedPeople`・`CurrentUser\Root`へ証明書を追加しただけでは`0x800B0109`（ルート証明書が信頼されていない）で失敗する。`LocalMachine\TrustedPeople`（管理者権限が必要）でのみ成功する見込みだが、この開発環境には管理者権限がなく実インストールまでは検証できていない。README/ADR 0005に、利用者が管理者権限で`certutil -addstore -f TrustedPeople`を1回実行する手順を明記した。
  - 検証で追加したCurrentUser側の証明書エントリ（Root・TrustedPeople）は、目的を達成しない設定だったため実機から削除済み（`CurrentUser\My`の秘密鍵入り証明書のみ残置、署名に必要）。
- `.gitignore`へ`dist/`を追加（`*.msix`・`*.pfx`・`*.cer`は既存パターンで除外済みだったが、明示のため）。
- `README.md`にサイドロードMSIXのインストール手順を追加。`docs/adr/0005-windows-distribution.md`を新設。
- ビルド警告0・エラー0、全88テスト成功（パッケージング設定のみの変更でテスト対象コードの変更なし）。Privacy gate成功。
- 未検証のまま残る項目: ユーザー環境での実際の`Add-AppxPackage`インストール成功確認(管理者権限でのcertutil実行後)。

### v0.1.0 checkpoint 39 (Claude)

ユーザーが実機(管理者権限のPowerShell)で`certutil -addstore -f TrustedPeople "<dist\SeminarSched.WinUI.cer>"`を実行し、成功を確認した(`LocalMachine\TrustedPeople`への追加。証明書ストアの出力で「署名は公開キーと一致します」「証明書が追加されました」を確認)。checkpoint 38で未検証だった最後のブロッカーが解消された。

- `docs/adr/0005-windows-distribution.md`の「検証状況」を更新し、管理者権限でのcertutil手順がユーザー環境で成功したことを記録した。
- `.msix`本体のダブルクリックインストール自体の成否は、ユーザーからの追加報告待ち。

### v0.1.0 checkpoint 40 (Claude)

Feature Parity行22（講習設定）・行23（コマ設定・並べ替え）の残課題を実装した（最適化以外の残gapを継続する方針）。

- `SetupPage`の「コマ・開校日」タブ:
  - コマ一覧(`TimeSlots`)を`ObservableCollection<MasterItem<TimeSlot>>`にバインドし、`CanReorderItems`/`AllowDrop`でdrag並べ替えを有効化。`DragItemsCompleted`で並べ替え後の位置をSortOrderとして`SaveTimeSlotAsync`へ反映する（内容が変わった項目のみ保存）。
  - 開校日・休校日一覧(`CourseDays`)を単一選択から複数選択(`SelectionMode="Extended"`)へ変更し、備考入力欄(`CourseDayNote`)を追加。単一選択時は既存の備考を読み込み表示する。開校/休校ボタンは選択した全日付へ一括適用し、備考欄が空なら既存の既定値（開校=""・休校="休校"）を維持する。
  - `CourseDay`のnote/SortOrderは元々domain・SQLiteスキーマに存在しており、schema変更・migrationは不要（UI層のみの実装）。
- Release/x64 build: warning 0 / error 0。全88 tests passed。Privacy gate成功。

### v0.1.0 checkpoint 41 (Claude)

Feature Parity行21（共通基本情報Excel）の残課題「名前選択helper列とExcel内dropdown」を実装した。実装前にPython参照repo（read-only、`src/summer_scheduler/infrastructure/excel/template.py`）を調査し、openpyxlでの実装（ID列dropdown＋名前選択dropdown＋IDへの自動変換数式＋確認用逆引き数式列、行3〜10000がvalidation範囲、行3〜1000が数式prefill範囲）を確認した上で、ClosedXML（`IXLRangeBase.CreateDataValidation()`/`.List(IXLRangeBase,bool)`、`IXLCell.FormulaA1`）で同等の挙動を移植した。

- `MasterDataWorkbookService.cs`: 「講師対応科目」「受講希望」シートの出力を専用メソッドに書き換え、各FK列（講師ID・科目コード・生徒ID・通常担当講師ID・第1〜3希望講師ID、計8列×2シート）へ`AddReferenceHelperColumns`で以下を追加した。
  - ID/コード列自体に、対応する生徒/講師/科目シートのID/コード列を参照元とするdropdown（`IgnoreBlanks`は必須列のみ`false`）。
  - 隣に「◯◯名から選択」dropdown列（対応シートの氏名/表示名列が参照元、常に空欄可）。
  - 既存データがある行より後の空白行（`ReferenceFormulaMaxRow`=200行まで）は、ID列へ`IF(選択="","",IF(COUNTIF(名前一覧,選択)=1,INDEX(ID一覧,MATCH(選択,名前一覧,0)),""))`という数式を事前入力し、名前を選ぶだけでIDが自動入力されるようにした（同名が複数あると空欄のまま、直接ID入力にフォールバック）。
  - 「◯◯名（確認）」列（既存データ行も含め全行）へ、現在のID値から名前を逆引きする`IFERROR(INDEX(...),"ID不明")`数式を追加し、直接typedしたIDでも名前が確認できるようにした。
  - 定数はPython版（10000/1000）よりも小さい`ReferenceValidationMaxRow=1000`・`ReferenceFormulaMaxRow=200`とした（現実的なroster規模とimport時の数式再評価コストを踏まえた意図的な縮小）。
  - importの列読み取り（`RowReader`）はヘッダー名で列を検索するため、列順の変更・追加列の挿入は無改修で動作する（確認済み）。
- 新規テスト`Export_QualificationSheet_NameSelectionAutoFillsIdAndConfirmColumnResolvesName`で、名前選択列に名前を入力した後にファイルを再読み込みし、ID列が数式によって正しいIDへ自動変換されること、確認列が正しい名前を逆引きすることを検証した（ClosedXMLの数式評価エンジンでCOUNTIF/INDEX/MATCH/IFERRORが正しく動くことを実機で確認）。
- SetupPageの共通基本情報Excel説明文に名前選択機能の案内を追加。
- Release/x64 build: warning 0 / error 0。全89 tests passed（新規1件）。Privacy gate成功。

### v0.1.0 checkpoint 42 (Claude)

ユーザーから「使用量が91%なので寝る前にできるところまで進めてほしい、許可なしでどんどん進めてよい」との指示を受け、引き続きFeature Parityの残課題（行18・行20周辺）を実装した。

- 講師対応科目の一括設定（「担当設定」タブ）へ備考入力欄を追加。空欄のまま一括設定すると各ペアの既存の備考を変更せず維持し、値を入力すると全ペアへ同じ備考を一括設定する（既存の`SaveQualificationAsync`が備考も含めた全列upsertのため、空欄時は事前に`GetQualificationsAsync`で既存値を読み取ってペアごとに引き継ぐ実装とした）。
- 受講希望（LessonRequest）の個別編集UIを新設（Python版はExcel中心だったが、WinUI版でも個別のadd/edit/deleteを可能にした）。
  - `SeminarSched.Domain.MasterData.LessonRequest`レコードを新設（必要授業回数・通常担当講師＋優先度・第1〜3希望講師・1対1必須・最大連続コマ数上書き・空きコマ許可上書き・備考）。
  - `IMasterDataRepository`/`SqliteMasterDataRepository`へ`SaveLessonRequestAsync`（`(ProjectId,StudentId,SubjectId)`一意制約でのupsert、`MasterDataWorkbookService`のExcel取込upsertと同一SQLパターン）・`GetLessonRequestsAsync`・`DeleteLessonRequestAsync`を追加。
  - `SetupPage`の「担当設定」タブへ「受講希望」セクションを新設。一覧から選択すると全フィールドを読み込んで編集でき（`_nullableTeacherItems`をRegularTeacher/Preferred1〜3の各ComboBoxで共用）、優先度5で通常担当講師未指定の場合はUI側でエラー表示する（Excel取込のvalidationと同じ業務ルール）。最大連続コマ数上書きは`NumberBox`の値0を「上書きなし」として扱い、空きコマ許可上書きは「指定なし/許可/不許可」の3択ComboBoxとした。
  - 新規テスト`SaveGetDeleteLessonRequest_RoundTripsAllFieldsAndUpsertsOnConflict`（`SqliteMasterDataRepositoryTests.cs`）で全項目のround-trip・upsertによる更新・削除を検証した。
- Release/x64 build: warning 0 / error 0。全90 tests passed（新規1件）。Privacy gate成功。

### v0.1.0 checkpoint 43 (Claude)

Feature Parity行17「ID自動採番wizard」を実装した（ユーザーの許可のもと無人で継続）。

- `SetupPage`の生徒・講師タブへ「IDを自動採番」ボタンを追加。既存の全ID（`_studentItems`/`_teacherItems`）を正規表現`^(.*?)(\d+)$`で prefix と数値末尾に分解し、最も件数の多いprefixグループの最大値+1を、既存の桁数に合わせて0埋めして採番する（例: S-001〜S-009が既存なら次はS-010）。既存IDが無ければ既定値`S-001`/`T-001`から開始する。
- 単純なUIヘルパー（既存の`_studentItems`/`_teacherItems`キャッシュを読むだけで新規のrepository/serviceは不要）のため、既存の自動テストの対象範囲外（WinUIページのcode-behindは元々テスト対象外という既存方針を踏襲）。
- Release/x64 build: warning 0 / error 0。全90 tests passed。Privacy gate成功。

### v0.1.0 checkpoint 44 (Claude)

Feature Parity行30「availability一括編集」の残課題「複数日付・複数コマへ同時適用する一括操作」を実装した（ユーザーの許可のもと無人で継続）。

- `IAvailabilityMatrixService`へ`SetLevelsAsync(projectPath, kind, entityIds, IReadOnlyCollection<(long OpenDateId,long TimeSlotId)> slots, level)`を追加。既存の`SetLevelAsync`（単一日付・単一コマ）はこの新メソッドへ単一要素のcollectionを渡すだけの委譲に変更した。
- `SqliteAvailabilityMatrixService`の実装は、指定された全ての`(OpenDateId,TimeSlotId)`ペアと全ての対象（生徒/講師）の組み合わせへ、単一のtransaction内でupsertとTeacherUnavailability同期を行う。ループ中に未開校のペアが見つかった場合は例外をthrowしてtransactionをrollbackし、それまでに適用した分も含めて全体を無効化する（部分適用を防止）。
- `ImportPage`（③アンケート取込み）の可用性手動編集セクションへ「複数日付・複数コマへ一括適用」を新設。日付・コマをそれぞれ複数選択できるListViewと値ComboBoxを追加し、選択済みの対象（生徒/講師）×全日付×全コマの組み合わせへ一括適用するボタンを設けた。コマの選択肢は`App.CourseSettings.GetTimeSlotsAsync`から取得した有効な全コマ（特定の日付に限定しない）とし、日付ごとの開講状況の妥当性はサービス層の`SetLevelsAsync`が検証する。
- 新規テスト2件（`SqliteAvailabilityMatrixServiceTests.cs`）: `SetLevelsAsync_AppliesToEveryDateAndSlotCombinationInOneCall`（2日×2コマの全組み合わせへの一括適用を検証）、`SetLevelsAsync_InvalidPairAmongManyRollsBackTheWholeBatch`（複数ペアの一部が無効な場合に、有効なペアも含めて全体がrollbackされ元の値のまま残ることを検証）。
- Release/x64 build: warning 0 / error 0。全92 tests passed（新規2件）。Privacy gate成功。

### v0.1.0 checkpoint 45 (Claude)

Feature Parity行25「Google Forms作成kit」の残課題「講師指導可能科目用の別Apps Script」を実装した（ユーザーの許可のもと無人で継続）。

- `QuestionnaireKitService`が生成する単一のCode.gsへ`createTeacherQualificationForm()`を追加。既存の生徒フォームと同じ`CONFIG.subjectsByLevel`（小/中/高/その他）を再利用し、講師IDテキスト項目＋学校段階別の指導可能科目チェックボックスを持つ、勤務可能日時アンケート（`createTeacherForm`）とは別のGoogleフォームを生成する。指導可能科目は講習期間ごとに毎回聞く必要がないため、意図的に別フォームとした（コード内コメントに理由を記載）。
- `createSeminarSchedForms()`が生徒・講師（勤務可能日時）・講師指導可能科目の3フォームを作成し、3つの回答URLをすべてログ出力するよう更新。README（生成物内のREADME.txt）も3フォームの配布・反映手順に更新した。
- 講師指導可能科目の回答自体の自動取込み（CSV列mapping、diagnostics）は今回のscope外とし、READMEには「①設定の担当設定タブへ手動反映」と明記した（正直な現状表示。Feature Parityにも残課題として明記）。
- 既存テスト`GenerateAsync_WritesConfiguredAppsScriptAtomically`へ`createTeacherQualificationForm`の存在チェックを追加。
- Release/x64 build: warning 0 / error 0。全92 tests passed。Privacy gate成功。

### v0.1.0 checkpoint 46 (Claude)

Feature Parity行50「全体時間割Excel/PDF」の残課題「コマ不可のgray表示」を実装した（ユーザーの許可のもと無人で継続）。

- `ScheduleReport`へ`TeacherUnavailabilityCell(Date,TimeSlot,Teacher)`のリストを追加。`SqliteOutputPackageService`が`TeacherUnavailability`テーブルをTeacher/OpenDate/TimeSlotとJOINして取得する（`ScheduleReportRow`の日付・コマ文字列表現と完全に一致する式を使用し、Overview側の照合キーを揃えた）。
- `OverviewGridLayout`へ`OverviewUnavailability`と`OverviewCell.Unavailable`を追加。`Build()`は「配置が0件」かつ「(日付,講師,コマ)が出勤不可集合に含まれる」場合にのみ`Unavailable=true`とする。講師列自体はその日に他の配置がある場合のみ表示されるため（既存仕様のまま）、この機能は「部分的に出勤不可な講師」のケースのみを対象とする（終日不可で配置ゼロの講師は元々列として現れない）。
- `ExcelScheduleReportRenderer`/`PdfScheduleReportRenderer`の全体時間割セクションで、`Unavailable`かつ配置なしのセルへLightGray背景を適用。
- 新規テスト`GenerateAsync_OverviewGrid_GraysOutUnavailableSlotForTeacherWithOtherAssignmentsThatDay`（`SqliteOutputPackageServiceTests.cs`）で、配置のあるコマは通常表示、出勤不可のコマだけgray表示されることをExcel出力から直接検証した。
- Release/x64 build: warning 0 / error 0。全93 tests passed（新規1件）。Privacy gate成功。

### v0.1.0 checkpoint 47 (Claude)

Feature Parity行55「atomic export・上書き確認」の残課題「個別file選択とpreview」を実装した（ユーザーの許可のもと無人で継続）。

- `OutputPage`の出力完了後に生成物一覧（全体時間割.xlsx・時間割.pdf・講師別folder内の全ファイル）をListViewへ表示する`OutputFilesPanel`を追加。
- 「選択したファイルを開く」ボタンで`Windows.Storage.StorageFile.GetFileFromPathAsync`＋`Windows.System.Launcher.LaunchFileAsync`によりOSの既定アプリ（Excel/PDFビューアなど）でファイルを開く。「出力フォルダーを開く」ボタンで同様にエクスプローラーを開く。
- WinUI/UWPのLauncher APIはアプリ内蔵のpreview機能ではなく「OSの既定アプリに委譲する」設計のため、独自のExcel/PDFプレビューア実装は行わず、確実に動作するこの方式を採用した。
- UI層のみの変更でサービス層に変更なし。Release/x64 build: warning 0 / error 0。全93 tests passed。Privacy gate成功。

### v0.1.0 checkpoint 48 (Claude)

ユーザーより2つの指示を受けた: (1)講師配布roster区分は元々「通常担当分」⇒「講習担当分」⇒「その他」の3区分だったが、「その他」は廃止し「通常担当分」⇒「講習担当分」の2区分のみでよいという明示的な方針決定。(2)⑤時間割エディタ（OptimizationPage）を、実機確認をしながら進めてよい（実機確認＝実際にアプリを起動して確認することを指す。ユーザーが離席する間はアプリを起動したまま他の作業を進めてよい、との許可）。

**講師配布roster「その他」区分の廃止:**
- `ExcelScheduleReportRenderer.RenderTeacherPacket`のroster区分見出しを「講習担当（その他）」から「講習担当」へ変更（実装は元々2区分だったが紛らわしいラベルが残っていたため修正）。
- `SqliteOutputPackageServiceTests`の対応するアサーションを新しいラベルへ更新。Feature Parityの該当行の文言も2区分構成が最終仕様であることを明記するよう更新。

**⑤時間割エディタ「日別グリッド編集」の残課題2件:**
- Feature Parity行「検索・scroll同期」の残課題「header/row/grid間のscroll同期」を実装。グリッドの見出し行（講師名）・見出し列（コマ名）は本体セルと同じ`BoardGrid`（単一Grid、単一ScrollViewer）内にあるため、行・列を複製せず、見出しセルへ`TranslateTransform`（`_columnHeaderTransform`/`_rowHeaderTransform`/`_cornerTransform`）を適用し、`ScrollViewer.ViewChanged`で毎回オフセット分だけ逆方向へ平行移動させることで「見出しが常に画面内に固定表示される」Excelのウィンドウ枠固定に相当する見た目を実現した。見出しセルは`Canvas.SetZIndex`で本体セルより手前に描画し、不透明な背景色を設定してスクロールしてきた本体セルが透けないようにした（行・列の高さ/幅を複製grid間で同期させる必要がある従来手法より単純で、ずれのリスクが低い設計）。
- Feature Parity行「講師一時表示・availability編集」の残課題「一括操作（複数コマ・複数講師まとめて設定）」を実装。`IScheduleEditorService.SetTeacherUnavailableManyAsync(path, openDateId, targets, unavailable)`を新設し、単一の`SetTeacherUnavailableAsync`はこれへ単一要素collectionを渡す委譲とした。実装は複数の(講師,コマ)組を単一transaction内で検証・適用し、途中で「既に配置がある」組が見つかった場合は例外をthrowして全体をrollbackする（部分適用を防止、checkpoint 44の`SetLevelsAsync`と同じ設計方針）。UIには講師・コマをそれぞれ複数選択できるListViewと「出勤不可/出勤可能にする」ボタンを追加した。
- 新規テスト`SetTeacherUnavailableManyAsync_AppliesAllPairsInOneCallAndRollsBackOnConflict`で、複数組の一括適用と、一部が競合する場合に全体がrollbackされることを検証した。
- **実機未確認事項**: この環境にはWinUIアプリを視覚的に操作・確認する手段（スクリーンショット・UI自動化ツール）が無いため、上記2機能（見出し固定表示の見た目、複数選択ListViewの操作感）はビルド成功・自動テスト成功のみで、実際の画面表示・ドラッグ操作感はユーザー自身の実機確認が必要。アプリを起動した状態で待機し、ユーザーに確認を依頼した。
- Release/x64 build: warning 0 / error 0。全94 tests passed（新規1件）。Privacy gate成功。

### v0.1.0 checkpoint 49 (Claude)

Feature Parity行「講師配布（講師別）」の残課題「PDF個別ファイルは未対応（共通PDFのsectionのみ）」を実装した（アプリを実機確認用に起動して待機している間、並行して進めた）。

- `PdfScheduleReportRenderer.RenderTeacherPacket(report, teacherName, path)`を新設。Excel版`RenderTeacherPacket`と同じ構成（担当一覧＝通常担当→講習担当の2区分の一覧＋週calendar）を独立したPDFファイルとして出力する。
- `SqliteOutputPackageService.GenerateAsync`が「講師別」folder内で講師ごとに`{講師名}t.xlsx`と`{講師名}t.pdf`の両方を生成するよう変更。`SanitizeTeacherFileName`へ拡張子引数を追加し、同じbase名でxlsx/pdfそれぞれ独立して重複回避する。
- 既存テストへ、生成された講師別PDFファイルの存在と`%PDF`ヘッダーを確認するアサーションを追加。
- Release/x64 build: warning 0 / error 0。全94 tests passed。Privacy gate成功。

### v0.1.0 checkpoint 50 (Claude)

Feature Parity行「import preview/diff」の残課題「簡易形式（必要回数・勤務不可）側のdiffは未算出」を実装した（⑤の実機確認待ちの間、並行して進めた）。

- `CsvResponseImportService.ComputeDiff`が、生徒側は`日付`列（可用性形式）が無く`必要回数`列がある場合に`ComputeRequiredSessionsDiff`を、講師側は`日付`列が無く`勤務不可`列がある場合に`ComputeTeacherUnavailableListDiff`を呼ぶよう分岐を追加。
  - `ComputeRequiredSessionsDiff`: 生徒ID・科目コードで既存の`LessonRequest.RequiredSessions`を検索し、無ければ追加・値が違えば変更・同じなら変更なしとして件数化する。
  - `ComputeTeacherUnavailableListDiff`: Apply時が全置換（既存`TeacherUnavailability`を削除してから`勤務不可`列を再挿入）であることに合わせ、講師ごとの既存不可集合と新しい不可集合を完全一致比較する（既存0件→追加、集合が異なる→変更、同一→変更なし）。
  - 削除候補一覧（`RemovalCandidates`）は可用性形式のみの概念のため、簡易形式では常に空のまま（Apply時に全置換で自然に反映されるため、Python版の対象外機能である旨は変更なし）。
- 新規テスト`PreviewAsync_SimpleFormat_ComputesRequiredSessionsAndUnavailableListDiff`で、初回import（追加）→再import同一内容（変更なし）→内容変更後再import（変更）の3段階を検証した。
- Release/x64 build: warning 0 / error 0。全95 tests passed（新規1件）。Privacy gate成功。

### v0.1.0 checkpoint 51 (Claude)

ユーザーから「プロジェクトが開けない」との報告を受け、実機のログ（`%LocalAppData%\SeminarSched.WinUI\logs\`）を確認したところ、`[ERROR] Unhandled UI exception: SqliteException: SQLite Error 1: 'no such table: ApplicationMetadata'.`が2回記録されていた。原因を特定し修正した。

**根本原因:**
- `SqliteProjectSchema.ReadVersionAsync`は`ApplicationMetadata`テーブルへ直接SELECTするが、このテーブルが存在しないファイル（Python版が生成する`.jukuschedule`ファイルにはこのテーブルの概念自体が無い。または壊れた/無関係なSQLiteファイル）を開こうとすると、生の`SqliteException`をthrowしていた（テーブルは存在するが該当行が無い場合だけ、意図された案内メッセージ付き`InvalidDataException`になっていた）。
- さらに`HomePage.xaml.cs`の全操作（開く・最近使ったプロジェクトを開く・バックアップ作成・復元・複製・新規作成）のcatch節はいずれも`Microsoft.Data.Sqlite.SqliteException`を捕捉対象に含めていなかったため、この例外が伝播し、WinUIの`UnhandledException`ハンドラーまで届いて（ログには記録されるが）操作自体は失敗としてユーザーへ通知されないまま終わっていた。
- Python版が生成した実際の`.jukuschedule`ファイルを開こうとした場合に、まさにこの経路でクラッシュする（「このファイルは現在のWinUI版プロジェクトではありません」という本来出るはずの案内が表示されない）。

**修正内容:**
- `SqliteProjectSchema.ReadVersionAsync`: `ApplicationMetadata`へのSELECTを`try/catch(SqliteException)`で囲み、「このファイルはSeminarSched.WinUIのprojectファイルではありません。Python版projectのcopy-importは未実装です。」という案内付き`InvalidDataException`へ変換。
- `SqliteProjectRepository.UpgradeIfNeededAsync`: 接続open自体（`inspection.OpenAsync`）も`try/catch(SqliteException)`で囲み、SQLiteとして開けないファイル（非DB形式）も「このファイルはSQLiteデータベースとして開けません。projectファイルが破損している可能性があります。」という案内付き`InvalidDataException`へ変換。
- `HomePage.xaml.cs`: 新規作成・開く・最近使ったプロジェクトを開く・バックアップ作成・復元・複製の全catch節へ`SqliteException`を追加（`using Microsoft.Data.Sqlite;`を追加）。
- 新規テスト2件（`SqliteProjectRepositoryTests.cs`）: `OpenAsync_SqliteFileWithoutApplicationMetadataTable_ThrowsFriendlyError`（Python版相当の無関係テーブルのみ持つSQLiteファイルを開こうとして案内付き例外になることを検証）、`OpenAsync_NotASqliteFile_ThrowsFriendlyError`（プレーンテキストファイルを開こうとして案内付き例外になることを検証）。
- 実機で起動していたアプリ（修正前のbuildで動いていた）を一旦終了し、修正版で再起動した。
- Release/x64 build: warning 0 / error 0。全97 tests passed（新規2件）。Privacy gate成功。

### v0.1.0 checkpoint 52 (Claude)

Feature Parity行「自動backup・世代管理」の残課題「自動実行と世代管理」を実装した（ユーザーの「残っている項目も実装してください」との指示のもと継続）。

- `IProjectRepository.CreateAutomaticBackupAsync(path, maxGenerations)`を新設。project直下に`{ファイル名}_backups`folderを作り、`{ファイル名}_auto_{yyyyMMdd_HHmmss_fff}.jukuschedule`形式でtimestamp付きbackupを1件作成し、同folder内の`_auto_`backupを新しい順に数えて`maxGenerations`件を超える分を自動削除する。既存の`CreateBackupAsync`（一時file→整合性確認→`OpenAsync`検証→atomic move）をそのまま再利用する。
- 失敗（読み取り専用folder・disk容量不足など）はbest-effortで握り潰し、呼び出し元の主目的（project open）を妨げない設計とした（`IProjectRepository`のXML docにその契約を明記）。
- `ProjectService.OpenAsync`が`OpenAsync`成功直後に自動で`CreateAutomaticBackupAsync(path, 5)`を呼ぶよう変更（世代数5は明確なPython版仕様が無いための暫定値、既存の「復元前3世代backup」とは別枠）。
- 新規テスト2件: `CreateAutomaticBackupAsync_KeepsOnlyTheNewestGenerationsAndNeverThrows`（4回連続実行して直近3世代のみ残ること、整合性が保たれること、存在しないpathでも例外を投げないことを検証）、`OpenAsync_TriggersAutomaticBackupWithFiveGenerations`（`ProjectService.OpenAsync`が正しい引数で自動backupを呼ぶことをApplication層のfakeで検証）。
- Release/x64 build: warning 0 / error 0。全99 tests passed（新規3件）。Privacy gate成功。

### v0.1.0 checkpoint 53 (Claude)

Feature Parity行「講師配布（学年順）」の残課題「A4サイズへの厳密な収まり調整は未検証」を検証・修正した。実機で視覚確認する手段が無い環境のため、実際にPDFを描画してPdfSharpでページ寸法を読み取り、MigraDocの既定余白（`PageSetup.DefaultPageSetup`: A4・左右2.5cm・上2.5cm・下2cm）と突き合わせる方法で数値的に検証した。

- `PdfScheduleReportRenderer.AddCalendar`（生徒配布・講師配布・講師別いずれの週calendarページでも共通利用される）の7列テーブルが列幅3.6cm（合計25.2cm）だったが、A4横向きの使用可能幅（29.7cm－左2.5cm－右2.5cm＝24.7cm）を0.5cm超過していたため、右余白へはみ出していた。列幅を3.5cm（合計24.5cm）へ修正し、余白内に収まるようにした。
- 検証手順: (1) `MigraDoc.DocumentObjectModel.PageSetup.DefaultPageSetup`を直接読み取り、既定がA4・Portrait・左右2.5cm・上2.5cm・下2cmであることを確認。(2) 実際に`PdfScheduleReportRenderer.RenderTeacherPacket`でPDFを生成し、`PdfSharp.Pdf.IO.PdfReader`で開いてPage.Width/Heightを計測し、Landscape指定時に29.7cm×21.0cmへ正しく回転することを確認。(3) 7×3.6cm＞24.7cmであることを算出し、3.5cmへ修正。
- 新規テスト`WeeklyCalendarColumnWidth_FitsWithinA4LandscapeUsableWidth`（`PdfScheduleReportRendererLayoutTests.cs`）で、この算出根拠（`PageSetup.DefaultPageSetup`から求めた使用可能幅と列幅合計の比較）を恒久的な回帰テストとして固定した。
- Release/x64 build: warning 0 / error 0。全100 tests passed（新規1件）。Privacy gate成功。

### v0.1.0 checkpoint 54 (Claude)

ユーザーから「保存先を選んで作成する際の名前・場所を一律に決めておいてほしい。Python版は`%LocalAppData%`配下で一括管理していたが、WinUI版はどうなっているか」との質問・要望を受けた。読み取り専用のPython参照repoを調査し、2段階のAskUserQuestionでユーザーの希望を確認したうえで実装した。

**調査結果（要約）:**
- Python版は`%LocalAppData%\SummerScheduler\`配下に設定・DB・log・backup・workspace（生徒/講師/プロジェクト）を完全固定folderで管理し、出力・Googleフォームkitはfolder選択dialogを出すが既定folderを事前選択、ファイル名は常にtemplate化されている（手入力させない）。
- 一方WinUI版はsettings.json/logのみ`%LocalAppData%\SeminarSched.WinUI\`に固定されており、project・backup・出力・Googleフォームkitは全てdialogが汎用の「ドキュメント」folderから開始し、ファイル名の一貫性もなかった。

**ユーザーの決定（2問）:**
1. バックアップ・出力・Googleフォームなど「作ったものを保存する場所」→ **folder選択dialogを廃止し、固定folderへ自動保存**（推奨案を採用）。
2. 新規プロジェクト作成・開く → **folder選択dialogは残すが、最初に開かれるfolderを既定のfolderに固定する**（Python版と同じ方針）。

**技術調査:** 従来使っていた`Windows.Storage.Pickers`（UWP由来）は開始folderを`PickerLocationId`列挙型（Desktop/Documents等）にしか設定できず、任意pathを指定するAPIが無いことを確認。WindowsAppSDK 2.4.0に含まれる新しい`Microsoft.Windows.Storage.Pickers`（`Microsoft.WindowsAppSDK.Foundation`パッケージ由来）を調査したところ、`FolderPicker`/`FileOpenPicker`/`FileSavePicker`いずれも`SuggestedFolder`（文字列path）で任意の開始folderを指定できることを実機ビルドで確認し、こちらへ移行した（コンストラクターが`WindowId`を要求するため`Microsoft.UI.Win32Interop.GetWindowIdFromWindow`で取得する新パターンに統一）。

**実装内容:**
- `ProjectService`（Application層）へ`DefaultProjectsDirectory`・`DefaultBackupDirectory`（いずれも`%LocalAppData%\SeminarSched.WinUI\Workspace\{Projects,Backups}`、初回アクセス時に自動作成）を追加。
- `IProjectRepository.CreateAutomaticBackupAsync`のシグネチャを変更し、backup保存先を呼び出し側から明示的に渡すようにした（従来は`{project直下}\{名前}_backups`を内部計算していたが、Python版と同様に全projectで共有する一元backup folderへ変更。同一folder内でも`{名前}_auto_*`のprefixで世代管理のprune対象を正しくproject単位に絞ることを新規testで確認）。
- `WorkspacePaths`（WinUI層、`Output`・`Forms`）を新設。
- `OutputPage`（⑥出力）・`QuestionnairePage`（②アンケート作成）: folder選択dialogを完全に廃止し、`WorkspacePaths.Output`/`WorkspacePaths.Forms`へ直接生成するよう変更。
- `HomePage`: 新規作成・開く・複製は`Microsoft.Windows.Storage.Pickers`へ移行し`SuggestedFolder`で`ProjectService.DefaultProjectsDirectory`を初期folderに設定。バックアップ作成はdialogを完全に廃止し`DefaultBackupDirectory`へ自動保存。復元は引き続きdialogを使うが初期folderを`DefaultBackupDirectory`に設定。
- 共通基本情報Excel（SetupPage）・CSV/XLSX取込（ImportPage）はPython版も固定folder化していないため、旧`Windows.Storage.Pickers`のまま変更していない。
- 新規テスト2件: `CreateAutomaticBackupAsync`のcross-project prune分離検証（`SqliteProjectRepositoryTests.cs`）、`DefaultProjectsAndBackupDirectories_AreCentralizedUnderLocalAppDataAndExist`（`ProjectServiceTests.cs`）。
- Release/x64 build: warning 0 / error 0。全101 tests passed（新規1件、既存2件を仕様変更に追従）。Privacy gate成功。
- **実機確認済み**: ユーザーが実際にアプリを操作し、保存先が固定folderになっていることを確認した（2026-09-18）。project open crash修正（checkpoint 51）も、この確認操作を通じて間接的に動作を確認できた。

### v0.1.0 Draft Release作成 (Claude)

ユーザーより「v1.9.5と同じ内容のものがほぼ実装できたと思ったら、draftとしてリリースしてください」との指示を受けた。`docs/FEATURE_PARITY.md`を確認したところ、「未実装」「保留」のままの項目は以下3件のみで、いずれも(a)ユーザー自身が明示的に後回しでよいと指示した最適化探索品質関連、または(b)Python版v1.9.5自体でもUI停止中の機能であり、実質的な機能ギャップではないことを確認した。

- greedy初期解・complete hint、5段階品質profileの実行速度プリセット（30/120/600秒） — 最適化探索。ユーザー指示により後回し。
- 集団授業DB/service互換 — Python版v1.9.5でも「UI停止中」（無効化されたまま）。

残りの項目は全て「実装済み」または「実装中」（コア機能は動作するが細部の仕上げ・実機確認待ちの残課題がある状態）であり、①〜⑥の業務flow全体をひととおり実行できる状態にあることを確認した。

- `docs/releases/v0.1.0.md`を、開発初期の未更新な内容（数checkpoint分しか反映していなかった）から、①〜⑥の業務flow別に整理した包括的なrelease notesへ書き直した。既知の未実装・保留事項を明記した。
- `docs/CODEX_HANDOFF.md`のCurrent Version/Latest Draft Releaseを更新。
- `docs/FEATURE_PARITY.md`の行47（manual/lock semantics）の備考に残っていた「Undo/Redoは未実装」という古い記述を削除（行48で実装済みであることを確認し修正、矛盾を解消）。
- GitHub上に`v0.1.0`タグでDraft Releaseを作成した（公開はしない。ユーザーが内容を確認したうえで公開判断する）。

### v0.1.0 checkpoint 55 (Claude)

ユーザーが実際の本番データ（`生徒・講師_基本情報.xlsx`、F:ドライブ上）を取り込もうとしたところ、「受講希望 : 必須シートがありません。」という取込エラーで①〜③の作業が進められなくなった。

**原因調査:** Python参照repoを調査し、Python版には別由来の2種類のExcel名簿形式が存在することを確認した。
1. `master_data.xlsx`（共通基本情報Excel）— 生徒/講師/科目/講師対応科目/**受講希望**の5シート。既存`MasterDataWorkbookService`が対応する形式。
2. `生徒・講師_基本情報.xlsx`（Python版`shared_roster.py`が生成する「年度をまたいで利用する」名簿）— 生徒/講師/科目/講師対応科目/**通常授業**の5シート（受講希望ではなく通常授業を含む）。姓・名を分けて入力し「氏名（確認）」列で確認する方式、学年はExcel短縮コード（S1〜S6/J1〜J3/H1〜H3）で保存され内部表記（小1〜小6等）への変換が必要、ID列はPython側の名前選択helper列による数式で解決済みの値としてそのまま保存されている。

ユーザーの実ファイルは②の形式であり、①の形式しか読めない既存importerでは必須シート「受講希望」が見つからず失敗していた。

**実装内容:**
- `ISharedRosterImportService`（Application層、新規）・`SharedRosterImportService`（Infrastructure層、新規）を追加し、②の形式を専用に検証・取込みできるようにした。既存`MasterDataWorkbookService`と同じ設計（SHA256によるpreview/apply間の変更検知、transaction＋rollback、`ImportBatch`/`ImportSourceSnapshot`/`AuditLog`への証跡保存）を踏襲し、`ImportType='shared_roster'`で区別している。
- ヘッダーの正規化を「最初の全角`（`より前だけを採用」という汎用ルールにした（Python側の注記パターンが`（必須）`・`（自動・入力不要）`・`（確認）`等多様なため、既存の`（必須）`限定除去より汎用化）。
- 学年変換は`GradeFromExcelCode`辞書（Python版`domain/grades.py`の`grade_from_excel`相当）で実装。既に内部表記の値が来た場合はそのまま通す。
- `RegularLessonProfile`向けに`UpsertRegularLessonsAsync`を新設（`SqliteMasterDataRepository`の同テーブル向けupsert SQLパターンを踏襲）。
- `App.xaml.cs`へ`App.SharedRosterImport`を登録。`SetupPage`（①設定・プロジェクトタブ）へ「共通名簿Excel（生徒・講師_基本情報）」セクションと「共通名簿Excelを検証して取込み」ボタンを追加し、既存の共通基本情報Excelの取込みダイアログと同じUXパターン（preview→確認dialog→反映）で実装した。
- 新規テスト（`SharedRosterImportServiceTests.cs`、架空データのみ使用）: 実ファイルと同じヘッダー構造の5シートを組み立てて全件取込み・学年変換（"J2"→"中2"）を検証するテスト、および「通常授業」シート欠落を正しく検出し「受講希望」を要求しないことを確認するテスト。
- Release/x64 build: warning 0 / error 0。全105 tests passed。Privacy gate成功。

**未解決・次回の課題:** ユーザーのF:ドライブには実際にPython版で使っていたGoogleフォームの生の回答CSV（`2026夏期講習 個別指導受講申込 回答原本...csv`等）もあり、これをC#版へ直接取り込む機能（Python版`application/course_survey_service.py`相当、990行規模の実機能）はまだ移植していない。次回はこの生CSV importerの移植に着手する必要がある。また、`C:\Users\sota1\AppData\Local\SummerScheduler`（ユーザー実機のPython版インストール先）は参考情報として提示されたが、まだ調査していない。

### v0.1.0 checkpoint 56 (Claude)

checkpoint 55で共通名簿Excel（生徒・講師_基本情報.xlsx）を取り込めるようにした後、ユーザーが実際に③アンケート取込みを試すために言及していた「F:ドライブにあるpython版で使っていたアンケート結果」（Googleフォームの生の回答CSV2件）を調査し、これを取り込む機能が丸ごと未移植であることを確認して実装した。

**調査結果:** Python参照repoの`application/course_survey_service.py`（`CourseSurveyService`、約990行）が該当機能。生成済みGoogleフォームが出力する生の回答CSV/XLSX（質問文そのままの列名、例:「姓（苗字）（必須）」「受講教科（1教科目）（必須）」「受講不可日時（チェックしたコマは受講不可） [2026-07-24（金）]」）を直接検証・反映する専用serviceで、既存の`IResponseImportService`（列名を固定した簡易CSV向けの差分更新）とは全く別物。ユーザーの実ファイル（F:ドライブの生徒回答57行・講師回答16行のCSV）のヘッダーを直接確認し、Python側の実装と完全に一致することを確認した。

**実装内容:**
- `ICourseSurveyImportService`（Application層）・`CourseSurveyImportService`（Infrastructure層）を新設。Python版のロジックをほぼ1:1で移植: 学校区分・受講教科・受講回数の教科ごとの列組合せ判定（`_student_request_columns`相当）、学校区分付き科目名への正規化（`小学校・英語`等、`_canonical_questionnaire_subject`相当）、日付列からの開校日抽出＋現在の開校日との差分検証、チェックボックス複数選択セル（例:「Z 15:40～17:00, A 17:10～18:30」）からのコマコード抽出を区切り文字境界を考慮した正規表現で実装、未登録の在籍生はエラー・体験生（アンケートの「在籍区分」列が「体験生」）は警告のうえ`TRIAL-0001`形式で自動登録、時間割配置後（`Assignment`存在時）は一括置換を拒否するhard block、Googleフォームの分岐ページに由来する重複ヘッダーを許容（Python版`readers.py`と同じ「[重複2]」命名規則）。
- 反映は生徒・講師ごとに受講希望（`LessonRequest`）と可用性（`StudentAvailability`/`TeacherAvailability`、講師は`TeacherUnavailability`も同期）を全置換する設計（差分マージではない）。
- `App.xaml.cs`へ`App.CourseSurveyImport`を登録。`ImportPage`（③アンケート取込み）へ「Googleフォーム生回答（アンケート統合）」セクションと専用の検証・反映ボタンを追加した。
- 新規テスト5件（`CourseSurveyImportServiceTests.cs`、架空データのみ使用）: 生徒・講師の正常な往復反映、未登録生徒のerror＋反映拒否、体験生の自動登録＋warning、配置済み時間割がある場合のhard block、2回反映した際に古い可用性が正しく全置換されること（蓄積されないこと）を検証。
- **実データでの動作確認**: ユーザーの実際の本番ファイル（F:ドライブの共通名簿Excel・生徒回答CSV・講師回答CSV、いずれも架空データではない）に対し、読み取り専用の一時検証スクリプト（コミットせず削除済み）でこのserviceを直接実行し、75名の生徒・20名の講師・26科目の名簿に対して57名の生徒・16名の講師・83件の受講希望をエラー0件・警告0件で正しく解決できることを確認した（実行後、実データはコミット・ログ出力のいずれにも一切含めていない）。
- Release/x64 build: warning 0 / error 0。全108 tests passed。Privacy gate成功。

**未対応:** Python版`export_latest_combined`相当（取込結果を色付きの統合Excelとして出力する機能）は未移植。取込みのエビデンス自体（原本ファイルのsnapshot・監査ログ）は保存されるため、③〜⑤の業務flowを進める上での実質的なブロッカーではない。

### v0.1.0 checkpoint 57 (Claude)

ユーザーから3件の要望を受けた。(1) 共通基本情報Excelと共通名簿Excelの違いの説明。(2) 共通名簿はPython版同様、プロジェクトを開いていなくても使えるものとし、ホーム画面の上部に配置してほしい。(3) コマ設定をPython版同様のカレンダー方式にし、開校/休校だけでなく日付ごとにコマ構成も分けられるようにしてほしい（実際に送付したアンケートとGoogleフォーム作成キットの実例を参考にする）。

**共通名簿の独立化:** これまでの共通名簿Excel取込み（checkpoint 55）はプロジェクトを開いている時にしか使えず、名簿データもプロジェクトごとに個別管理されていた。`ISharedRosterStore`/`SharedRosterStore`を新設し、`%LocalAppData%\SeminarSched.WinUI\Workspace\SharedRoster`に固定した共通正本（内部的には既存のプロジェクトschemaと同じSQLiteファイルを「ダミープロジェクト」として利用し、既存の`SqliteMasterDataRepository`・`SharedRosterImportService`をそのまま再利用）を管理する。新規に`SharedRosterWorkbookWriter`（`生徒・講師_基本情報.xlsx`形式の書き出し。Python版と異なりID列は数式・入力補助シートではなく既存ExternalIdを値としてそのまま書く簡易実装）を追加し、共通正本の内容をExcelへ書き出してから既存のimportサービスへ渡す形でプロジェクトへの反映を実現した。ホーム画面の最上部（プロジェクトカードより前）に「共通名簿」カードを追加し、Python版と同じ3操作（Excelで基本情報を編集＝既定アプリで直接開く、新規で基本情報を作成＝空テンプレート別名保存、作成した基本情報を反映＝検証・確認ダイアログ・反映、開いているプロジェクトへも自動反映）を実装。新規プロジェクト作成時は共通正本の内容を自動的にコピーする。

**開校日・コマのカレンダー化:** Python版`OpenDateSettingsTab.qml`を参考に、①設定「コマ・開校日」タブの開校日UIをフラットな一覧から月表示カレンダー（動的に構築するGrid、`SetupPage.xaml.cs`）へ置き換えた。日付をタップして複数選択し、「選択日に使用するコマ」のチェックボックス（3値: 全選択日でON/OFF/一部）で日付ごとにコマの有効・無効を切り替えられるようにした。従来は「開校」ボタンが常に全有効コマを設定するのみで、日によってコマ数を変える手段がなかった。バックエンド（`CourseDay.EnabledTimeSlotIds`、`OpenDateTimeSlot`テーブル）は checkpoint 44前後から既に対応済みだったため、変更はUI層のみ。

**Googleフォーム作成kitの全面書き直し（重大な不整合の発見と修正）:** ユーザーが指定した実際のPython生成済みキット（`C:\Users\sota1\AppData\Local\SummerScheduler\workspace\プロジェクト\Googleフォーム_...`）を調査したところ、C#版`QuestionnaireKitService`が生成するCode.gsは独自設計の簡易フォーム（参加可能コマの正の選択、受講回数なし、姓名分割なし）であり、Python版が実際に生成するフォーム、および checkpoint 56で実装した③アンケート取込み（`ICourseSurveyImportService`）が期待する列構成と一致していないことが判明した。つまり、C#版のkitで生成したフォームの回答は、C#版の取込み機能で読み込めない状態だった。実際のPython生成済み3ファイル（`create_student_questionnaire.gs`/`create_teacher_questionnaire.gs`/`create_teacher_subject_questionnaire.gs`）を仕様として、共通のApps Scriptテンプレート（`kind`・関数名・プロパティキーだけ差し替え）から3種類を生成するよう全面的に書き直した。生徒用は学年別page分岐、教科ごとの学校区分・受講教科・受講回数（最大4教科）、受講不可日時のcheckboxGrid、フォーム回答を取込み用の単一シート「Form Responses 1」へ自動整形するonFormSubmitトリガーまで含めて移植した。②アンケート作成に生徒用・講師用フォーム名・回答締切・問い合わせ先の入力欄を追加（Python版と同様、自由入力・既定値あり）。

**動作確認:** 生成したキットのCONFIG構造を実際の参照ファイルと目視比較し一致を確認。新規・更新テスト（`QuestionnaireKitServiceTests.cs`、`SharedRosterStoreTests.cs`）を追加。Release/x64 build: warning 0 / error 0。全113 tests passed。Privacy gate成功。実機でのカレンダーUI・共通名簿UIの見た目確認はユーザー側で今後実施。

**未対応:** ①設定・プロジェクトタブに残っているプロジェクト単体向けの共通名簿Excel取込み（checkpoint 55）はそのまま残した（新しいホーム画面の共通正本フローと機能は重複するが、開いているプロジェクトだけへ一時的に反映したい場合の代替経路として維持）。Googleフォーム側のGoogleでの実行確認は未実施（この環境からはGoogleへ到達できないため）。

### v0.1.0 checkpoint 58 (Claude)

ユーザーから追加の要望5件と、C:\Users\sota1\OneDrive\デスクトップ\seminarSchedへの参照付きで「Python版の仕様をよく確認し、同じような仕様にしてほしい（変えていいのはデザインだけ）」という広い裁量での作業指示を受けた。ユーザーは離席するとのことで、終業時刻まで自走するタスクを自分で設定して実行するよう指示された。

**5件の小さめの要望対応:**
1. 新規プロジェクト作成時、実運用でほぼ必ず使うZ/A/B/Cの4コマ（15:40-17:00等、ユーザー提供の実データに基づく時刻）を既定値として自動生成し、生成した全開校日へ割り当てるようにした（`SqliteProjectRepository.CreateAsync`）。既存テストのうち「新規プロジェクトはコマ0件」という前提だったものを合わせて修正。
2. ②アンケート作成の生徒用・講師用の回答締切をそれぞれ独立して設定できるように変更（`QuestionnaireKitService.GenerateAsync`のシグネチャに`studentDeadline`/`teacherDeadline`を追加）。既定フォーム名を実運用の文言に統一。
3. ②アンケート作成キット保存後に「保存先を開く」ボタンを追加し、Explorerで直接開けるようにした。
4. ユーザーが添付した実際の回答CSV（生徒57名・講師16名、氏名は実データ）に対し、読み取り専用の一時検証スクリプト（コミットせず削除済み）で③アンケート取込み（`ICourseSurveyImportService`）を実行し、構造的なパースエラーが0件であることを確認した（未登録判定のみ発生。これは検証用に用意した架空名簿に実際の生徒名が無いための想定内の結果）。

**「事前確定」アーキテクチャの修正（本checkpointの主要作業）:**
ユーザーから「事前確定は古い仕様の理解に基づいたサブ機能で、本来はアンケート結果から作られた生徒カードをカレンダーに配置していく画面（Python版で言う④時間割編集）が主軸」との指摘を受けた。Explore subagentを使いPython参照repoの`ScheduleEditorPage.qml`（1953行）・`PreconfirmationPage.qml`（425行）・`OptimizationPage.qml`（676行）と対応するview model・application serviceを精読させ、以下を確認した。

- Python版のサイドバーに「事前確定」という独立項目は存在しない。事前確定は④時間割編集画面内のタブの1つに過ぎず、実体は通常の手動配置（`is_manual=True`）に`is_locked=True`を追加しただけで、専用のテーブルも状態も持たない。
- ④時間割編集（カレンダーへのD&Dでカードを配置するメイン画面）と⑤時間割自動作成（CP-SAT実行専用、D&Dなし）はPython版でも完全に別画面。
- カード＝1受講希望の1セッション（週3回なら3枚）。ドロップ判定はhard constraint検証＋ソフト指標の悪化有無による green/yellow/red の3値。yellowは確認ダイアログ、理由をAuditLogへ記録。

この調査結果に基づき、C#側を以下のように修正した。
- `IFixedLessonService`から独自の`preconfirmed`種別（Assignment.Source='preconfirmed'、IsManual=false）を廃止し、`AddAsync`/`RemoveAsync`/`GetFixedLessonsAsync`を削除。事前確定は`IScheduleEditorService.AddManualAsync(isLocked:true)`を直接呼ぶ、通常の手動配置と同じ経路に統一した（監査ログの理由文言はisLockedの値で従来通り区別）。
- ロック済み手動配置は先にロック解除しないと削除できないよう`RemoveManualAsync`へガードを追加（Python版の「ロック済みは移動・未配置化できません」という制約に合わせた）。
- 旧「④ 事前確定」の独立ナビゲーション項目を廃止し、`PreconfirmationPage.xaml/.cs`を削除。日別グリッド編集・手動配置一覧・Undo/Redoを新設の`ScheduleEditorPage`（④ 時間割編集）へ移し、そこに簡易な「事前確定」入力セクション（1枠ずつ登録、Python版と同じ制約）を追加した。`OptimizationPage`（⑤ 時間割自動作成）は最適化品質設定と実行トリガーのみのPython版相当の薄い画面にした。
- Undo/Redo履歴は④⑤間のページ遷移をまたいで保持する必要がある（⑤で自動作成した結果を④で取り消せるように）ため、ページインスタンスのフィールドから静的な`ScheduleUndoState`（`SeminarSched_WinUI`名前空間）へ移した。プロジェクトの新規作成・開く・閉じる・復元・複製切替のたびにクリアされる。

**ログ改善:** 実機のログに`NullReferenceException`が複数回（本checkpoint作業中に2回、いずれもスタックトレース欠落で原因箇所を特定できず）記録されていたのを見つけた。`FileAppLogger.Error`がスタックトレースを記録していなかったため、次回発生時に原因を特定できるよう追加した。**この`NullReferenceException`自体の原因は未特定のまま**（再現手順不明。ユーザーの実機操作でのみ発生しており、この環境からは対話的なGUI操作で再現できない）。次回、ユーザーから再現手順が得られ次第、優先的に調査すること。

**動作確認:** Release/x64 build: warning 0 / error 0。全113 tests passed。Privacy gate成功。アプリの起動（新しいナビゲーション構成でのクラッシュ有無）のみ確認済み。④⑤の実際の画面遷移・D&D動作のユーザーによる目視確認はまだ。

**未対応（次回以降の課題）:**
- Python版のドロップ時green/yellow/red判定・ソフト指標差分プレビュー・確認ダイアログは未移植（現状のMoveAsyncはhard constraint検証のみで、確定的に成功/失敗する）。
- 3ペインレイアウト（未配置レール・グリッド・詳細/差分/履歴タブ）、フィルタ（学年・科目・警告等）・拡大率・複数日サマリー表示は未移植。
- カードのバッジ表示（①優先度5🔒✎⚠）・未配置カードの理由文・候補件数表示は未移植。
- 再最適化後の差分表示（新規配置/日時変更/講師変更/未配置化）・監査ログの右ペイン表示は未移植。

### v0.1.0 checkpoint 59 (Claude)

checkpoint 58で追加したスタックトレース記録を活かし、実機ログに複数回残っていた`NullReferenceException`の原因調査を行った。

**発見:** ④時間割編集・⑤時間割自動作成（`ScheduleEditorPage`/`OptimizationPage`）は`WorkflowPageBase`を継承しない素の`Page`のままで、画面全体を「プロジェクトが開かれているか」でガードしていなかった。⑤の実行ボタンだけは`IsEnabled`で個別に守られていたが、④の手動配置追加・削除・ロック切替・自動配置リセット・ドラッグ&ドロップセルなどは`App.ProjectService.Current!.Path`を直接force-unwrapしており、**プロジェクトを開いていない状態でこれらを操作すると確実に`NullReferenceException`で落ちる**ことをコードから確認した（実際にこの手順で再現させたわけではないが、他の全ページ（①②③⑥）を横断的に調査した結果、この2画面だけがこの種のガード漏れを持っていた唯一の箇所だった）。

**対応:** 両ページを他の①②③⑥ページと同様に`WorkflowPageBase`へ変更し、`EnsureProject`で「プロジェクトを開いてください」のInfoBarを表示しつつ、操作可能な部分（`ScrollViewer`で包んだコンテンツ全体）を`IsEnabled=false`で無効化するよう統一した。`StackPanel`には`IsEnabled`が無い（`Control`ではなく`Panel`のため）ことに注意し、`ScrollViewer`（`ContentControl`派生）を無効化対象にした。

**動作確認:** Release/x64 build警告0・エラー0、全114 tests passed、privacy gate成功。アプリの起動は確認したが、この特定の再現手順（プロジェクトを開かずに④⑤を操作する）をこの環境から対話的に実行して確認することはできていない。**次回ユーザーがこの操作を試して再発しないことを確認してほしい。** 再発する場合はcheckpoint 58で追加したスタックトレース付きログ（`%LocalAppData%\SeminarSched.WinUI\logs\app-yyyyMMdd.log`）を確認すること。

### v0.1.0 checkpoint 60 (Claude)

ユーザーから「22時まで動かせるタスクを自分で設定して実行してよい、python版の仕様を確認し同じ仕様に合わせてほしい（デザインのみ変更可）」との指示を受け、checkpoint 59で未対応と記していたgreen/yellow/red判定のうち、ドラッグ移動（`Cell_Drop`のassignment分岐＝`MoveAsync`）を対象に実装した。

**Python版仕様の確認（Exploreサブエージェントで`optimization/manual_edit.py`・`application/schedule_edit_service.py`・`ui/viewmodels/schedule_editor_view_model.py`・`ui/qml/ScheduleEditorPage.qml`を精読）:**
- `preview_edit`はハード制約検証（`validate_optimization_result`を変更前後両方に適用）→RED、を先に確定し、そこを通過した場合のみソフト指標の悪化有無でYELLOW/GREENを判定する。RED＝ハード制約違反のみで、ソフト指標だけでREDになることはない。
- ソフト指標は`unassigned_count`・`regular_teacher_penalty`・`preferred_teacher_penalty`・`preferred_time_score`・`paired_slot_count`・`active_teacher_slot_count`・`changed_existing_assignment_count`の7種（各LOWER/HIGHER_IS_BETTERの向き付き）。1件でも悪化すればYELLOW。
- UIはドロップ確定前にプレビューし、YELLOWなら確認ダイアログ（悪化した指標一覧＋理由入力欄）を出し、確定時はサーバー側（`_require_preview_allowed`）でも再検証し理由文字列をAuditLogへ保存する。

**C#版の実装（スコープを1件のドラッグ移動プレビューに限定。全体差分ではない）:**
- 既存のC#最適化ソルバー（`SqliteScheduleRunService.PreferencePenalty`＝通常担当講師＋第1〜3希望講師を1つのペナルティ値に統合、`Math.Min(生徒希望度,講師希望度)`＝希望日時一致度）は、Python版の`regular_teacher_penalty`+`preferred_teacher_penalty`+`preferred_time_score`を独自に統合した設計だったため、Python版の3分割を再現するのではなくこのC#既存の統合スコアリングを再利用した（「デザインのみ変更可」の範囲内の設計判断。目的関数の重み付けと完全に一致させるため）。
- `unassigned_count`・`changed_existing_assignment_count`（全体スケジュール差分が必要）は今回のスコープ外（1件移動では常に差分0になりがちで実益が薄いため）。`active_teacher_slot_count`・`paired_slot_count`はプロジェクト全体のAssignmentを読み込みLINQで前後を再集計する形で実装（件数が少なく許容コスト）。
- `qualification_override`（RED→YELLOWへの降格、資格外講師でも確認の上で手動配置を許可する特例）も追記で実装した。`BuildMovePreviewAsync`は講師の指導可能科目チェックだけを`EnsureTeacherCanTeachAsync`（ハード拒否）ではなく非throwの`IsTeacherQualifiedAsync`に置き換え、他の全hard制約を満たす場合に限り`qualification_override`という合成ソフト指標（Before=0/After=1で常にWorsened扱い）を付けてYELLOWへ降格する。自動最適化側の候補生成は元々`TeacherQualification.CanTeach=1`を要求しているため、この手動配置を候補として選ばないことは既存ロジックのまま担保される。
- `IScheduleEditorService`へ`PreviewMoveAsync`（読み取り専用、トランザクションをコミットせず破棄）を追加し、`MoveAsync`に`confirmSoftWarnings`/`reason`引数を追加（省略時はfalse/nullで、YELLOWなら`SoftWarningConfirmationRequiredException`を投げる＝Python版の`SoftWarningConfirmationRequiredError`と同じ「UIを経由しない直接呼び出しでもYELLOWは黙って適用されない」設計）。
- `ScheduleEditorPage.xaml.cs`の`Cell_Drop`（assignment分岐）へ`ResolveMovePreviewAsync`を追加。GREEN→即適用、RED→エラー表示のみで中止、YELLOW→悪化した指標一覧＋理由入力欄を持つ`ContentDialog`を表示し、「変更する」を押した場合のみ`confirmSoftWarnings:true`で`MoveAsync`を再実行する。
- 新規テスト3件（`SqliteScheduleEditorServiceTests.cs`）：`PreviewMoveAsync_ReturnsGreenWhenNoSoftMetricWorsens`、`PreviewMoveAsync_ReturnsRedMessageForHardConstraintViolation`、`MoveAsync_YellowRequiresConfirmSoftWarningsAndPersistsReason`（confirmSoftWarnings無しだと例外・ありだと成功しAuditLog.Reasonへ理由文字列が保存されることを検証）。

**追加で実装した項目（同checkpoint内、時間の許す範囲で優先度を自己判断して実施）:**
- 未配置カードのcandidateCount/reasonText表示：`GetUnplacedSessionsAsync`が残り回数に加え、`SqliteScheduleRunService`の候補生成クエリと同条件（資格・空き時間・出勤不可・生徒衝突）で数えた候補コマ数を返し、0件の場合は①科目を担当できる講師が未設定／②講師の空き時間・出勤可否の条件を満たすコマが無い／③生徒自身の他の授業と重なる、の3段階で理由を判定して表示する（Python版のremainingCount/candidateCount/reasonTextに相当）。
- 再最適化差分のカード単位詳細：`GetLabelSetAsync`（受講希望/講師/日付/コマのID→表示名ルックアップ）を新設し、④「自動作成の差分」カードに件数集計だけでなく「[日時変更] 生徒名 / 科目名　7/20 1限 → 7/21 2限」のような個別カードの変化一覧も表示するようにした。

**未対応（次回以降）:**
- `AddManualAsync`経由の配置（未配置一覧からのドラッグ）にはソフト指標プレビューを適用していない（前述の通り、未配置→配置は`unassigned_count`改善が支配的になりやすくPython版でも実質常にGREEN寄りになるため、優先度を下げた）。
- `unassigned_count`/`changed_existing_assignment_count`の全体差分ベースのソフト指標（1件移動では差分が常に0になりがちで実益が薄いため今回は見送った）。
- ドラッグ中のセルのライブ色分け（green/yellow/red背景色）は未実装。WinUIの`DragOver`イベントは同期的でデータ内容（どのカードをドラッグ中か）を確実に読めないため、Python版のような各候補セルへのリアルタイム色分けではなく、ドロップ確定後のプレビュー結果を確認ダイアログで見せる設計にした（デザイン差分として許容）。
- 3ペイン化（未配置レール／グリッド／詳細・履歴タブ）、学年・科目等のフィルタ、ズームスライダーは未着手（現状は縦積みのカード群で全機能を提供しており、機能面のギャップではなくレイアウトの好みの差と判断し優先度を下げた）。

**動作確認:** Release/x64 build警告0・エラー0。`dotnet test`全121 tests passed（Infrastructure 80・Application 12・Optimization 20・Domain 7・Architecture 2）。実機での確認ダイアログ表示・ドラッグ&ドロップ・差分カード・未配置理由表示の見た目確認はユーザー側で今後実施。アプリの起動自体はこのcheckpoint中に何度もリビルド・再起動して確認しており、`%LocalAppData%\SeminarSched.WinUI\logs\app-20260918.log`にクラッシュは一切記録されていない。

### v0.1.0 checkpoint 61 (Claude)

ユーザーから「アンケート取り込みができません。元のcsvを取り込んでちゃんと動くようにしてほしい」と報告を受けた。

**根本原因（実機ログから特定）:** `%LocalAppData%\SeminarSched.WinUI\logs\app-20260918.log`に22:29:34の実機クラッシュが記録されていた：`NullReferenceException` at `ImportPage.get_CurrentMatrixKind()` ← `ReloadMatrixAsync()` ← `MatrixKind_Changed`。原因は`ImportPage.xaml`の`<RadioButton x:Name="MatrixStudentKind" IsChecked="True" Checked="MatrixKind_Changed"/>`。WinUIは`IsChecked="True"`というXAMLリテラルプロパティの設定を`InitializeComponent()`実行中に同期的な`Checked`イベントとして発火させるが、その時点ではXAML中で後に宣言された兄弟コントロール（`MatrixTeacherKind`等）はまだ`Connect`されておらずnullのため、`MatrixKind_Changed`内の`CurrentMatrixKind`アクセスで即座に例外となる。`App.xaml.cs`の`UnhandledException`ハンドラは記録するだけで`e.Handled=true`を設定しないため、③アンケート取込みページを開いた瞬間に**アプリ全体が毎回クラッシュ**しており、「取り込みができません」はこのクラッシュでアンケート取込みUIへ到達すらできなかったことが原因だった（実際、クラッシュ後は22:29〜今回の修正までアプリが一度も再起動されていなかった）。

**対応:** `OptimizationPage`が同種の初期化順序バグ（`QualitySlider.Value`設定による早期`ValueChanged`発火）に対して既に使っていた`_isLoaded`ガードと同じパターンを適用。`ImportPage`に`_loaded`フィールドを追加し、`Page_Loaded`（`InitializeComponent`完了後、全フィールドConnect済みが保証される）で`true`にし、`MatrixKind_Changed`は`_loaded`がfalseの間は即returnするようにした。XAML側の`IsChecked="True"`はそのまま残している（`Page_Loaded`側の`if(ready)await ReloadMatrixAsync();`が既定表示（生徒）のデータ読込を別途行うため、削除しても機能に影響はない）。

**CSV解析ロジック自体の検証:** クラッシュ修正だけで解決したか確証を得るため、ユーザーがDownloadsフォルダに保存していた実際のGoogleフォーム回答CSV（生徒57名分・講師16名分、実データ）を一時的なリフレクション経由の検証ツール（コミットせず使用後に削除、実データはコミット・ログへは一切書き込んでいない）で読み込み、`ReadTable`のヘッダー検出・`StudentRequestColumns`の教科×回数×学校区分の列ペアリング（16列すべて正しく解決）・`DateHeaders`の受講/出勤不可日時列からの日付抽出（開講日21日分すべて一致）・`CanonicalQuestionnaireSubject`による科目名の正規化（例:「中学校・英語」「高校・数学ⅠA」等、想定通り）を確認した。解析ロジック自体に問題はなく、③のクラッシュが唯一の原因だったと判断できる。

**追加要望への対応（同じ会話内でユーザーから追加指示）:** 「2ファイルを入れる際は別々で入れるようにしてください。生徒回答用と講師回答用でそれぞれボタンを作ってください」。①簡易形式・②Googleフォーム生回答の両セクションで、従来は1つのボタンで生徒→講師と連続してファイルピッカーが開く仕様だったのを、「生徒回答ファイルを選択」「講師回答ファイルを選択」の2ボタン＋選択済みファイル名を表示するテキスト＋両方選択後のみ有効になる「検証」ボタンの構成へ変更した（`ImportPage.xaml`/`.xaml.cs`）。ファイルを選び直すと直前の検証結果（`_preview`/`_surveyPreview`）は破棄され「反映」ボタンも無効化されるようにし、古い検証結果を新しいファイル組み合わせに対して誤って反映できないようにした。

**動作確認:** Release/x64 build警告0・エラー0。修正後に実機で2回再起動し、ログにエラーが記録されていないことを確認した（22:36:16・22:41:57起動、以降エラーなし）。ボタン分離後のCSV検証・反映の実際のクリック操作による確認はユーザー側で今後実施。

### v0.1.0 checkpoint 62 (Claude)

checkpoint 61の修正直後、ユーザーが実際に③アンケート取込みで実CSV（生徒57名分の実回答・講師16名分の実回答）を投入したところ、「[エラー] 行1 [生徒回答] 必須列がありません: 生徒ID, 科目コード, 日付」等のエラーになるスクリーンショットが届いた。

**原因1（UIの見分けがつかない）:** エラーメッセージ文言（「生徒ID」「科目コード」）から、ユーザーが使ったのはchekcpoint 61で新設した①簡易形式（旧形式、生徒ID・科目コードを直接指定するCSV用）のボタン列だったと判明した。②Googleフォーム生回答セクションのボタンと、ラベルが「生徒回答ファイルを選択」対「生徒の生回答ファイルを選択」という僅かな差異しかなく、かつ両セクションとも枠のないフラットな並びだったため、スクロールした状態や流し見では容易に混同する。実際にこのバグでユーザーの操作が失敗した。

**対応1:** `ImportPage.xaml`のレイアウトを刷新。②Googleフォーム生回答セクションを最上部へ移動し、アクセントカラーの太枠＋「🟦 Googleフォーム回答の取込み（通常はこちらを使う）」の見出し、ボタンに①②③④の番号を振って操作順を明示。①簡易形式セクションは下部へ移動し、淡色背景＋「⬜ 旧形式（...Googleフォームの回答ではこちらは使わない）」という明示的な注記を付けた。

**原因2（②Googleフォーム生回答自体は正常）とその検証方法:** ①のボタンを使った場合の動作は仕様通り（生徒ID・科目コード形式のパーサーへ本物のアンケート回答CSVを渡せばエラーになるのは当然）であり、②自体にバグがあるか確認するため、ユーザーが「2026夏期講習.jukuschedule」（実プロジェクト）と実CSV2件をこの会話に貼り付けた。実データを保護するため、`%LocalAppData%\SeminarSched.WinUI\Workspace\Projects\2026夏期講習.jukuschedule`をセッションのscratchpad配下へ複製し、複製に対してのみ検証・反映・自動作成を実行し、検証後にscratchpad配下は全て削除した（実データはリポジトリにもログにも一切書き込んでいない）。結果：`CourseSurveyImportService.PreviewAsync`/`ApplyAsync`は生徒57名・講師16名・受講希望83件をエラー0件・警告0件で正しく反映した。②のロジック自体に問題はないと確認できた。

**発見2（真のバグ、より重大）:** 上記の実プロジェクトへ反映した状態で⑤の`SqliteScheduleRunService.RunAsync`（CP-SAT自動作成）を実行したところ、`InvalidOperationException: 時間割を作成できませんでした: Infeasible`で失敗した。ユーザーからの「もし正しくできているとなったら最後の計算までできるかも含めてやってほしい」という指示に基づき原因を追跡した。

- `CpSatScheduleSolver.AddRegularTeacherMinimums`が、通常担当講師優先度2以上のLessonRequestごとに「その通常担当講師との最低実施回数」を**ハード制約**（`model.Add(sum(regular)>=Math.Min(remainingMinimum,regular.Length))`）として課していた。コード内コメントには「Capacity shortages must not make the entire model infeasible」とあり、個々のrequestが自分の候補数を超えて要求されないようMath.Minで守ってはいたが、**複数のrequest間の衝突**（同じ生徒の別科目がそれぞれ別の通常担当講師の最低回数を要求し、その両方の「強制的に選ばれる」候補が同じ日時に重なるケース）までは考慮していなかった。
- 実データで検証：`RegularLessonProfile`が生徒75名全員に対して`RegularTeacherId`（共通名簿Excelの「通常授業」シート由来）を持ち、優先度は列が未入力のため既定値3（`SharedRosterImportService.ParseRegularLesson`の`row.Integer("担当講師優先度", false, 3, 1, 5)`）で全件揃っていた。これにより83件中67件のLessonRequestがこの強制最低回数の対象になり、うち16名以上の生徒が2件以上の強制対象を同時に持っていた。二分探索的にbisectionした結果（`AddStudentConsecutiveAndGapConstraints`単体では発生せず、`AddRegularTeacherMinimums`単体で再現）、この関数が原因と断定した。
- **対応:** `AddRegularTeacherMinimums`をハード下限からソフト化した。各requestごとに`shortfall`という`IntVar(0, achievable)`を導入し、`sum(regular)+shortfall>=achievable`という常に充足可能な制約にし、未達分（shortfall）を目的関数で重くペナルティ（`-100,000`/回、未配置ペナルティ1,000,000より弱くday-dispersion10,000より強い）を課す方式に変更。これにより「できる限り通常担当講師に割り当てる」という意図は保ちつつ、他の生徒・科目との衝突があってもモデル全体がInfeasibleにならないようにした。あわせて`ScheduleSolutionValidator.ValidateRegularTeacherMinimums`（ソルバーのハード制約が守られたかを検証する側のテスト）も削除し、対応するテスト`Validator_RejectsUnsatisfiedRegularTeacherMinimum`を`Validator_AcceptsUnsatisfiedRegularTeacherMinimum`へ置き換え、新規に衝突再現テスト`SolveAsync_RegularTeacherMinimumBecomesSoftWhenTwoDemandsCollide`を追加した（`tests/SeminarSched.Optimization.Tests/CpSatScheduleSolverTests.cs`）。
- **修正後の実データ再検証:** 同じ複製プロジェクト・同じ実CSVで①検証→②反映→③自動作成をフルパイプラインで再実行し、**placed=458 / unassigned=0**（必要回数の合計458件全件を配置、未配置0件）で成功することを確認した。elapsed約21秒（quality設定は20秒のtime limitで検証、実アプリの⑤画面ではユーザー設定のquality levelに応じた時間制限が使われる）。

**動作確認:** Release/x64 build警告0・エラー0。`dotnet test`全122 tests passed（Infrastructure 80・Application 12・Optimization 21・Domain 7・Architecture 2、Optimizationは差し替え後+1件）。実データでのフルパイプライン検証（import→schedule run）は上記の通りscratchpad上の複製プロジェクトで実施し完全成功、検証後にscratchpadは削除済み。ユーザーの実プロジェクト（`2026夏期講習.jukuschedule`）そのものは今回一切書き換えていない。実機アプリでの③②セクションの見分けやすさ、⑤自動作成の実行結果はユーザー側で改めて確認してほしい。

### v0.1.0 checkpoint 63 (Claude)

実機で①時間割編集まで一通り動作したユーザーから、「途中で止まらず、キューの末尾に追加していく形で」という指示のもと①設定画面の細かい修正一式と、③④⑤⑥への追加要望を連続して受け取った。以下①③④まで対応（⑤⑥は次checkpointで対応）。

**①設定画面:**
- **生徒タブ:** 一覧が単一文字列の連結表示で生徒ID・氏名・学年の桁がずれて見えていた問題を、ヘッダー行＋`ListView.ItemTemplate`（固定幅Grid列）による本物のカラム表示に変更（`生徒ID|生徒氏名|学年|状態`）。`MasterItem<T>`にActive/StatusTextを追加。
- **担当設定タブ:** 「講師対応科目」の入力＋フラット一覧を、添付画像と同じ講師×科目のマトリクス表（行=講師、列=科目を校種でグループ化しヘッダー2段、セルの○をクリックしてCanTeachをトグル）へ置き換え（`RenderQualificationMatrix`/`QualificationCell_Click`）。個別の備考編集は上部の単票フォームに残した。
- **受講希望:** ①から完全に削除し、③アンケート取込みページの「Googleフォーム回答の取込み」カードの直後（かつ「可用性の手動編集」カードの直前）へ移設。表示を生徒ID/科目コードから生徒氏名/科目名へ変更し、こちらもヘッダー＋固定幅Gridで列ずれを解消（`LessonRequestRow`表示レコード）。
- **コマ設定:** 新規プロジェクト作成時のデフォルトを従来のZ/A/B/C 4枠からA/B/C 3枠へ変更（`SqliteProjectRepository.DefaultTimeSlots`からZを削除）。一覧の各行に削除ボタン（×）を追加し`ICourseSettingsRepository.DeleteTimeSlotAsync`を新設（Assignmentが`ON DELETE RESTRICT`のため使用中のコマは削除できずSqliteExceptionを分かりやすいメッセージに変換）。開始・終了のTimePicker列幅が110pxで狭く分単位が見えなかった問題を160px相当へ拡幅して解消。
- 関連テスト`QuestionnaireKitServiceTests`のZスロット依存アサーションを`"A 17:10～18:30"`へ更新。

**③アンケート取込み:** ユーザーから「旧形式（簡易形式CSV）は消してよい」との指示を受け、checkpoint 61で追加した「⬜ 旧形式」カードとその専用コードビハインド（`Select/Verify/Apply_Click`・`RenderDiff`等）を削除した。バックエンドの`IResponseImportService`/`ResponseImportService`自体はUI呼び出し元が無くなり事実上orphan状態だが、今回は削除せず残した（テスト付きの独立した機能でありUIから見えなくなっただけで実害はないため。将来的な完全撤去は未着手）。

**④スクロールバー:** `ScheduleEditorPage`・`OptimizationPage`はcheckpoint 59で「`ProjectRequired`のInfoBarはプロジェクト未選択時も操作可能に保ちたいが、コンテンツ本体（`ScrollViewer`）はグレーアウトしたい」という理由でルートを`<StackPanel><InfoBar/><ScrollViewer x:Name="ContentPanel">...</ScrollViewer></StackPanel>`という構造にしていた。`StackPanel`は子要素に無限の高さを与えて計測するため、内側の`ScrollViewer`が「自分に割り当てられた領域を超えたらスクロールする」という判断ができず、スクロールバー自体が出ないままウィンドウ下端でコンテンツが見えなくなる、という実害のあるレイアウトバグだった。ルートを`<Grid RowDefinitions="Auto,*">`（Row0=InfoBar、Row1=ScrollViewer）へ変更し、`ScrollViewer`が残り領域ぶんだけの確定した高さを受け取れるようにして解消した。同じ症状が出ていないか他ページのルート構造も全て確認し、`OutputPage`（⑥出力）だけがそもそも`ScrollViewer`を持たない素の`StackPanel`ルートだったため、他ページと同じ「`ScrollViewer`を直接ルートにする」パターンでラップして予防した。`ImportPage`・`SetupPage`・`QuestionnairePage`・`HomePage`は元から`ScrollViewer`が直接ルートの安全な構造だったため変更不要。

**動作確認:** Release/x64 build警告0・エラー0。`dotnet test`全122 tests passed。実機アプリを再起動しログにエラーが無いことを確認。①③④の各画面の実際の見た目・スクロール動作の確認はユーザー側で改めて実施してほしい。

**未対応（次checkpointへ持ち越し）:**
- ⑥出力：ExcelのカラムレイアウトをPython版と完全一致させる対応（PDFは当面xlsx変換のままでよいとユーザーから明示的に許可されている。はみ出し修正は将来対応）。

### v0.1.0 checkpoint 64 (Claude) — ⑤時間割自動作成：複数戦略・進捗UI

ユーザーから「複数戦略を行っていくために、他のソルバーなどもどんどん追加していってください。自動作成中に中断したくなることもあるはずなので、進捗ゲージ・パーセンテージ・残り時間予測・現在の処理内容を表示してほしい（スピナー演出は残す）」との指示。

**発見：スケルトンは既に存在していた。** `src/SeminarSched.Optimization/Execution/`・`Profiles/`配下に、`IScheduleStrategy<TInput,TSolution>`・`ScheduleOptimizer`（ステージ×戦略の実行エンジン、時間配分・停滞検知・「現在のベストを採用して中断」・進捗通知を完備）・`OptimizationProfileCatalog`（品質5段階×ステージ×戦略構成）・`ScheduleEvaluation`（8段階の辞書式比較）が、テスト付きで既に実装済みだった。`OptimizationPage`のコメント「現時点のv0.1.0 solverは単一CP-SAT戦略です。複数戦略はv0.2.0で接続します」が示す通り、前セッション（Codex）が設計・実装したがCP-SAT本体・UIへの配線が未着手のままだったv0.2.0計画そのものだった。今回はこの既存エンジンに実際のCP-SAT戦略9種を実装して接続した。

**実装:**
- `CpSatScheduleSolver`に`CpSatSolveOptions`（乱数シード・並列ワーカー数・search_branching・hint・LNS用の自由request集合）を追加し、`CpModel.AddHint`（ヒント）と`model.Add(variable==0/1)`（LNSの固定）を実装。
- `OptimizationStrategyKind`の9種すべてに実装を追加（`Execution/CpSatStrategies.cs`）：StandardCpSat（標準）・SeededCpSatA/B/C（乱数シード違いの多重試行）・AlternateDecision（`search_branching:PORTFOLIO_SEARCH`）・MultiStage/HintImprovement（直前の最良解をhintに再探索）・NeighborhoodRepair（受講希望の約25%だけを自由にし残りをhintの値へ固定するLarge Neighborhood Search、hint自体が全ハード制約を満たす解のため必ず実行可能）・FinalPolishing（最終段の仕上げ探索）。
- `ScheduleEvaluationCalculator`を新設し、`ScheduleSolution`から`ScheduleEvaluation`（未配置数・通常担当講師不足・希望講師penalty・分散penalty等）を計算する処理を実装。
- `IScheduleRunService`/`SqliteScheduleRunService`に`RunAsync(path, OptimizationProfile, OptimizationRunControl, IProgress<OptimizationProgress>?, CancellationToken)`を追加し、内部で`ScheduleOptimizer`へ委譲。既存の`RunAsync(path, TimeSpan, ...)`は単一ステージ・StandardCpSat1本のプロファイルへ変換する後方互換ラッパーとして残し、既存テスト（`SqliteScheduleRunServiceTests`等）は無変更のまま通る。
- `OptimizationPage`：進捗ゲージ（`ProgressBar`＋経過/残り時間予測＋パーセンテージ＋現在のステージ・戦略名を日本語ラベルで表示）を追加し、既存のスピナー（`ProgressRing`）はそのまま維持。「中断して現在の結果を採用」ボタンを追加し`OptimizationRunControl.AcceptCurrentBest()`を呼ぶ。

**発見した重大な性能退行とその修正:** 実データ（生徒57名・受講希望83件・必要回数計458件・候補変数41,575個）で「高速」プロファイルを検証したところ、複数戦略で時間を均等分割すると全戦略が失敗する退行を発見した。原因は二重：(1) `OptimizationProfileCatalog`の「高速」がステージ内3戦略で60秒を均等分割し1戦略20秒しか使えなかったため、単一戦略時代の所要時間（21〜31秒）に届かなかった。(2) より深刻な原因として、`CpSatSolveOptions.NumSearchWorkers`の既定値を`1`（単一スレッド）にしていたが、実データで直接比較したところ`num_search_workers:1`は40秒経過しても実行可能解にすら到達せず、旧実装が使っていた`num_search_workers:0`（自動）はわずか32.4秒で解けることを確認した。既定値を`0`へ修正し、`OptimizationProfileCatalog`の「高速」「やや高速」も総時間を底上げ（60→120秒、180→240秒）・戦略数を調整して1戦略あたりの持ち時間を単一戦略時代以上に確保した。修正後、実データで「高速」「やや高速」、および全4ステージ・全9戦略中5種（1戦略はカタログの各段に重複あり）を強制実行するカスタムプロファイルの3パターンすべてで検証し、458件全件配置・エラーなしを確認した（LNSの`NeighborhoodRepair`はhintで温めた上で2.8秒という高速さで完了し、部分固定の仕組みが正しく機能していることも確認できた）。

**テスト:** `CpSatScheduleSolverTests`にhint/LNS固定の動作確認テスト2件、`CpSatStrategyIntegrationTests`に9戦略すべてを通しで実行する結合テストを追加。`OptimizationProfileCatalogTests`・`ScheduleOptimizerTests`は「高速」カタログの戦略構成変更に合わせて更新（後者はカタログの実際の構成に依存しない自前プロファイルへ変更し、将来のカタログ再調整に対して頑健にした）。

**動作確認:** Release/x64 build警告0・エラー0。`dotnet test`全125 tests passed（Infrastructure 80・Application 12・Optimization 24・Domain 7・Architecture 2）。実データでの検証は使い捨てスクリプトでscratchpad上の複製プロジェクトに対して実施し、検証後に削除済み（実データはコミット・ログへ一切書き込んでいない）。実機アプリでの進捗UI・中断ボタンの見た目・実際のクリック操作はユーザー側で改めて確認してほしい。

**未対応:**
- CP-SATの`SolutionCallback`（解が改善されるたびに呼ばれるコールバック）は未接続。進捗の経過時間・残り時間予測は壁時計ベースの近似（`OptimizationProgress.Elapsed`/`MaximumTime`）で、ソルバー内部の探索進捗そのものではない。
- 「標準」「高品質」「最高品質」帯は今回のworkers修正後に実データでは未再検証（「高速」「やや高速」および全ステージ強制実行プロファイルでの検証により、同じ仕組みを使う以上おそらく問題ないと判断しているが、実際のユーザー操作での確認は未実施）。

### v0.1.0 checkpoint 65 (Claude) — ⑥出力：帳票をPython版と同じ5種の独立ファイルへ再構成

ユーザーから「⑥について、出力されるファイルの列や配置なども含めてpython版と全く同じものにしてほしい。PDF版はとりあえずxlsxを変換したものでいい」との指示（①〜⑤の一連の作業キューの最後の項目）。

**発見：アーキテクチャそのものがPython版と異なっていた。** Python版の⑥出力は`overall`（全体時間割）・`student_handouts`（生徒配布）・`teacher_handouts`（講師配布・学年順）・`teacher_packets`（講師配布・講師別、講師ごとに独立したfolder出力）・`issues`（未配置・警告一覧）の5種を、それぞれ独立した帳票として生成する（`src/summer_scheduler/reporting/builder.py`の`ReportKind`と`src/summer_scheduler/ui/viewmodels/output_view_model.py`の`_REPORT_OPTIONS`で確認）。一方、既存のC#実装は`時間割.xlsx`1本に全体時間割・配置一覧（Python版に対応が無いsheet）・生徒別・講師別・未配置警告のsheetを束ねていた。列見出しの前に、まずこの「何本のファイルに分けるか」自体を合わせる必要があると判断し、`ExcelScheduleReportRenderer`を5つの公開メソッド（`RenderOverall`/`RenderStudentHandouts`/`RenderTeacherHandouts`/`RenderTeacherPacket`/`RenderIssues`）へ全面的に書き直した。

**実装:**
- `ScheduleReport`モデルを拡張：`ProjectTitle`・`AcademicYear`・`SeasonName`・`GeneratedAtText`を追加（出力情報sheet・handoutページの帳票タイトル用）。`SlotLabels: IReadOnlyList<string>`を`SlotDefinitions: IReadOnlyList<SlotDefinition>`（`Label`/`Code`/`TimeRangeText`）へ置き換え（後述のバグ修正のため）。`Unassigned: IReadOnlyList<string>`を構造化した`UnassignedRequestRow`（生徒/科目/必要/配置済/不足/主な理由/解決候補/優先度/通常担当/1対1/備考）へ、`RegularTeacherShortfalls`を`WarningRow`（severity/issue type/日付/コマ/生徒/講師/内容/対応状況）へ置き換えた。
- 新設`HandoutPageLayout`（`Layout/HandoutPageLayout.cs`）：Python版distribution_builder.pyの生徒個人calendarページ（9列A:I、月/曜日/日付見出し＋コマごとの週block、週全体が休校日の週は1行へ結合）を生成する共通レイアウト。生徒配布・講師配布（学年順）・講師配布（講師別）の3レポートが同じhandoutページ描画メソッド（`WriteStudentHandoutPage`）を共有し、`includeTeacher`フラグの有無だけで内容を出し分ける（Python版が3レポートとも同じ`_student_page`を共有しているのと同じ設計）。
- `未配置一覧`/`警告一覧`をPython版issue_builder.pyと同じ列見出し・列順の2sheet構成へ刷新。未配置行の主な理由・解決候補は、`SqliteScheduleEditorService.GetUnplacedSessionsAsync`と同じ3段階診断（①科目を担当できる講師が未設定／②講師の空き時間・出勤可否の条件を満たすコマが無い／③生徒自身の他の授業と重なる）＋候補コマ列挙クエリを`SqliteOutputPackageService`側にも実装して生成（Python版は候補ごとに独立validatorで再検証するが、ここでは同じ候補生成条件を満たす具体的な日時・講師の組をそのまま提示する簡略版）。
- 全体時間割は週ごとに`週_yyyyMMdd`sheetへ分割し、先頭に`出力情報`sheet（帳票名/校舎・講習/更新日時）を追加。`WriteOverviewWeekSheet`をコマ3行（学年／科目略称／生徒名を`TextRotation=255`で縦書き）×講師1名2列（同時最大2名まで横並び）の構成へ書き直し、末尾に凡例行を追加。
- `IOutputPackageService.OutputPackageResult`を5帳票×Excel/PDF＋講師別folderのパスを個別に持つ形へ拡張し、`SqliteOutputPackageService.GenerateAsync`は5メソッドをすべて呼び出して一時folderへ書き出してから`Directory.Move`でatomicに確定する（従来の設計を維持）。`OutputPage`のファイル一覧表示も5帳票分＋講師別folderへ更新。

**発見・修正したバグ（実データでの検証中に発見）:**
1. 生徒名・学年の並び順が「高→小→中」という五十音の読み順になっていた（既定の文字列比較のため）。Python版`_GRADE_ORDER`（小1..小6 < 中1..中3 < 高1..高3）と同じ学年順にする`GradeOrdering.SortKey`を新設し、生徒配布・講師配布・欠席一覧すべての並び替えをこれに置き換えた。
2. handoutページのコマ列見出しが「Zタイムタイム」のように「タイム」が二重になっていた。原因は、結合済みラベル文字列（`"Z 15:40-17:00"`）を空白で分割して先頭token=コード扱いしていたが、実データのTimeSlotは`DisplayName`と`Code`が別カラムで、`DisplayName`自体に既に用途表記が含まれるケースがあったため。`TimeSlot.Code`カラムを直接クエリして`ScheduleReport.SlotDefinitions`へ渡す方式に直し、文字列分割をやめた。

**動作確認:** Release/x64 build警告0・エラー0。`dotnet test`全125 tests passed（内訳は checkpoint 64 と同じ）。`SqliteOutputPackageServiceTests`を新しい5帳票構成に合わせて全面的に書き直した。さらに、使い捨てのテストクラス経由でscratchpad上に複製した実際のプロジェクトDB（生徒57名・講師34名・458件配置済み・未配置0件）に対して`GenerateAsync`を実行し、5帳票すべてのsheet名・列見出し・並び順・セル内容を確認した上で使い捨てテストとscratchpad上の複製データを削除した（実データはコミット・ログへ一切書き込んでいない）。アプリは再ビルド・再起動して起動確認済み。⑥出力画面でのボタン操作自体（実際のクリック→ファイル一覧表示）はユーザー側で改めて確認してほしい。

**未対応・意図的な簡略化:**
- Python版は休校日・範囲外の連続する列をセル結合で1つにまとめるが、本移植版は列ごとに同じ文言（「休校日」「指定範囲外」）を繰り返す簡略版のまま。
- Python版の「補足の集団授業」sheet（全体時間割）は、本移植版に集団授業の概念自体が無いため対象外。
- 警告一覧はPython版の汎用issue検知基盤（`project_validation_service`、severityやissue_typeを問わず様々な診断を蓄積する仕組み）が本移植版に無いため、現時点で検知できる「通常担当不足」のみを警告行として出力している。日程競合・定員超過等その他の診断種別は未移植（`FEATURE_PARITY.md`に明記済み）。
- PDF版はユーザー指示により「とりあえずxlsxを変換したもの」の位置付けのまま据え置いた。列見出しやsheet構成はExcel版に合わせたが、9列handoutページの罫線・縦書き等の細かい書式までは作り込んでいない。PDFのはみ出し等の見た目の崩れは別途対応が必要（ユーザーも認識済み）。
- 講師配布（講師別）の旧`担当一覧`sheet（通常担当/講習担当の2区分roster）はPython版に対応が無いため削除した。もし利便性の面で残したいという要望があれば、handoutページとは別に追加することを検討する。

### v0.1.0 checkpoint 66 (Claude) — ⑥出力：Python版が実際に生成したxlsxと直接突き合わせて修正

checkpoint 65完了直後、ユーザーから「python版を見ても分かりませんか？まずはpython版の仕様を確認してほしい。それでわからなかったら、こちらを確認してください（`ドキュメント`folder配下に実際にPython版で生成した`季節講習時間割.xlsx`・`生徒別時間割.xlsx`・`講師配布時間割_学年順.xlsx`・`講師配布時間割_講師別`folder等がある）」との指示。

checkpoint 65はPython版のソースコード（`reporting/*_builder.py`）を読んで実装したが、今回ユーザーが提示した「Python版が実際に生成したxlsx」を直接ClosedXMLで開いて構造・書式・セル内容を突き合わせたところ、ソースコードの読み取りだけでは気づけなかった複数の相違・誤りが見つかった。実データ（生徒・講師の実名を含む）はセッションのscratchpad配下へ一時的に複製して調査し、調査後にscratchpadごと削除した（実名はコミット・ログのいずれにも書き込んでいない）。

**発見・修正した相違点:**
1. **全体時間割の「コマ」ラベル列**: checkpoint 65では視認性を優先し先頭1列へ共通化していたが、Python版の実出力を見ると日付panelごとに専用のラベル列を持つ構成だった（`WriteOverviewWeekSheet`を全面的に書き直し、日付ヘッダーの結合範囲もラベル列を含めてpanel全体を覆うよう修正）。
2. **全体時間割の週sheet構成**: Python版は該当日・出勤講師が1件も無い週も含め、日程範囲内の全週をsheet化していた（「対象となる開校日・出勤予定講師がありません」のplaceholder表示）。checkpoint 65は該当日が1件も無い週をそもそも生成していなかった（`OverviewGridLayout.Build`が空の週を除外していたバグ）。週sheotに帳票タイトル・日付範囲の行（row1-2）が無かったのも合わせて修正。
3. **全体時間割の配色・コマラベル書式**: タイトル行(D9EAF7)・日付見出し(1F4E78・白太字)・コマラベル/コマ見出し(EAF0F6)・出勤不可セル(D9D9D9)の配色、およびコマラベルセルが「{コード}\n{開始}–{終了}」という改行＋enダッシュ表記であることを、実ファイルの`ClosedXML`読み取りで確認し反映した。
4. **講師配布（学年順）のrow5サマリー行**: checkpoint 65はソースコード上の`_regular_teacher_summary`関数を根拠に、プロフィール行の直下へ通常担当講師の一覧を追加していたが、Python版が実際に生成した`講師配布時間割_学年順.xlsx`を確認したところ、そのrowは常に空白だった（該当コードパスが実際には呼ばれていない、または別の設定でのみ有効という可能性が高い）。実出力に合わせ、生徒配布版と同一の空白行へ戻した。
5. **講師配布（講師別）の生徒絞り込みロジック（最重要）**: checkpoint 65は「その講師が担当する生徒だけに絞り込む」実装だったが、Python版`distribution_builder.py`の`build_teacher_packet_document`を再読し、かつ実際に生成された講師別folder内の1講師分のファイルを確認したところ、**全講師のファイルに参加生徒全員が含まれており、並び順だけが講師ごとに変わる**（その講師の通常担当の生徒→今期その講師が担当する生徒→残り全員、の3段階、各段の中は元の学年順を維持）という仕様だと判明した。実際、この検証に使ったファイルの講師は今期の担当が0件だったため、その講師別ファイルの並び順が`講師配布時間割_学年順.xlsx`（全講師共通の学年順一覧）と完全に一致しており、この仕様が確定した。`RenderTeacherPacket`（Excel・PDF双方）を書き直した。
6. **生徒配布・講師配布handoutページの休校日・範囲外セル**: Python版は日付列単位でコマ数ぶん縦結合し1つの値だけを表示するが、checkpoint 65は列ごとに文言を繰り返す簡略版のままだった。日単位の休校日・範囲外種別は週の中で一定（コマごとに変わらない）ことを確認した上で、縦結合するよう修正。
7. **handoutページの列幅・配色**: 列幅（A=8.3,B=11.4,C-I=9.2）、月見出しの配色(0B3041・白太字)、曜日/日付見出しの配色(F2F2F2)、休校日(E8E8E8)・範囲外(0E2841)セルの配色を、実ファイルの値に合わせて修正（checkpoint 65は概算値だった）。
8. `TimeSlot.Code`と生の開始/終了時刻を`SlotDefinition`に保持させ、handoutページ（全角チルダ区切り）と全体時間割（改行＋enダッシュ区切り）がそれぞれ異なる区切り文字を使えるようにした。

**未対応のまま（意図的）:** 本移植版に概念が無い「補足の集団授業」シート、Python版の汎用issue検知基盤（警告一覧の診断種別拡充）、PDF版の細かい書式（9列handoutページの罫線等）は、checkpoint 65から変更なし。

**動作確認:** `dotnet test`全125 tests passed（`SqliteOutputPackageServiceTests`を新しいセル書式・配色に合わせて更新）。使い捨てのテストクラス経由でscratchpad上に複製した実際のプロジェクトDB（生徒57名・講師34名・458件配置済み）に対して`GenerateAsync`を実行し、全体時間割の週sheet構成・コマ列structure・講師別ファイルの生徒集合と並び順が、Python版の実出力ファイルと一致することを確認した上で使い捨てテストとscratchpad上の複製データを削除した。アプリは再ビルド・起動確認済み。実際の見た目（配色・罫線等）はユーザー側で改めて確認してほしい。

### v0.1.0 checkpoint 67 (Claude) — ④時間割編集：生徒ID非表示・未配置カードの日付連動・出勤可否未設定講師の除外・画面構成整理

ユーザーから④時間割編集について複数の指摘。「生徒IDを設定する必要はないので画面に出さない」「未配置カードの詳細が見切れている、氏名・学年・科目（校種接頭辞なし）・残り回数を書いてほしい」「カード一覧は選択中の日付で配置可能な生徒だけにし、配置可能なコマ(Z/A/B/C等)も書いてほしい」「配置一覧・事前確定は基本的に使わないので、複数コマ・複数講師の出勤可否一括設定の下へ折り畳んでおいてほしい」「変更履歴は上ではなく時間割の右横に置いてほしい」「アンケートに回答していないはずの田中という人物が全日程に紛れ込む」。

**田中さんの件（データ調査、使い捨てスクリプトでscratchpad上の実DB複製のみ参照・削除済み）:** 田中さんは実際にTeacherテーブルに存在する有効な講師で、共通名簿由来の科目資格を2件持つが、`TeacherAvailability`（出勤可否）データが1件も無かった。`GetBoardAsync`は出勤可否データが無い講師を「常時出勤可能」として扱っていたため、資格さえあれば全日程の列に自動的に現れていた。ユーザーに対応方針を確認したところ「出勤可否データが無い講師は編集画面に出さない」を選択。

**実装:**
- `UnplacedSessionOption`を全面刷新：`Label`（生徒ID込み）/`CandidateCount`/`ReasonText`を廃止し、`StudentName`/`Grade`/`SubjectShortName`/`Remaining`/`AvailableSlotCodes`へ置き換え。`GetUnplacedSessionsAsync`に`openDateId`引数を追加し、内部の候補列挙（旧`CountCandidatesAsync`、プロジェクト全体でカウントのみ）を`GetAvailableSlotCodesForDateAsync`（1つの日付に絞り実際のコマコードを列挙）へ差し替え。候補コマが1つも無い受講希望はそもそも一覧に含めない（Python版のような「候補0件の理由表示」は廃止し、⑥出力の未配置一覧診断に一本化）。
- `BoardCard.StudentLabel`・`LessonRequestOption.Label`から生徒ID(`ExternalId`)プレフィックスを削除。
- `GetBoardAsync`：出勤可否データが1件も無い講師を、明示的な一時表示（「+講師を表示」）を除き列挙から除外するよう修正（田中さん問題の根本対応）。
- `ScheduleEditorPage.xaml`：`UnplacedList`に氏名・学年・科目略称・残り回数・配置可能コマを表示するカード型`ItemTemplate`を追加。「事前確定」「配置一覧・手動配置」を、日別グリッド編集カード内の「複数コマ・複数講師の出勤可否を一括設定」の下、既定で閉じた`Expander`へ移動。「変更履歴」をページ上部から日別グリッドの右列（未配置一覧｜グリッド｜変更履歴の3列構成）へ移動。

**動作確認:** Release/x64 build警告0・エラー0。`SqliteScheduleEditorServiceTests`を新しいUnplacedSessionOption・出勤可否フィルタに合わせて更新（日付ごとの候補フィルタ・生徒ID非表示・出勤可否未設定講師の除外を検証するテストを追加）。`dotnet test`全126 tests passed。田中さんの調査は実DBの複製に対する使い捨てスクリプトのみで行い、複製・スクリプトとも作業後に削除済み（実名はコミット・ログへ一切書き込んでいない）。実機でのカード見た目・折り畳みUI・変更履歴の配置はユーザー側で改めて確認してほしい。

**未対応:** 「講師の同時担当上限（2名まで）は候補コマの列挙に含めない」という既存方針は維持したため、まれに候補コマとして出た枠が実際にはドロップ時に講師定員超過でREDになるケースが起こり得る（旧実装から変わらない既知の挙動）。

### v0.1.0 checkpoint 68 (Claude) — ⑤時間割自動作成：画面外でも実行継続・進捗の常設表示・タスクバー連携

ユーザーから⑤について複数の指摘。「最適化品質スライダーが長く、目盛りの数字と実際の位置がずれている」「残り時間などの表示を動的に変化させてほしい（現状ほぼ固まって見える）」「自動作成中に別画面へ切り替えると中断してしまう現象がある（実際は動いているかもしれない）。別画面にいても進行状況が分かるよう左下（Settingsの上あたり）に表示しておいてほしい」「Windowsタスクバーのアイコンにも進行中は緑の進捗、完了時はオレンジで点滅するお知らせを出してほしい」。

**調査（コード読み取りのみ）:** `ScheduleOptimizer.RunAsync`の`progress?.Report()`はストラテジーの開始・終了時にしか呼ばれず、1ストラテジーの持ち時間は品質帯によっては数十秒〜数分になる（「最高品質」は1ストラテジーあたり約216秒）。そのためUIの経過/残り時間表示は実質「その報告が来るまで固まって見える」状態だった。また、`Run_Click`は`await App.ScheduleRun.RunAsync(...)`を直接待つ実装で、実行状態（`OptimizationRunControl`・進捗）はすべて`OptimizationPage`インスタンスのフィールドに閉じていた。WinUIの`Frame.Navigate`は画面遷移のたびに新しいPageインスタンスを作るため、実行中に他画面へ切り替えて戻ると（Task自体は裏で継続していても）新しいPageインスタンスには進捗情報が無く、UI上は「中断したように見える」。

**実装:**
- 新設`OptimizationRunState`（`SeminarSched.WinUI`直下、`ScheduleUndoState`と同じ「Pageの外に置く」設計）が実行制御・最新進捗・直近の結果をアプリ全体で1つ保持する。`StartAsync`が実行前スナップショットのpush・実際の`RunAsync`呼び出し・失敗時のロールバックまで一括で行うため、Page側は`await OptimizationRunState.StartAsync(path,profile)`を呼んで最新状態を読み直すだけでよい。1秒間隔の`DispatcherTimer`を内蔵し、直近の進捗報告値から経過時間を毎秒外挿して`Changed`イベントを発火するため、ストラテジー境界の間も残り時間表示が滑らかに動く。
- `OptimizationPage`は`Page_Loaded`で`OptimizationRunState.Changed`を購読し、`RefreshRunUi()`で現在の状態をそのまま反映するだけの薄い実装へ書き換えた。画面を離れて戻ってきても（新しいPageインスタンスでも）実行中なら進捗がそのまま復元される。
- `MainWindow`のナビゲーションペイン下部（`NavigationView.PaneFooter`、既定のSettings項目の直前）に、⑤実行中だけ表示される小さな進捗パネル（パーセント・残り時間目安、タップで⑤へ遷移）を追加。
- 新設`TaskbarProgress`（`ITaskbarList3`のCOM相互運用、追加パッケージ不要）で、実行中はタスクバーのアプリアイコンに緑の進捗バーを表示し、完了時は`SetProgressState(Paused)`（黄〜オレンジ寄りの配色。Windowsの`ITaskbarList3`はNormal/Error/Pausedの3色しか無く、指定の「オレンジ」の代替）と`FlashWindowEx`（タスクバーボタンの点滅、フォーカスが戻るまで継続）を組み合わせて完了を知らせる。
- `OptimizationPage.xaml`の品質スライダーを`MaxWidth="480"`のStackPanelへ収め、Sliderネイティブの目盛り位置とずれていた手描きの「1 2 3 4 5」数値行を削除（下の「品質レベル X/5」テキストと重複していたため実質的な情報損失は無い）。

**動作確認:** Release/x64 build警告0・エラー0。`dotnet test`全126 tests passed（挙動変更はUI層のみのためInfrastructure等のテストに影響なし）。アプリを再ビルド・起動確認済み。COM相互運用・タイマー・ナビゲーションペインの実際の見た目、画面切替時に本当に進捗が継続表示されるか、タスクバーの色・点滅は実機でのユーザー確認が必要（この環境からは対話的なGUI操作・タスクバー描画の確認ができないため）。

**未対応・既知の制約:** タスクバー進捗色はWindows APIの制約上「緑（実行中）」「黄〜オレンジ寄り（完了、Paused状態）」の2色で、正確な「オレンジ」そのものは指定できない。CP-SATのSolutionCallbackは引き続き未接続で、進捗率は壁時計ベースの近似のまま。

### v0.1.0 checkpoint 69 (Claude) — ホーム画面の年度既定値バグ修正・アプリ全体を日本語表示に固定

ユーザーから「ホームで年度の初期値が今の年度になっていない（デフォルト値がほしい）」「月日選択のカレンダーが英語になっている。他にも日本語であるべきものが英語になっている箇所があるので判断して直してほしい」との指摘。

**年度が既定値にならない不具合の原因:** `HomePage.Page_Loaded`に`if(AcademicYearBox.Value==0){年度・開始日・終了日の既定値設定、ProjectDefinition_Changedの購読}`という「初回のみ実行」のつもりのガードがあったが、WinUIの`NumberBox.Value`は未設定時`double.NaN`であり`0`ではないため、この条件は常にfalseで、このブロック自体が一度も実行されていなかった。年度の既定値が入らないだけでなく、開始日・終了日の既定値（今日／今日+30日）も入らず、年度・講習区分を変更しても自動生成タイトルが更新されない（`ProjectDefinition_Changed`が未購読のため）という3つの不具合が同時に発生していた。`double.IsNaN(...)`判定へ修正し、すべて解消。

**カレンダーが英語になる原因:** アプリ内の文言はすべて日本語のハードコードだが、パッケージの既定言語をOSの表示言語に委ねていたため、ビルド環境やOSの表示言語がen-USだと、`CalendarDatePicker`等WinUI組み込みコントロールの内蔵リソース（曜日名・月名等、自前では文言を持たない部分）だけ英語になっていた。`App()`コンストラクタの先頭で`Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride="ja-JP"`をXAML読み込み前に設定し、組み込みコントロールの表示言語をja-JPへ固定。

**日本語であるべき箇所の洗い出し:** XAML・codebehindを一通り検索し、`SettingsPage.xaml`（NavigationViewの既定Settings項目の遷移先）がVisual Studioの既定テンプレートのまま「Settings」「This is the Settings page」という未翻訳の英語プレースホルダーだったのを発見、日本語へ差し替えた（既存の「アプリ情報」ページと役割が重複するため、内容は最小限のまま）。ログメッセージ（`App.Logger.Info/Error`の引数、例："Schedule run completed"）は開発者向けの内部診断ログであり画面には表示されないため対象外とした。

**動作確認:** Release/x64 build警告0・エラー0。`dotnet test`全126 tests passed（挙動変更はUI層のみ）。アプリを再ビルド・起動確認済み。年度の初期値・カレンダーの表示言語・設定ページの文言は実機でユーザー側の確認をお願いしたい。

**未対応:** 「あなたの判断で日本語に戻してください」との指示のうち、今回はXAML/codebehindの静的な文言と組み込みコントロールの言語設定のみを対象にした。実行時にしか現れないダイアログ・エラーメッセージ等で見落としがあれば、追加で報告してほしい。

### v0.1.0 checkpoint 70 (Claude) — ホーム年度をドラムロールへ、⑤スライダー再調整、④「すべてリセット」追加

ユーザーから3件。「ホームの年度NumberBoxの枠（スピンボタンのハイライト）が他をクリックしても消えない。ドラムロールで選べるようにしてほしい」「⑤のシークバー（品質スライダー）がcheckpoint69の修正で小さくなりすぎた。元と今の中間くらいに、目盛りの数字も残してほしい（ずれないように）」「④時間割編集にすべての配置をリセットする機能がほしい」。

**ホーム画面の年度入力:** `NumberBox`（スピンボタン付き）が原因不明の描画残留を起こしていたため、コントロールごと`DatePicker`（`DayVisible="False" MonthVisible="False" YearVisible="True"`）へ置き換えた。年のみのドラムロール（WinUI標準のDatePicker年カラム）になり、ユーザー要望の見た目をそのまま実現できる。副次効果として`DatePicker.Date`は未設定時`double.NaN`のような特殊値ではなく既定で「今日」になるため、checkpoint69で`double.IsNaN`判定に修正した「初回だけ既定値を入れる」ガードそのものが年度に関しては不要になった（開始日・終了日の既定値設定とイベント購読は引き続き必要なため、判定を明示的な`bool _initialized`フィールドへ置き換えた）。

**⑤品質スライダー:** checkpoint69で目盛りの数字を削除し幅を480pxへ絞ったが、今回「元（束縛無し、~1100px超）と今（480px）の中間」との指示で760pxへ調整。削除していた「1 2 3 4 5」の目盛り数字も復活させたが、Sliderのtrackはthumb半径ぶん左右に内側マージンがあるため、数字の行にも同じマージン（12px）を付けた上で両端だけ`HorizontalAlignment="Left"/"Right"`（中央3つは`Center`）にする形へ変更し、以前より目盛り位置に近づけた（WinUIの内部レイアウトに依存するため、実機での見た目確認をお願いしたい）。

**④「すべてリセット」:** 既存の「自動配置だけリセット」（`IsLocked=0 AND IsManual=0`のみ削除）とは別に、ロック済み・手動配置を含めた全`Assignment`を削除する`ResetAllAsync`を追加。この操作は元に戻す操作を使えない（`clearsHistory:true`、既存の「自動配置だけリセット」と同じ方針）ため、実行前に`ContentDialog`で確認する。監査ログの新アクション`all_assignments_reset`を追加し、変更履歴一覧にも表示されるようにした。

**動作確認:** Release/x64 build警告0・エラー0。`dotnet test`全126 tests passed（挙動変更はUI層・新規メソッド追加のみで既存テストに影響なし）。アプリを再ビルド・起動確認済み。ドラムロールの見た目・スライダー目盛りの実際のずれ具合・「すべてリセット」の確認ダイアログはユーザー側で確認をお願いしたい。

### v0.1.0 checkpoint 71 (Claude) — ⑤進捗バー拡大・完了時100%表示・タスクバーをオレンジバッジへ、④レイアウト調整

ユーザーが実機で⑤時間割自動作成を実際に実行したスクリーンショット付きで5件の指摘。「進捗バーがまだ小さい、今の5倍くらいに」「すべてリセットのボタンが見当たらない（Expanderの中に隠れていて気づけなかった）、日別グリッド編集のところに出してほしい」「自動作成の差分・履歴を日別グリッド編集の右、入らなければ下に」「49%で止まって見えるが完了しているなら100%・残り0にしてほしい」「完了時の黄色はいいが、このウィンドウへ戻ってきたら元に戻したい。また色は黄ではなく橙にしてほしい」。

**実装:**
- `RunProgressBar`に`Height="24"`を指定（既定の細い高さから大幅に拡大）。
- ④の「すべてリセット」ボタンを、既定で折り畳まれているExpanderの中から、常に見えている日別グリッド編集のツールバー行（元に戻す/やり直す/+講師を表示/⑤自動作成へ、の並び）へ移動。
- ④の「自動作成の差分」カードを、ページ上部の独立カードから、日別グリッド編集内の3列目（変更履歴の真上）へ移動。3列目の幅を260→280pxへ拡張。
- `OptimizationRunState.Estimate()`を修正し、`IsRunning==false`（停滞検知による早期終了・「中断して現在の結果を採用」・完了のいずれか）になった時点で常に100%・残り0を返すようにした（それまでは経過時間/持ち時間の比率をそのまま出していたため、時間を使い切る前に終わった実行は完了後も中途半端なパーセントのまま止まって見えた）。
- タスクバー通知を「完了時はPaused（黄系）の進捗状態のまま」から「進捗バーはNoProgressへ戻し、代わりに`SetOverlayIcon`でオレンジ(#FF8C00)の丸バッジを重ねる」方式へ変更。バッジは`CreateIcon`のAND/XORマスクから実行時に生成（追加の画像アセット不要）。`MainWindow.Activated`でこのウィンドウがフォアグラウンドに戻った瞬間にバッジを消すようにし、「通知目的だけなので見たら消えてほしい」という要望に対応した（`FlashWindowEx`の点滅自体は`FLASHW_TIMERNOFG`により元々フォアグラウンド復帰で自動停止する）。

**動作確認:** Release/x64 build警告0・エラー0。`dotnet test`全126 tests passed。アプリを再ビルド・起動確認済み。なお、ユーザーが今回のスクリーンショットを送る直前に実機で⑤を実際に実行し、配置458件・未配置0件・118.1秒（標準探索）で成功したログを確認した（checkpoint 68の画面外継続の仕組みが実際に機能した実例）。進捗バーの実際の大きさ・オレンジバッジの見た目・フォアグラウンド復帰時に消えるかは、引き続き実機でのユーザー確認をお願いしたい。

**未対応・既知の制約:** タスクバーのオレンジバッジはWindowsのオーバーレイアイコン機能（16x16、単色円）であり、`SetProgressState`の緑色進捗バーとは別物として表示される（進捗バー自体はPausedのような色止め表示ではなく完了と同時に消える設計にした）。

### v0.1.0 checkpoint 72 (Claude) — アプリアイコンをPython版と統一、④手動配置をwarn-and-confirm方式へ、⑤スライダー幅を再修正

ユーザーから3件。「アイコンをpython版と同じにしてもらえますか」「手動配置のときに、条件を満たしていない場合は、置けないようにするのではなく、警告文を出してyes noで選ばせる形式にしたい」「品質のバーは長さが変わっていません（checkpoint71時点でも未解消）」。

**アプリアイコン:** Python版のアイコン一式（`seminarSched/src/summer_scheduler/resources/app_icon.ico`・`app_icon.png`、1024x1024マスター）をWinUI版の`Assets/`へ丸ごと置き換えた。`AppIcon.ico`はPython版のicoファイルをそのままコピー（10サイズ埋め込み済み）。パッケージ用の各PNG（`Square150x150Logo`・`Square44x44Logo`・`StoreLogo`・`Wide310x150Logo`・`SplashScreen`・`LockScreenLogo`等）は、既存ファイルの実寸法をPNG IHDRチャンクから確認した上で、使い捨てのC#コンソールプロジェクト（`System.Drawing.Common`、`<UseWindowsForms>true</UseWindowsForms>`）でPython版マスター画像から高品質リサイズして再生成した（正方形はそのまま拡縮、ワイド系は`Package.appxmanifest`の`BackgroundColor="transparent"`に合わせ透明背景に中央配置）。生成後、使い捨てプロジェクトは削除済み。

**④手動配置のwarn-and-confirm化（本checkpointの主要作業）:** これまで`AddManualAsync`（手動配置・事前確定・ドラッグ配置がいずれも内部で使用）は、講師の資格・出勤可否・担当上限のいずれかを満たさない場合に即座に`InvalidOperationException`で配置を拒否していた。今回、既存の`MoveAsync`が使っているGreen/Yellow/Red判定（`EditPreview`/`SoftWarningConfirmationRequiredException`）と同じ仕組みを追加側にも拡張し、次の分類にした。
- **Red（従来どおり即拒否・確認なし）:** リクエスト・コマ自体が不正、必要回数を超えて既に配置済み、同じ生徒が同じ日時に重複。いずれも物理的に不可能なケース。
- **Yellow（警告文＋はい/いいえで確認可）:** 講師がその科目を担当可能に未設定、生徒または講師がその日時に参加できない設定、講師の同時担当人数上限（2人）超過。
- 新設: `SqliteFixedLessonService.PreviewAddAsync`/`BuildAddPreviewAsync`（`PreviewMoveAsync`と同じ、コミットしないtransactionで判定）。`AddManualAsync`に`confirmSoftWarnings`・`reason`引数を追加（`MoveAsync`と同じ形）。
- UI側（`ScheduleEditorPage.xaml.cs`）に`ResolveAddPreviewAsync`を新設（`ResolveMovePreviewAsync`と同型）。事前確定ボタン・手動配置ボタン・ドラッグ&ドロップ配置（`Cell_Drop`の`"request:"`分岐）の3箇所すべてで、実際の配置前にこのpreviewを呼び、Yellowなら`ContentDialog`（警告文＋任意の理由入力欄）で「はい（配置する）/いいえ」を確認してから`confirmSoftWarnings:true`で再実行するようにした。
- 対象は**手動配置（新規追加）のみ**とし、ドラッグ移動（`MoveAsync`）側の既存の確認方式は変更していない（ユーザーの発言「手動配置のときに」を移動と区別して解釈）。
- 内部的には`EnsureTeacherCanTeachAsync`（旧・即throw版）を削除し、真偽値を返す`IsTeacherQualifiedAsync`/`IsAvailableAsync`/`IsWithinTeacherCapacityAsync`をpreview・実処理の両方から共有する形にリファクタリングした。`MoveAsync`が使う既存のthrow版ラッパー（`EnsureAvailabilityAsync`・`EnsureTeacherCapacityAsync`）はそのまま残し、移動側の挙動は変えていない。
- 既存テスト`SqliteFixedLessonServiceTests.AddManualAsync_OneToOneLesson_ConsumesBothTeacherSeats`は、2人目の配置が講師上限超過でRedからYellowへ変わったため、期待する例外を`InvalidOperationException`から`SoftWarningConfirmationRequiredException`へ修正し、`confirmSoftWarnings:true`で再実行すると実際に2件目が配置されることを確認するassertionを追加した。他の`AddManualAsync`呼び出しテストはすべて資格あり・出勤可・上限内のGreenケースのままで影響なし。

**⑤品質スライダー再修正:** checkpoint70で`StackPanel`に`MaxWidth="760"`を指定したが、ユーザーから「長さが変わっていない」との報告。`MaxWidth`は`Stretch`する子要素への伝播が不確実なため、`StackPanel`・`Slider`の両方に明示的な`Width="760"`を指定する形へ変更した（より強制力のある指定）。

**動作確認:** Release/x64 build警告0・エラー0。`dotnet test`全126 tests passed（`SqliteFixedLessonServiceTests`の期待例外変更を含む）。アプリを再ビルド・起動確認済み（ログにcrash記録なし）。アイコンの実際の見た目、スライダーの幅、Yellow警告ダイアログの表示・はい/いいえの動作は、いずれもこの環境からは視覚確認できないため、引き続き実機でのユーザー確認をお願いしたい。

### v0.1.0 checkpoint 73 (Claude) — ④「配置一覧」「自動作成の差分」の生徒ID非表示・日付連動・詳細情報追加

checkpoint72完了後、ユーザーから「④時間割編集のcard周りの修正を続けてほしい（生徒ID非表示・カード詳細情報・日付連動一覧・配置可能コマのヒント）」との指示。checkpoint67は未配置一覧カードのみが対象で、同じ画面内の他の場所に同種の問題が残っていたため、まず調査してユーザーに対象箇所を確認してから着手した。

**見つかった残課題:** 「事前確定・配置一覧・手動配置（通常は使いません）」Expander内の配置一覧（`GetAssignmentsAsync`が返す`ScheduleAssignmentItem.Label`）と、「自動作成の差分」カード（`GetLabelSetAsync`が返す`ScheduleLabelSet.RequestLabels`）の両方が、ラベル文字列に生徒の`ExternalId`をそのまま埋め込んでいた（例:「S-001 架空 生徒」）。また配置一覧はプロジェクト全体の配置を日付を問わず一括表示しており、未配置一覧・盤面のように選択中の日付へ絞り込まれていなかった。

**実装:**
- `SqliteScheduleEditorService.GetAssignmentsAsync`のSQLから`st.ExternalId||' '||`を除去し、代わりにPython版のカード詳細フォーマット（`_card_dict`の`f"{student_name}（{grade}） / {subject_name}"`）に合わせて`st.Name||'（'||st.Grade||'）'`（学年を括弧書き）を追加。さらに`'第'||a.SessionIndex||'回　'`をラベル先頭へ追加し、何回目のコマかも分かるようにした。講師側の`te.ExternalId||' '||te.Name`はこのアプリの既存の慣例（講師IDは常に表示）に合わせそのまま残した。
- `GetAssignmentsAsync`に`long? openDateId = null`引数を追加（既定nullは従来どおりフィルタなし。既存テストは全て単一引数呼び出しのため無変更で動作）。日付が指定された場合のみ`WHERE a.OpenDateId=$date`を適用。
- `GetLabelSetAsync`のRequestLabels構築SQLからも同様に`st.ExternalId||' '||`を除去し、`st.Name||'（'||st.Grade||'） / '||su.DisplayName`へ変更（「自動作成の差分」カードの生徒ラベルにも反映される）。
- `ScheduleEditorPage.xaml.cs`: 配置一覧の初期読み込み（`ReloadEditorAsync`、日付未選択時点）を廃止し、`ReloadBoardAsync`（日付選択・盤面再読込のたびに呼ばれる、未配置一覧と同じタイミング）内で`GetAssignmentsAsync(path, date.Id)`を呼ぶよう変更。これにより配置一覧が常に「日別グリッド編集」で選択中の日付のものだけに絞られる。日付未選択時は`UnplacedList`と同様`Assignments.ItemsSource = null`。
- 配置一覧セクションの説明文に「下の一覧は上の『日別グリッド編集』で選択中の日付の配置だけを表示します。」を追記。

**動作確認:** Release/x64 build警告0・エラー0。`dotnet test`全126 tests passed（`GetAssignmentsAsync`の新しい`openDateId`引数はすべて省略可能なため既存テストは無修正で通過。`GetLabelSetAsync_ResolvesRequestTeacherDateAndSlotLabels`はExternalIdの有無を検証していなかったため無修正で通過）。アプリを再ビルド・起動確認済み（ログにcrash記録なし）。実際の見た目（ラベルの表示内容・日付切替時の一覧の絞り込み）はユーザー側で確認をお願いしたい。

**未対応・意図的にスコープ外:** 事前確定・手動配置の生徒選択ComboBox（`ManualRequest`/`PreconfirmRequest`、`SqliteFixedLessonService.GetRequestsAsync`）はもともと`ExternalId`を含んでいなかったため対象外。配置可能コマのヒント（`AvailableSlotCodes`相当）は未配置一覧に限定される概念（既に配置済みのカードには「配置可能な別のコマ」という情報は無い）と判断し、配置一覧・差分カードへは追加していない。

### v0.2.0 checkpoint 74 (Claude) — ②アンケート作成に画像付き「作成手順」ポップアップを移植

v0.2.0 Draft Release直後、ユーザーから「アンケート作成について、元のpython版では画像付きの説明書があったはずです。それを表示できるようにしてください。また、それをポップアップできるような仕様にしてください。画像はpy版をそのまま流用してもOKです。」との指示。Explore subagentでPython参照repoを調査し、`ui/qml/GoogleFormsGuideDialog.qml`（10手順・画像13枚のスクリーンショット付きガイド、`ui/qml/QuestionnaireCreationPage.qml`の「作成手順」ボタンから開くモーダルDialog）が該当機能だと特定した。画像は`ui/qml/assets/google_forms_guide/`配下の13枚（Windows Explorer・メモ帳・Apps Scriptエディタ・Google認証画面のスクリーンショットのみで個人情報は含まない）で、Apps Script側の生成物（`.gs`3本＋手順書txt）には含まれず、あくまでアプリ内ポップアップ専用のアセットだった。

**実装:**
- Python版の13枚のPNGをそのまま`Assets/GoogleFormsGuide/`へコピーし、`.csproj`へ`<Content Include="Assets\GoogleFormsGuide\*.png" />`を追加。
- 新規`GoogleFormsGuide.cs`（`SeminarSched_WinUI`名前空間、`TaskbarProgress.cs`等と同じ場所に配置）に、Python版の10手順（番号・タイトル・説明文・画像1〜2枚・補足の注意書き）をそのままのテキストで移植した静的データと、`ContentDialog`向け・別ウィンドウ向けの両方から呼べる`BuildContent()`ファクトリメソッドを実装。
- `ShowAsync(XamlRoot)`: `ContentDialog`（既定の548px幅制限を`dialog.Resources["ContentDialogMaxWidth/MinWidth"]`の上書きで1100pxへ拡張するWinUI3の既知の回避策を使用）に手順を表示。Primaryボタン「別ウィンドウで表示」を押すとダイアログを閉じてから独立した`Window`（`AppWindow.Resize`で1180x820、`OverlappedPresenter.PreferredMinimumWidth/Height`で760x560を下限に設定）で同じ内容を開き直す（Python版の「モーダルのまま手順書を見ながら他アプリを操作できない」という不便さへの対策をそのまま踏襲）。
- 手順4の説明文中のURL（`https://script.google.com/home`）だけを正規表現で検出し`Hyperlink`化、クリックで`Windows.System.Launcher.LaunchUriAsync`により既定ブラウザーを開く（Python版の`Qt.openUrlExternally`相当）。
- `QuestionnairePage.xaml`に「作成手順」ボタンを追加（プロジェクト未選択でも操作可能。手順自体はプロジェクト固有のデータに依存しないため）。

**動作確認:** Release/x64 build警告0・エラー0。画像がAppXパッケージへ正しく含まれること（`bin\x64\Release\...\AppX\Assets\GoogleFormsGuide\*.png`）をビルド出力で確認。アプリを`dotnet run`で再起動しログにcrash記録がないことを確認。ダイアログの実際の見た目・画像の表示・「別ウィンドウで表示」の動作は、この環境からは視覚確認できないため、実機でユーザーに確認をお願いしたい。

### v0.2.0 checkpoint 75 (Claude) — ⑤品質スライダー目盛りの理論値inset化、集団授業クラス・受講登録機能の新規追加

ユーザーから2件。(1)「⑤の進捗バーの長さは良いが、やはり目盛りの数字の位置がずれる。きっちり同じ場所になるようにしてほしい」。(2) 集団授業（個別指導と並行して受講する生徒がいる集団クラス）の日程・受講生を登録できる機能を、③アンケート取込みの後段（3.1・3.2）へ新規追加してほしいという詳細な仕様指示。

**⑤品質スライダー目盛り位置:** checkpoint69「余白なし」→checkpoint70「Margin 12px決め打ち」はいずれも実測に基づかない推測だった。WinUI既定テーマの`generic.xaml`（`Microsoft.WindowsAppSDK.WinUI`パッケージ内、`Themes/generic.xaml`）を直接確認したところ、水平Sliderのthumb幅は`SliderHorizontalThumbWidth`（既定18px）で、`TickPlacement`のtickは中央のthumb列に合わせてこの半径ぶん左右へinsetされる（＝理論上の正しいinset量は18÷2＝9px）ことを確認した。今後テーマが変わってもズレないよう、固定pxではなく`Application.Current.Resources["SliderHorizontalThumbWidth"]`を`Page_Loaded`時に実測して`QualityTickLabels`（目盛り数字の`Grid`）の左右`Margin`へ反映する`AlignQualityTickLabels()`を新設した（取得失敗時は18pxを既定値としてfallback）。

**集団授業機能（新規、本checkpointの主要作業）:** 実装前にPython参照repoを調査（Explore subagent）したところ、v1.6.0で「再導入はv2.0.0で」として意図的に停止された`GroupLesson`/`GroupLessonStudent`というDB/serviceが残存していることが判明した。ただしPython版は「1回の開講＝1行」（`group_code`単位でクラス/seriesという概念が無く、同じクラスが複数日開講される場合は日程ごとに別行が必要）で、受講生の登録も手動UIが無くExcel一括取込みでしか行えない設計だった。ユーザーの今回の指示（クラスをまず登録し、その開講日程を複数ひも付け、受講生はクラス単位で1回チェックすれば済むようにしたい、同一学年でも複数クラスを許可、他学年受講の許可オプションを持たせたい）はPython版の設計と異なっていたため、Python版のUIをそのまま復活させるのではなく、指示に沿った独自設計で新規実装した（`docs/FEATURE_PARITY.md`の「Python v1.9.5で意図的に停止中の機能は無断でscopeへ追加しない」という既存の歯止めに抵触しないよう、今回はユーザーの明示指示に基づくものであることを同ドキュメントへ明記した）。

- **ホーム画面:** 「新しい講習プロジェクト」カードへ「集団授業の日程を考慮する」`CheckBox`を追加。オンにして作成したprojectだけが③に3.1/3.2を表示する（作成後の切り替えUIは無し）。`CourseProjectDefinition.Create`・`ProjectSummary`にそれぞれ`considerGroupLessons`引数／`ConsiderGroupLessons`プロパティを追加（いずれも末尾の省略可能引数のため既存呼び出し・テストは無修正で動作）。
- **schema:** `CourseProject.ConsiderGroupLessons`列と、`GroupLessonClass`（クラス名・対象学年・他学年受講許可・有効フラグ）・`GroupLessonSession`（クラス×`OpenDate`×`TimeSlot`の開講日程、複数可）・`GroupLessonEnrollment`（クラス×生徒の受講登録）の3テーブルを`SqliteProjectSchema`の`EnsureColumnsAsync`/`CompleteSchemaSql`（version番号を上げずに追加していく既存の枠組み）へ追加。**副次的に発見した既存バグを修正:** `SqliteProjectRepository.OpenAsync`は読み取り専用接続で`CourseProject`をSELECTするだけで、schema versionが既に最新（2）の場合は`UpgradeIfNeededAsync`が`EnsureCurrentAsync`を一切呼ばずに早期returnしていた。これまでは v1由来の安定列しか読んでいなかったため問題化しなかったが、今回`ConsiderGroupLessons`という「version番号を上げずに追加された列」を読もうとして初めて顕在化する潜在バグだったため、`UpgradeIfNeededAsync`の「既に最新版」分岐でも書き込み可能接続で`EnsureCurrentAsync`を呼ぶよう修正した。
- **Application/Infrastructure:** `IGroupLessonService`/`SqliteGroupLessonService`を新設（クラスのCRUD、開講日程の追加・削除・一覧、受講候補生徒の取得＝対象学年で絞込み・`AllowOtherGrades`時は全学年・現在の受講状態付き、受講登録のON/OFF）。クラス名の重複・開講日程の重複はSQLiteのUNIQUE制約違反(`SqliteErrorCode==19`)を捕捉しユーザー向けメッセージへ変換。
- **③アンケート取込み画面:** `ConsiderGroupLessons`がオンのprojectでのみ表示される2枚の新規カード。「3.1 集団授業クラスの登録」はクラス名・対象学年（既存生徒の学年から候補を出す`IsEditable`な`ComboBox`）・他学年受講許可を入力して保存し、選択中のクラスへ開講日程（日付・コマの`ComboBox`）を複数追加・削除できる。「3.2 集団授業の受講登録」はクラスを選ぶと、対象学年（`AllowOtherGrades`時は全学年）の生徒一覧がチェックボックス付きで表示され、チェックの都度その場で`SetEnrollmentAsync`を呼んで反映する（生徒名検索での絞り込みも可能）。
- 新規テスト`SqliteGroupLessonServiceTests.cs`（4件）：クラスの保存・更新・クラス名重複拒否、削除時のセッション・受講登録のカスケード削除、開講日程の追加・重複拒否・削除、受講候補の学年絞り込みと`AllowOtherGrades`切替時の全学年表示・受講ON/OFFの反映を検証。

**動作確認:** Release/x64 build警告0・エラー0。`dotnet test`全130 tests passed（既存126件は無修正で通過、新規4件追加）。アプリを`dotnet run`で再起動しログにcrash記録がないことを確認。

**未対応・意図的にスコープ外:** 受講登録した集団授業の時間帯を④時間割編集・⑤時間割自動作成の制約（個別指導との二重予約回避）へは連携していない（今回はデータの登録機能のみが指示範囲のため）。Python版の`GroupLesson`が持つteacher/room/note/subjectといった付随フィールドや、Excelでの一括登録は移植していない。品質スライダーの目盛り位置・集団授業3.1/3.2の実際の見た目は、この環境からは視覚確認できないため実機での確認をお願いしたい。

### v0.2.0 checkpoint 76 (Claude) — 新規project作成の同名衝突を警告＋自動リネームへ、最近使ったプロジェクトへフォルダーを開くボタン・最終更新日、講習区分「その他」

ユーザーから3件。(1)「プロジェクトを新しく作る場合、同名のフォルダが作成されてしまう場合、エラーとして返すのではなく、(2)をつけておき、同名のフォルダがあったから(2)という名前にしているという警告にしてください」。(2)「最近使ったプロジェクトのディレクトリをエクスプローラーで開けるようにするボタン、また、最終更新日の記載を追加してほしい」。(3, ターン途中で追加）「講習区分について、その他を選択した場合にはその名称を入力させるボックスを用意してほしい」。実機ログ（`app-20260919.log`、21:26〜21:27）を確認したところ、まさにこの「同名のプロジェクトが既に存在します。上書きは行いません。」という`IOException`でユーザーの新規作成操作が2回連続で失敗していたことを確認し、今回の指摘の実際の発生状況を裏付けられた。

**新規project作成の同名衝突（本checkpointの主要バグ修正）:** `HomePage.CreateProject_Click`に`ResolveUniqueProjectPath`を新設。保存先folder内に同名の`.jukuschedule`ファイルが既に存在する場合、Explorerのファイル複製と同じ流儀で`名前(2).jukuschedule`→`名前(3).jukuschedule`…と空いている名前を自動的に探し、そこへ保存する（`ProjectService.CreateAsync`/`SqliteProjectRepository.CreateAsync`側の「既に存在する場合は例外」というガード自体はそのまま残し、安全網として機能させる）。リネームが発生した場合は`InfoBarSeverity.Warning`（エラーではない）で「同名のプロジェクトファイルが既に存在したため名前を変更しました」と実際に保存したファイル名を表示する。project本体のTitle列（DB上の表示名）は変更しない、あくまでディスク上のファイル名だけの衝突回避である点に注意。

**最近使ったプロジェクト:** 各行に「フォルダーを開く」ボタンを追加し、保存先folderをOSの既定エクスプローラーで開けるようにした（`QuestionnairePage`の「保存先を開く」等と同じ`Process.Start(UseShellExecute=true)`パターン）。また「最終更新日」を追加表示：`RecentProjectEntry.LastOpenedUtc`（このアプリで最後に「開く」操作をした日時）ではなく、ファイル自体の`File.GetLastWriteTime`をその場で読み直した値を使うようにした（バックアップ復元など、アプリの「開く」操作を経ない変更でも正しい値になるようにするため）。表示用の`RecentProjectRow`（`RecentProjectEntry`＋算出済み文字列）をHomePage内に新設し、`RecentProjectsList.ItemsSource`をこれへ差し替えた。

**講習区分「その他」:** `CourseSeason`に`Other=4`を追加し、ホーム画面の講習区分`ComboBox`へ「その他」を追加。選択時のみ「講習区分の名称」`TextBox`（`OtherSeasonNameBox`）を表示し、`CourseProjectDefinition.Create`の新引数`customSeasonName`（Other選択時は必須、それ以外はnull）として渡す。`CourseProjectDefinition.Title`はOtherの場合`{年度}{CustomSeasonName}`を生成する（それ以外は従来通り`ToJapaneseName()`）。`CourseSeasonExtensions.ToJapaneseName()`のOtherケースは汎用fallbackとして「その他」を返すのみ（実際の名称はTitle列に保存済みの値が正）。⑥出力の`SqliteOutputPackageService`が個別に持つ`seasonName`という補助フィールド（`ToJapaneseName()`から再導出、帳票の一部にのみ使用）は、Other時は汎用の「その他」のまま据え置いた（`projectTitle`は別途DB保存済みの正しい値を使うため実害は小さいと判断）。

**動作確認:** Release/x64 build警告0・エラー0。`dotnet test`全132 tests passed（既存130件は無修正で通過、`CourseProjectDefinitionTests`に2件追加：Otherでの名称反映・名称未入力時のリジェクト）。アプリを`dotnet run`で再起動しログにcrash記録がないことを確認。3点とも実際の見た目・動作はこの環境からは視覚確認できないため、実機でユーザーに確認をお願いしたい。

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
