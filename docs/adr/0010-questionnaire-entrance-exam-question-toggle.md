# ADR 0010: アンケート作成に「中学受験と中学受験以外を分ける」オプションを追加する

- Status: Accepted for v1.4.0
- Date: 2026-10-14

## 背景・ユーザーの要望

ユーザー報告（クライアント側）: `生徒・講師_基本情報.xlsx`に基本情報を追加してアンケートを
作ろうとしたところ、「生徒用フォームに必要な小学校・中学校・高校の使用中科目が不足しています。
①の科目を確認してください。」という警告が出た。以前のアンケート修正の影響を疑っていた。

調査の結果、`QuestionnaireKitService.GenerateAsync`はプロジェクト自身のDB
（`_masterData.GetSubjectsAsync(projectPath, ...)`）から科目を読み込んでおり、共通名簿Excelや
`roster.db`を直接は読まない。実際のxlsxの科目シートは26件すべて有効で3校種を網羅しており、
`ClassifySchoolLevel`（`Contains('小')`/`'中'`/`'高'`の単純一致）もこのセッションの変更の
影響を受けていない。したがって、これはコード側のregressionではなく、アンケートを生成した
プロジェクト側に当該科目がまだ反映されていない（共通名簿を取込む操作が必要）という
ワークフロー上の問題である可能性が高い。エラー文中の「①」も、「0 基本情報設定」へ科目タブが
移動した今となっては古い案内文言だが、今回は変更対象としなかった。

続けて、ユーザーから独立した機能要望があった:

> 今の構成としてはアンケートに中学受験かどうかを科目で選択させているが、小学生であることを
> 選択されたら、その下に中学受験かどうかを選択させる欄をつくり、科目は算数（中学受験）、
> 算数（中学受験ではない）と分けずに、算数のみにする。国語も同様。実際に送られてくるものは
> 今と同じものになるようにする。

その後、これを既定の挙動ではなくオプションにしたいという追加要望があった:

> あー、オプションで、この中学受験のアンケートをつけさせるかどうかを変更できるようにしましょうか。
> 生徒数の下にもう一つオプションを追加して、そこで「中学受験と中学受験以外を分ける」というのを
> 追加してほしい。そうしたときに、アンケートで表示されるようにする。それ以外はこれまでと同じ。

生成されるGoogleフォームは実際にクライアントへ配布する生きたフォームであるため、既存の挙動を
無断で変えるリスクを避け、デフォルトでは現状維持、希望したときだけ新しい設計にする方針とした。

## 設計

### UI

当初は`QuestionnairePage.xaml`（2 アンケート作成）へその場限りのチェックボックスとして追加したが、
ユーザーから「チェックボックスはここに作ってほしい」と、ホーム画面「新しい講習プロジェクト」の
「オプション機能」パネル（既存の「集団授業の日程を考慮する」「一人の講師が同時に担当できる
生徒数」の並び）を示す指摘があり、設計をプロジェクト作成時の永続設定へ変更した
（下記Amendment参照）。

### `QuestionnaireKitService`

`GenerateAsync`に`bool askEntranceExamQuestion = false`を追加した。既定値`false`により、
既存の呼び出し元（テスト含む）は無改修で今まで通りの生成結果になる。

この値は`studentConfig`匿名型へ`askEntranceExamQuestion`としてそのままJSON化し、生成される
Google Apps Script内の`QUESTIONNAIRE_CONFIG.askEntranceExamQuestion`としてスクリプト本体へ
埋め込む。生徒用・講師用は共通のスクリプト本体テンプレート（`ScriptTemplate`）を使っており、
C#側で生成時にJSのコードブロックを出し分けるのではなく、**スクリプト本体は常に同一のまま**、
このconfig値によってApps Script実行時（フォーム作成時）に分岐させる設計にした。理由は、
既存のテンプレート構造（生徒・講師で共通、kindだけ分岐）を壊さず、将来ユーザーが生成済みの
Code.gsを直接見ても構造が一貫していて分かりやすいため。

分岐の内容:

1. `addStudentQuestions_`: `elementaryPage`（小学校ページ）の直後、`addSubjectRequestSection_`
   の前に、`askEntranceExamQuestion`がtrueのときだけ「中学受験をしますか？（必須）」
   （選択肢: する／しない）という質問を追加する。小学校の学年を選んだ回答者だけがこのページを
   通るため、この質問も小学生にだけ表示される。
2. `studentSubjectsBySchoolLevel.elementary`（C#側）: `askEntranceExamQuestion`がtrueのときだけ、
   「算数（中学受験）」「算数（中学受験以外）」を単一の「算数」へ（国語も同様に）まとめる
   （`StripEntranceExamDistinction`、`Distinct()`適用）。falseのときは今まで通り分割済みの
   2択のままになる。
