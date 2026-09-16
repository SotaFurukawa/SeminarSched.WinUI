# Feature parity inventory

参照元はPython版v1.9.5（commit `1d323a4`）の実装とテストです。状態は、画面だけ・ダミーデータだけでは「実装済み」にしません。

| Python版機能 | Python側実装 | C#側実装 | 状態 | 備考 |
|---|---|---|---|---|
| アプリ起動・version/About表示 | UI / release metadata | WinUI shell / assembly metadata | 実装済み | v0.0.0。UI実機確認は未完了 |
| NavigationView業務導線 | QML workflow shell | ①設定〜⑥出力の6段階flow | 実装中 | ②〜④・⑥は未接続機能を明示する骨格 |
| Home dashboard | workspace view model / QML | project作成・open・close UI | 実装中 | recent projectと主要導線は未実装 |
| 新規project作成 | project service | 年度・講習区分・期間・開講日を持つWinUI schema v1 | 実装中 | SQLite整合性確認後にatomic move |
| `.jukuschedule` open | project service / SQLite | WinUI schema v1の検証付きread/open | 実装中 | Python版は直接変更せず、copy-first importを別途設計 |
| 別名保存・複製 | project service | 未実装 | 未実装 | 原子的処理 |
| 最近使用・非表示 | workspace view model | 最大10件の履歴・再open・欠損時自動除去 | 実装中 | 手動非表示操作は未実装 |
| migration・整合性確認 | Alembic / project service | 未実装 | 未実装 | migration前backup必須 |
| 自動backup・世代管理 | recovery service | UIからのSQLite backup API検証付きsnapshot | 実装中 | 自動実行と世代管理は未実装 |
| 復元・復元前backup | recovery service | 確認UI・復元前3世代backup・検証・rollback付きatomic restore | 実装中 | failure injection拡充は未実装 |
| 生徒基本情報 | master repository/service | 追加・一覧UI、domain validation・SQLite CRUD | 実装中 | 編集・無効化・ID wizardは未実装 |
| 講師基本情報 | master repository/service | 追加・一覧UI、domain validation・SQLite CRUD | 実装中 | 編集・無効化・資格matrix UIは未実装 |
| 科目・略称 | master repository/service | 追加・一覧UI、domain validation・SQLite CRUD | 実装中 | 編集・無効化・校種presetは未実装 |
| 通常授業・担当優先度 | regular lesson profile | SQLite upsert・優先度1〜5・1対1必須 | 実装中 | 管理UIは未実装 |
| 共通基本情報Excel | master Excel service | 未実装 | 未実装 | 5sheet、preview、transaction |
| 講習設定 | phase2 models/view model | 期間内日付・開校/休校・日別有効コマの保存UI | 実装中 | note編集・一括操作拡充は未実装 |
| コマ設定・並べ替え | phase2 UI/service | コマ追加・表示順・時刻範囲・一覧UI | 実装中 | 編集・無効化・drag並べ替えは未実装 |
| Google Forms作成kit | questionnaire script service | 設定値入りCode.gs・READMEのatomic生成UI | 実装中 | Python版の学年分岐・全質問・診断は未移植 |
| 生徒回答・講師回答2file選択 | import UI/service | 2つのUTF-8 CSV選択UI | 実装中 | XLSXとGoogle生CSV mappingは未実装 |
| CSV UTF-8/CP932・旧形式 | importing readers | quoted UTF-8 CSV reader | 実装中 | CP932・旧形式の正規化mappingは未実装 |
| import preview/diff | importing diff | 全行参照検証・issue一覧・件数preview | 実装中 | 詳細diff・warning・削除候補は未実装 |
| import transaction/AuditLog | availability import service | SHA-256再検証・単一transaction反映 | 実装中 | AuditLog・生徒availabilityは未実装 |
| availability一括編集 | phase3 UI/service | 未実装 | 未実装 | 日付・コマ単位 |
| candidate生成 | optimization candidates | 未実装 | 未実装 | 疎な候補集合 |
| greedy初期解・complete hint | initial solution | 未実装 | 未実装 | 独立検証後hint |
| CP-SAT hard constraints | OR-Tools optimizer | 未実装 | 未実装 | 容量、資格、availability等 |
| 通常担当優先度1〜5 | objectives/constraints | 未実装 | 未実装 | v1.9.5挙動を正本とする |
| 辞書式目的・公平性 | objectives | 未実装 | 未実装 | 未配置、分散、講師公平性 |
| 高速/標準/高品質 | optimization view model | 未実装 | 未実装 | 30/120/600秒 |
| 5段階の最適化品質profile | 追加仕様 | profile catalog / slider / JSON設定 | 実装中 | strategy実装・実行中UIは未完了 |
| 高品質トーナメント探索 | 追加仕様 | 共通optimizer orchestration・候補選抜 | 実装中 | 実CP-SAT複数戦略とbenchmarkは後段へ保留 |
| 現在best採用/キャンセル分離 | 追加仕様 | execution API上で分離・部分結果test | 実装中 | UI接続とvalidator・transaction境界は未実装 |
| progress/cancel | worker / solver callback | 非同期progress DTO・cancel API | 実装中 | 実solverとUIへの接続は未実装 |
| solver独立validator | result validation | 未実装 | 未実装 | 保存前必須 |
| optimization transaction保存 | optimization run service | 未実装 | 未実装 | fingerprint照合 |
| 時間割grid | schedule editor QML | 未実装 | 未実装 | virtualization必須 |
| drag/drop手動配置 | schedule edit service | 未実装 | 未実装 | hard violation拒否 |
| manual/lock semantics | manual edit tests | 未実装 | 未実装 | 手動配置は再移動可・再最適化保持 |
| Undo/Redo | command stack | 未実装 | 未実装 | process内のみ |
| 講師一時表示・availability編集 | editor UI/service | 未実装 | 未実装 | 不可コマgray |
| 検索・scroll同期 | editor QML | 未実装 | 未実装 | header/row/grid同期 |
| 未配置・警告 | diagnostics/output | 未実装 | 未実装 | 通常担当不足を含む |
| 全体時間割Excel/PDF | reporting/output service | 未実装 | 未実装 | 日曜始まり週単位 |
| 生徒配布時間割Excel/PDF | reporting renderers | 未実装 | 未実装 | 不参加一覧を含む |
| 講師配布（学年順） | reporting renderers | 未実装 | 未実装 | A4 calendar |
| 講師配布（講師別） | reporting renderers | 未実装 | 未実装 | 講師別folder |
| 未配置・警告一覧出力 | output service | 未実装 | 未実装 | 5種類目 |
| atomic export・上書き確認 | output service | 未実装 | 未実装 | validator再実行 |
| 設定・logging | settings/logging | 未実装 | 未実装 | 個人情報を記録しない |
| 集団授業DB/service互換 | group lesson service | 未実装 | 保留 | v1.9.5同様UI停止中 |
| Windows配布・受入 | packaging/release tests | CI基盤のみ | 実装中 | installer/portable方針は未決 |

## Completion rule

各項目は、機能実装、単体テスト、統合テスト、UI確認、Python版との同等性確認が揃って初めて「動作確認済み」とする。Python v1.9.5で意図的に停止中の集団授業UIなどは、無断でscopeへ追加しない。
