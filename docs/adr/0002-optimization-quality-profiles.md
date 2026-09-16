# ADR 0002: 5段階の最適化品質profileと共通評価

- Status: Accepted for v0.1.0
- Date: 2026-09-17

## Context

Python版v1.9.5の高速・標準・高品質presetを移植しつつ、利用者が計算時間と探索品質を5段階で直感的に選べるようにする。品質を上げても単に制限時間だけを延ばすのではなく、複数候補、hint再探索、上位候補への時間集中、部分修復、最終polishingを段階的に増やす必要がある。

## Decision

- `OptimizationProfile`を品質レベルごとの時間予算、停滞終了時間、探索stage、候補数を持つ唯一の設定源とする。
- Level 3を初回既定値とし、最後に使ったlevelをapp settingsへ保存する。
- Level 4〜5は探索候補を均等に最後まで回さず、探索stageで上位候補を選び、改善・修復・polishingへ時間を集中する。
- 全strategyの結果を`ScheduleEvaluation`で辞書順比較する。hard violation、未配置、重要希望違反、主要penalty、講師空き、分散、その他soft penalty、objectiveの順を崩さない。
- solver結果は共通validatorを通すまで採用候補にしない。
- 「現在の最良解を採用」と「キャンセル」を分離する。前者は検証済みbestをtransaction保存し、後者は計算前状態を維持する。
- 進捗は不正確な完了率ではなく、経過/最大時間、stage、strategy数、改善回数、best評価を中心に通知する。

## Consequences

品質levelとsolver strategyを疎結合に保ち、実績に基づく将来の適応的な時間配分へ拡張できる。実際のCP-SAT parameterと時間配分はPython版scenarioに対するgolden testとbenchmarkを通して調整し、profile表示時間は保証ではなく目安として扱う。
