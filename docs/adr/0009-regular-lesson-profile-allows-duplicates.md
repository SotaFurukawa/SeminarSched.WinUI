# ADR 0009: 通常授業担当設定（RegularLessonProfile）は、生徒・科目が同じでも通常担当講師が異なれば複数行を許す

- Status: Accepted for v1.3.0
- Date: 2026-10-14

## 背景・ユーザーの要望

> 通常授業について、複数の行で同じ氏名、科目があった場合、エラーがでますが、重複することもある
> ので、重複を許してください。

続けて、どういう意味での重複を許すべきか尋ねたところ：

> 優先度の高い方をできるだけ使う、です。場合によっては週に2回英語を見てほしい生徒もいて、同じ
> 講師でない場合があります。

## 調査で判明したこと

- エラーは、共通名簿Excelの「通常授業」シートを取込むとき（`SharedRosterImportService`）に
  発生していた。同じ(生徒ID,科目コード)の組が2行あると`AddDuplicateIssues`が取込みエラーに
  していた。
- この制約の根拠は、`RegularLessonProfile`テーブルの`UNIQUE(ProjectId,StudentId,SubjectId)`と、
  保存処理（`SaveRegularLessonAsync`・`UpsertRegularLessonsAsync`）がこのキーでUPSERTしていた
  ことにある。「生徒1人・科目1つにつき、通常担当講師は1行だけ」という前提だった。
- `RegularLessonProfile`自体は時間割自動作成（最適化）から直接読まれるテーブルではない。
  `CourseSurveyImportService.LoadRegularLessonProfiles`が、受講希望アンケート取込み時に
  新規`LessonRequest`行の既定値（通常担当講師・優先度・1対1必須）を埋めるための「テンプレート」
  としてのみ使われている。実際に最適化が読むのは`LessonRequest`自身の
  `RegularTeacherId`/`RegularTeacherPriority`と、既存だが未使用だった
  `PreferredTeacher1Id`/`PreferredTeacher2Id`/`PreferredTeacher3Id`（第1〜3希望講師）である。
- `SqliteScheduleRunService`は既にこれら5つの講師候補（通常担当講師＋希望講師1〜3）を重み付け
  スコアでランキングしており（通常担当講師は優先度1〜5に応じてスコア2〜10、第1希望=6、
  第2希望=4、第3希望=2）、`LessonRequest.RequiredSessions`で指定された必要コマ数それぞれに
  ついて、空いている講師の中から最もスコアの高い候補を使おうとする。つまり「通常担当講師を
  できるだけ使い、必要ならランク付けされた希望講師へフォールバックする」という、まさに
  ユーザーが求めていた挙動の土台が、スコアリングの仕組みとして既に存在していた
  （`PreferredTeacher1〜3Id`は、これまでどのコード経路からも書き込まれておらず、常にNULLだった
  ため、実質的に未使用のまま眠っていた）。

## Decision

- `RegularLessonProfile`の一意制約を`UNIQUE(ProjectId,StudentId,SubjectId)`から
  `UNIQUE(ProjectId,StudentId,SubjectId,RegularTeacherId)`へ緩和した。同じ生徒・科目でも、
  通常担当講師が異なれば複数行を許す（講師まで完全一致する行だけ、引き続き1行へ上書きする）。
  - 新規プロジェクトファイル（`SqliteProjectRepository.CreateAsync`）は新しい制約で作成する。
  - 既存のプロジェクトファイルは、`SqliteProjectSchema.
    RelaxRegularLessonProfileUniqueConstraintAsync`という自己修復マイグレーションで、
    テーブルを作り直して移行する（他テーブルからの外部キー参照が無いため、RENAME→再作成→
    コピー→DROPで安全に行える）。既存データは失われない。
- `SqliteMasterDataRepository.SaveRegularLessonAsync`は、`profile.Id>0`（既存行の変更）なら
  `Id`で対象行を特定してUPDATEし、`profile.Id==0`（新規追加）ならINSERT（講師まで完全一致する
  場合のみ上書き）するよう変更した。以前は常にId=0で呼ばれ、(生徒,科目)キーのUPSERTだけに
  頼っていたため、同じ生徒・科目の行は常に1行へ集約されていた。UIの保存ハンドラー
  （`SetupPage.xaml.cs`）も、既存行の変更時は実際のIdを渡すよう修正した。
