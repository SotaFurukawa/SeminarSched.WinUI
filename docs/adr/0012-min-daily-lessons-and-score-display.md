# ADR 0012: 講師の一日最低コマ数設定と、自動作成の評価スコア表示

- Status: Accepted for v1.7.0
- Date: 2026-10-19

## 背景・ユーザーの要望

ユーザーから「面白い、しかし演出ではなく便利な機能」の案として「空きコマ・キャパシティの可視化」
「講師別の実働コマ数集計（給与計算用）」を一緒に検討したあと、設定ページのスクリーンショットを
示しながら次の要望があった。

> この下に、講師の一日最低コマ数を設定できるようにしましょう。デフォルトは1で、2にすると、一日に
> 2コマは確定で入れなければならない。といった感じです。
>
> 様々なスコアもあるはずなので、それを表示できるようにしてみたい。今既にある出力の機能のボタンや
> ファイルを開くボタンなどの下に、それが見れるようにするものを追加したい。できますか？とりあえず
> あなたにおまかせします。

## Decision

### ①講師の一日最低コマ数（MinDailyLessonsPerTeacher）

`SchedulingPolicy`に`MinDailyLessonsPerTeacher`（既定1、1〜10）を追加し、「1 プロジェクト設定」の
「スケジュール設定」タブへ、「同時に使える座席数上限」の下に新しいNumberBoxとして追加した。

**ハード制約だが、常にInfeasibleを避けられる形にした。** ユーザーの言葉「確定で入れなければ
ならない」はハード制約を指しているが、`AddRegularTeacherMinimums`（既存の「通常担当講師の最低
保証」機能）のコメントが詳しく説明している通り、この種の最低数をハードにすると、既に確定済みの
配置や他の制約との組み合わせで、全体がInfeasibleになる余地が生まれる。これを避けるため、「ある
講師がその日1コマでも配置されるなら、その日はこの数以上を確定で配置しなければならない」という
**条件付き**のハード制約にした（その講師がその日まったく配置されないこと自体は常に許される）。
CP-SATの`AddMaxEquality`で作る「その日働くかどうか」の指標変数と`sum(free) >= Min * dayUsed`と
いう片方向の線形制約だけで表現でき、`.OnlyEnforceIf()`を使わずとも既存のコードベースの記法
（`model.Add(LinearExpr ... <= ...)`）だけで実装できた。既に確定済みの配置がある（講師,日付）の
組は、その講師がその日働くこと自体が既に確定しているため、残り必要数を自由変数だけで無条件に
満たす（新規追加できる候補が無ければ、その制約だけは課さない＝諦める）。

### ②自動作成の評価スコア表示

既存の`ScheduleEvaluationCalculator`（戦略同士の比較のためだけに使われていた内部実装、
`HardConstraintViolations`・`UnassignedLessons`・`ImportantPreferenceViolations`・
`MajorPenalty`・`TeacherGapSlots`・`DistributionPenalty`・`SpacingPenalty`・`OtherSoftPenalty`・
`ObjectiveValue`の9項目）が、実はユーザーの言う「様々なスコア」そのものだった。これまで
`OptimizationRun.ObjectiveSummaryJson`には`ObjectiveValue`と`UnassignedLessons`の2項目だけが
保存されており、残りの内訳は自動作成の実行後に捨てられていた。

`SqliteScheduleRunService.SaveValidatedAsync`で`ScheduleEvaluationCalculator.Evaluate`を呼び、
9項目すべてを`ObjectiveSummaryJson`へ保存するよう変更した。新しい`IScheduleRunService.
GetLatestRunEvaluationAsync`で、直近に完了した自動作成のスコアをいつでも読み戻せるようにした。

「6 出力」ページの、既存の出力ボタン・ファイルを開くボタンの下に「直近の自動作成の評価スコア」
欄を追加した。`HardConstraintViolations`（CP-SAT経由の解では構造的に常に0）と`TeacherGapSlots`
（このcalculatorでは未計算のため常に0）は、常に変化しない値を見せても意味が無いため、UIからは
あえて外した（レコード自体には残してある）。残り7項目（未配置コマ数・通常担当講師等の希望を
満たせなかった件数・講師希望との不一致ペナルティ・生徒の授業日の集中度・授業間隔の乱れ具合・
その他のソフト制約ペナルティ・総合スコア）を、「値が小さいほど質が高い」という説明文とともに
表示する。自動作成を一度も実行していないプロジェクトでは欄自体を非表示にする。

v1.6.1以前に保存された簡易な内容（ObjectiveValue・UnassignedLessonsのみ）は、無い項目が既定値0
として復元される（レコードの対応するコンストラクター引数に既定値が無いため例外にはならない）。
古い実行結果の内訳を正確には表さないが、次に自動作成を実行すれば以降は完全な内訳が保存されるため、
特別な移行処理は設けていない。

## 影響範囲

- `src/SeminarSched.Domain/Scheduling/SchedulingPolicy.cs`: `MinDailyLessonsPerTeacher`追加。
- `src/SeminarSched.Infrastructure/Projects/SqliteProjectSchema.cs`: 新規プロジェクトの
  `CREATE TABLE SchedulingPolicy`と、既存プロジェクト向けの自己修復`AddColumnIfMissingAsync`。
- `src/SeminarSched.Infrastructure/Scheduling/SqliteSchedulingPolicyRepository.cs`・
  `SqliteScheduleRunService.cs`（`ReadSchedulingPolicyAsync`）: SELECT/INSERT双方へ反映。
- `src/SeminarSched.Optimization/Core/CpSatScheduleSolver.cs`:
  `AddMinimumDailyLessonsPerTeacherConstraint`を新規追加。
- `src/SeminarSched.WinUI/Pages/SetupPage.xaml`/`.xaml.cs`・`SchedulingPolicyRowViewModel.cs`:
  新しいNumberBoxと`BuildPolicy`への受け渡し。
- `src/SeminarSched.Application/Scheduling/IScheduleRunService.cs`・
  `src/SeminarSched.Infrastructure/Scheduling/SqliteScheduleRunService.cs`:
  `GetLatestRunEvaluationAsync`追加、`SaveValidatedAsync`の`ObjectiveSummaryJson`を9項目へ拡張。
- `src/SeminarSched.WinUI/Pages/OutputPage.xaml`/`.xaml.cs`: 評価スコア欄を追加。

## 検証について

- `CpSatScheduleSolverTests`に2件追加: 唯一の受講希望1件だけでは最低数2を満たせないため未配置の
  ままになること、2件目の受講希望（別の生徒・同じ講師・同じ日）と組み合わされば両方配置できること。
- `SqliteSchedulingPolicyRepositoryTests`の既存ラウンドトリップ試験へ`MinDailyLessonsPerTeacher`を
  追加。
- `SqliteScheduleRunServiceTests`に2件追加: 自動作成を一度も実行していない場合にnullが返ること、
  実行後に`HardConstraintViolations=0`・`UnassignedLessons=0`で内訳が読み戻せること。
- 既存の全テスト、合計288件（新規5件を含む）がすべて通過。
- 「6 出力」ページの評価スコア欄の実機確認（実際にスコアがどう表示されるか）は、この開発環境で
  実データを使った確認がしづらいため、本バージョン作成時点では行えていない（既知の制約）。