3. 「他学年を含む」ページ用の`studentSubjectChoices`は、`askEntranceExamQuestion`の値に関係なく
   常に分割済みの科目名一覧から計算する（`rawSubjectsBySchoolLevel`から算出し、collapse後の
   `elementary`配列は使わない）。中高一貫などで小学校の科目を手動選択するこの経路は、学校区分を
   別質問で明示的に答えさせる既存設計のままなので、今回の変更の対象外とした。
4. `writeCompactStudentResponse_`（回答の正規化。`CourseSurveyImportService.cs`が読む
   「整形済み回答」シートを書く関数）: `askEntranceExamQuestion`がtrueかつ自動判定された
   学校区分が「小学校」かつ回答が「算数」または「国語」（bareな値）のときだけ、同じ回答にある
   「中学受験をしますか？（必須）」の答えを見て、「する」なら「算数（中学受験）」、
   「しない」（または無回答）なら「算数（中学受験以外なら可能）」へ復元してから整形済み
   シートへ書き込む。これにより、整形済みシートの列構成・値の形式は、オプションの有無に
   関わらず完全に同一になり、`CourseSurveyImportService.cs`（受講希望アンケート取込み）は
   **一切変更不要**になった。

## 検証

- `QuestionnaireKitServiceTests`に2件追加: オプションOFF時に分割済み選択肢のまま・
  `askEntranceExamQuestion: false`が出力されることを確認するテストと、オプションON時に
  小学校ページの選択肢が「算数」「国語」へまとまり、専用の質問文が追加され、かつ
  「他学年を含む」ページ用の一覧（`studentSubjectChoices`）は分割済みのままであることを
  確認するテストを追加した。
- 生成されたスクリプトは実際にGoogle Apps Script上で実行してフォームの見た目を確認する手段が
  このセッションの環境には無いため、生成されたコード文字列の内容検証に留めている（既存の
  同種のテストと同じ方針）。

## Amendment（同日）: プロジェクト作成時の永続設定へ変更

ユーザーから、ホーム画面「新しい講習プロジェクト」の「オプション機能」パネルのスクリーンショットと
ともに「チェックボックスはここに作ってほしいです」という指摘があった。これは既存の
「集団授業の日程を考慮する」（`ConsiderGroupLessons`、プロジェクト作成時だけで決まり後から
変更不可）・「一人の講師が同時に担当できる生徒数」のすぐ下の位置を指している。

これに合わせ、設計を以下へ変更した:

- `QuestionnairePage.xaml`のチェックボックスは削除した。
- `AskEntranceExamQuestion`を`CourseProjectDefinition`・`ProjectSummary`の新しいプロパティ
  （既定false）とし、`CourseProject`テーブルへ`ConsiderGroupLessons`と同じ
  自己修復ALTER TABLEパターン（`SqliteProjectSchema.EnsureCurrentAsync`）で列を追加した。
  `SqliteProjectRepository.CreateAsync`も`ConsiderGroupLessons`と同じ「CREATE TABLE後にUPDATEで
  既定値から上書きする」手順を踏襲している。
- ホーム画面`HomePage.xaml`の「オプション機能」パネルに、「一人の講師が同時に担当できる生徒数」の
  直後へチェックボックス`AskEntranceExamQuestionCheck`（ラベル「中学受験と中学受験以外を分ける」）
  を追加した。`ConsiderGroupLessons`と同様、**プロジェクト作成時だけで決まり、後から変更できない**
  設定とした（生成済みで既に配布済みのフォームとの一貫性を壊さないため）。
- `QuestionnairePage`は、チェックボックスの代わりに`App.ProjectService.Current!.
  AskEntranceExamQuestion`を読み取って`GenerateAsync`へそのまま渡す。あわせて、このプロジェクトの
  設定値をそのまま表示するだけの読み取り専用の案内文（`EntranceExamOptionNote`）を追加した。
- `QuestionnaireKitService.GenerateAsync`自体のシグネチャ（`bool askEntranceExamQuestion = false`
  引数）は変更していない。呼び出し元が変わっただけである。
- 回帰防止のため`SqliteProjectRepositoryTests`に、`AskEntranceExamQuestion`がtrue/false
  それぞれで作成→再読込しても値が保たれることを確認するテストを2件追加した。
- `HomePage.xaml.cs`の`BuildDefinition()`で、新しい引数を`CourseProjectDefinition.Create`の
  途中（`customSeasonName`の手前）へ挿入したことに伴い、既存の`OtherSeasonNameBox.Text`
  引数が位置引数のままだと誤って新しい`askEntranceExamQuestion`引数の位置へずれてしまう
  （講習区分名がtrue/false判定される）事故になりかねなかったため、該当呼び出しは名前付き引数
  （`considerGroupLessons:`・`askEntranceExamQuestion:`・`customSeasonName:`）へ修正した。