- `SharedRosterImportService`の取込み時重複チェックも、キーに通常担当講師IDを含めるよう変更した
  （生徒ID・科目コード・通常担当講師IDが完全一致する行だけをエラーにする）。
- **`CourseSurveyImportService.LoadRegularLessonProfiles`を、単一値の辞書ではなく、同じ
  (生徒,科目)に対する複数行を優先度降順でまとめたリストの辞書へ変更した。** 新規`LessonRequest`
  を作る際、リストの先頭（優先度が最も高い行）を`RegularTeacherId`/`RegularTeacherPriority`へ、
  2番目以降（最大3件）を`PreferredTeacher1Id`/`PreferredTeacher2Id`/`PreferredTeacher3Id`へ
  割り当てる。これにより、ユーザーが望んだ「優先度の高い方をできるだけ使う、必要なコマ数ぶんの
  配置それぞれで、コマごとに異なる講師になることもある」という挙動が、既存の最適化ロジックへの
  変更なしで実現される（上記「調査で判明したこと」参照）。

## 影響範囲

- `src/SeminarSched.Infrastructure/Projects/SqliteProjectRepository.cs`: `RegularLessonProfile`の
  `CREATE TABLE`定義を新しい制約へ変更。
- `src/SeminarSched.Infrastructure/Projects/SqliteProjectSchema.cs`:
  `RelaxRegularLessonProfileUniqueConstraintAsync`を追加し、`EnsureCurrentAsync`の自己修復一式へ
  組み込んだ。
- `src/SeminarSched.Infrastructure/MasterData/SqliteMasterDataRepository.cs`:
  `SaveRegularLessonAsync`をId基準のINSERT/UPDATE分岐へ変更。`GetRegularLessonsAsync`の並び順に
  優先度降順を追加（同じ生徒・科目の行が画面上でも優先度順に並ぶように）。
- `src/SeminarSched.Infrastructure/MasterData/SharedRosterImportService.cs`: 重複チェックのキーと
  `UpsertRegularLessonsAsync`のON CONFLICT対象列を変更。
- `src/SeminarSched.Infrastructure/Importing/CourseSurveyImportService.cs`:
  `LoadRegularLessonProfiles`の戻り値をリストの辞書へ変更し、`InsertLessonRequest`で先頭を
  通常担当講師、残りを第1〜3希望講師へ割り当てるよう変更。
- `src/SeminarSched.WinUI/Pages/SetupPage.xaml.cs`: `SaveRegularLessonRow_Click`が、既存行の
  変更時は実際の`Id`を渡すよう変更（以前は常に0）。

## 検証について

既存の全テスト（計271件、うち新規4件）がすべて通過することを確認済み。新規テストは以下を検証
している。
- `SqliteMasterDataRepositoryTests.
  SaveRegularLessonAsync_AllowsMultipleRowsForSameStudentAndSubjectWithDifferentTeachers`:
  同じ生徒・科目で通常担当講師が異なる2行が共存でき、既存行の変更（Idを渡す）は別の新しい行を
  作らずその行だけを書き換えること。
- `SqliteProjectSchemaTests.
  EnsureCurrentAsync_MigratesLegacyRegularLessonProfileUniqueConstraint_...`: 旧制約のまま残って
  いた既存プロジェクトファイルが、自己修復マイグレーションで新しい制約へ移行し、既存データを
  失わないこと。
- `CourseSurveyImportServiceTests.
  ApplyAsync_MultipleRegularLessonProfilesForSameStudentAndSubject_...`: 優先度の異なる2行から
  生成される`LessonRequest`が、優先度の高い方を`RegularTeacherId`、低い方を`PreferredTeacher1Id`
  へ正しく引き継ぐこと。
- `SharedRosterImportServiceTests.
  PreviewAndApply_DuplicateStudentAndSubjectWithDifferentRegularTeachers_ImportsBothRows`:
  共通名簿Excelの「通常授業」シートで、同じ生徒・科目・異なる通常担当講師の2行が、以前のように
  取込みエラーで止まらず両方取り込まれること。

実際に「0 基本情報設定」画面のUI（通常授業担当設定タブで同じ生徒・科目の行を複数追加する操作）
を実機で確認するところまでは、本バージョン作成時点では行えていない（この開発環境のスクリーン
ショット手段に問題がある別件と同じ理由）。
