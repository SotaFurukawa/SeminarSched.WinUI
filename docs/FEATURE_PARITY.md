# Feature parity inventory

参照元はPython版v1.9.5（commit `1d323a4`）の実装とテストです。状態は、画面だけ・ダミーデータだけでは「実装済み」にしません。

| Python版機能 | Python側実装 | C#側実装 | 状態 | 備考 |
|---|---|---|---|---|
| アプリ起動・version/About表示 | UI / release metadata | WinUI shell / assembly metadata | 実装済み | v0.0.0。UI実機確認は未完了 |
| NavigationView業務導線 | QML workflow shell | ①設定〜⑥出力の6段階flow | 実装中 | ②〜④・⑥は未接続機能を明示する骨格 |
| Home dashboard | workspace view model / QML | project作成・open・close・backup・restore・複製UI | 実装中 | 主要導線は実装済み。recentの手動非表示は未実装 |
| 新規project作成 | project service | 年度・講習区分・期間・開講日を持つWinUI schema v1 | 実装中 | SQLite整合性確認後にatomic move |
| `.jukuschedule` open | project service / SQLite | WinUI schema v1の検証付きread/open | 実装中 | Python版は直接変更せず、copy-first importを別途設計 |
| 別名保存・複製 | project service | SQLite backup APIで検証付き複製を作成し、複製先へ切り替え | 実装済み | 元DB不変・上書き拒否を自動テスト |
| 最近使用・非表示 | workspace view model | 最大10件の履歴・再open・欠損時自動除去 | 実装中 | 手動非表示操作は未実装 |
| migration・整合性確認 | Alembic / project service | schema v2・自動backup付きv1→v2 migration・共通製品識別 | 実装中 | rollback failure injectionと今後のversion追加testを継続 |
| 自動backup・世代管理 | recovery service | UIからのSQLite backup API検証付きsnapshot | 実装中 | 自動実行と世代管理は未実装 |
| 復元・復元前backup | recovery service | 確認UI・復元前3世代backup・検証・rollback付きatomic restore | 実装中 | failure injection拡充は未実装 |
| 生徒基本情報 | master repository/service | 追加・一覧・選択編集・使用停止UI、domain validation・SQLite CRUD | 実装中 | ID wizardと検索は未実装 |
| 講師基本情報 | master repository/service | 追加・一覧・選択編集・使用停止・対応科目UI | 実装中 | 資格matrix一括操作と検索は未実装 |
| 科目・略称 | master repository/service | 追加・一覧・選択編集・使用停止UI、domain validation・SQLite CRUD | 実装中 | 校種presetは未実装 |
| 通常授業・担当優先度 | regular lesson profile | 管理UI・SQLite upsert・優先度1〜5・1対1必須 | 実装中 | 受講希望の全項目編集はExcel中心 |
| 共通基本情報Excel | master Excel service | 5sheet出力・全行preview・SHA再確認・transaction upsert・原本snapshot | 実装中 | 名前選択helper列とExcel内dropdownを継続移植 |
| 講習設定 | phase2 models/view model | 期間内日付・開校/休校・日別有効コマの保存UI | 実装中 | note編集・一括操作拡充は未実装 |
| コマ設定・並べ替え | phase2 UI/service | コマ追加・選択編集・使用停止・表示順・時刻範囲UI | 実装中 | drag並べ替えは未実装 |
| Google Forms作成kit | questionnaire script service | 設定値入りCode.gs・READMEのatomic生成UI。生徒フォームに学年ベースのpage分岐（小/中/高、科目をSchoolLevelから自動分類）と「他学年も受講する」分岐、特記事項・学力テスト希望を追加 | 実装中 | 講師指導可能科目用の別Apps Script、回答sheetの列正規化・診断は未移植。実際にGoogle上でスクリプトを実行しての動作確認は未実施（この環境からはGoogleへ到達できないため） |
| 生徒回答・講師回答2file選択 | import UI/service | CSV/XLSXの2file選択UI | 実装中 | sheet選択とGoogle Forms生列mapping UIは未実装 |
| CSV UTF-8/CP932・旧形式 | importing readers | quoted CSV・BOM/UTF-8/CP932自動判定・XLSX先頭sheet | 実装中 | 生Google Forms回答の自動mappingは未実装 |
| import preview/diff | importing diff | 全行参照検証・issue一覧（エラー/警告を区別）・件数preview。可用性形式（日付列あり）は追加/変更/変更なし件数と、以前登録済みで新しい回答に含まれない日付を「削除候補」として一覧表示し、明示チェックなしでは削除しない | 実装中 | 簡易形式（必要回数・勤務不可）側のdiffは未算出。Google Forms生回答の列mappingは未実装 |
| import transaction/AuditLog | availability import service | SHA-256再検証・単一transaction反映・ImportBatch・原本BLOB snapshot・AuditLog | 実装中 | 詳細diff表示とmapping保存は未実装 |
| availability一括編集 | phase3 UI/service | 生徒/講師の日付・コマ別0/1/2取込、講師不可と同期 | 実装中 | 手動matrix UIは未実装 |
| candidate生成 | optimization candidates | 資格・開校・生徒/講師availability・固定生徒衝突を検証し、希望講師penalty付き候補を生成 | 実装中 | Python版の全診断理由codeは未移植 |
| greedy初期解・complete hint | initial solution | 未実装 | 未実装 | v0.2.0複数strategy側で実装 |
| CP-SAT hard constraints | OR-Tools optimizer | 要求回数・生徒衝突・講師同時2名/1対1・固定保持・最大連続・空き時間禁止 | 実装中 | 学年別/日別上限等の残制約を継続移植 |
| 通常担当優先度1〜5 | objectives/constraints | 0/25/50/75/100%最低担当と候補不足時緩和・希望講師penalty | 実装中 | 辞書式soft objectiveと公平性は未完了 |
| 辞書式目的・公平性 | objectives | 未実装 | 未実装 | 未配置、分散、講師公平性 |
| 高速/標準/高品質 | optimization view model | 未実装 | 未実装 | 30/120/600秒 |
| 5段階の最適化品質profile | 追加仕様 | profile catalog / slider / JSON設定 | 実装中 | strategy実装・実行中UIは未完了 |
| 高品質トーナメント探索 | 追加仕様 | 共通optimizer orchestration・候補選抜 | 実装中 | 実CP-SAT複数戦略とbenchmarkは後段へ保留 |
| 現在best採用/キャンセル分離 | 追加仕様 | execution API上で分離・部分結果test | 実装中 | UI接続とvalidator・transaction境界は未実装 |
| progress/cancel | worker / solver callback | 非同期progress DTO・cancel API | 実装中 | 実solverとUIへの接続は未実装 |
| solver独立validator | result validation | 候補外・回数・衝突・容量・通常担当最低数・連続/空き時間をsolver外で再検証 | 実装中 | Python版全診断codeは未移植 |
| optimization transaction保存 | optimization run service | 固定保持・未固定atomic置換・OptimizationRun入出力概要保存 | 実装中 | 安定したinput fingerprintと詳細penalty内訳は未実装 |
| 時間割grid | schedule editor QML | ⑤に日付選択付きの行=コマ・列=講師グリッドを追加。カードはWinUIネイティブグリッドで生成 | 実装中 | virtualizationは未対応（開講コマ・講師数が多い場合の性能検証は今後） |
| drag/drop手動配置 | schedule edit service | カードのドラッグでセル間移動、未配置一覧からドラッグで新規配置。`MoveAsync`が資格・衝突・空き時間・講師上限をhard constraintとして再検証し、ロック済みは移動不可 | 実装中 | 実機での目視確認は未実施（ビルド・自動テストのみ確認） |
| manual/lock semantics | manual edit tests | ④固定Assignment追加・解除UI、可否/資格/同時2名/1対1/必要回数検証。⑤に手動配置追加/削除/移動・ロック切替・自動配置だけリセットUIを追加し、`IsManual`は再最適化時も`IsLocked`と同様に保持・候補除外・AuditLog記録 | 実装中 | Undo/Redoは未実装 |
| Undo/Redo | command stack | ⑤へ「元に戻す」「やり直す」を追加。Assignment/TeacherUnavailability全体のスナップショットをmemory上のstackで管理し、自動作成・手動編集すべての操作をカバー | 実装中 | process内のみ（再起動で消える）。粒度は操作単位ではなくテーブル全体のsnapshot |
| 講師一時表示・availability編集 | editor UI/service | グリッドの「+講師を表示」で全コマ不可の講師も列表示でき、セル右上の丸/バツで`TeacherUnavailability`を切替。既存配置があるセルは不可へ変更不可 | 実装中 | 一括操作（複数コマ・複数講師まとめて設定）は未実装 |
| 検索・scroll同期 | editor QML | 生徒名検索でグリッド内カードをハイライト表示 | 実装中 | header/row/grid間のscroll同期は未実装 |
| 未配置・警告 | diagnostics/output | 未実装 | 未実装 | 通常担当不足を含む |
| 全体時間割Excel/PDF | reporting/output service | 日曜始まり・土曜終わりの週単位、休校日を除いた日付を横に並べ、その日に配置がある講師だけを列として表示するgrid形式（`OverviewGridLayout`共通レイアウト）。コマを行、各セルへ学年・科目略称・生徒名を表示 | 実装中 | 1セル最大2名の表示は横分割ではなく縦積み（同一セル内で改行）。コマ不可のgray表示は未実装 |
| 生徒配布時間割Excel/PDF | reporting renderers | 生徒別sheet・PDF sectionを日曜始まり・土曜終わりの週calendar形式で生成（`WeeklyCalendarLayout`共通レイアウト）。講習に一度も参加しない生徒は個別calendarを作らず「講習欠席一覧」へ学年・氏名で一覧化 | 実装中 | セル内の科目略称・時刻の細かい書式はPython版と完全一致ではない |
| 講師配布（学年順） | reporting renderers | 生徒sheetを学年・氏名順に生成。生徒名は姓のみ表示、同姓がいる場合だけ名の先頭1文字を付与 | 実装中 | A4サイズへの厳密な収まり調整は未検証 |
| 講師配布（講師別） | reporting renderers | 講師別sheet・PDF sectionを週calendar形式で生成 | 実装中 | 講師別folder packet（個別ファイル分割、通常担当優先の並び順）は未実装 |
| 未配置・警告一覧出力 | output service | 未配置回数をExcel/PDFへ出力 | 実装中 | Python版全5種類診断は未移植 |
| atomic export・上書き確認 | output service | integrity再確認・新規一時folder・上書きなし | 実装中 | 個別file選択とpreviewは未実装 |
| 設定・logging | settings/logging | 未実装 | 未実装 | 個人情報を記録しない |
| 集団授業DB/service互換 | group lesson service | 未実装 | 保留 | v1.9.5同様UI停止中 |
| Windows配布・受入 | packaging/release tests | CI基盤のみ | 実装中 | installer/portable方針は未決 |

## Completion rule

各項目は、機能実装、単体テスト、統合テスト、UI確認、Python版との同等性確認が揃って初めて「動作確認済み」とする。Python v1.9.5で意図的に停止中の集団授業UIなどは、無断でscopeへ追加しない。
