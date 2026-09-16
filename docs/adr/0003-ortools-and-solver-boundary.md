# ADR 0003: OR-Toolsとsolver保存境界

- Status: Accepted for v0.1.0
- Date: 2026-09-17

## Decision

- Google.OrTools `9.15.6755`を固定する。
- v0.1.0はPython版同等性を優先し、決定論的な単一CP-SAT経路を正本とする。
- SQLiteから作る候補集合には、指導可、開校日・有効コマ、講師勤務不可、固定授業との衝突回避を反映する。
- solver出力は信用境界を越えたデータとして扱い、純粋な`ScheduleSolutionValidator`とDB正本を読む保存前validatorの両方を通す。
- 保存は未固定Assignmentだけを置換し、全検証と書込みを単一transactionで行う。固定Assignmentは変更しない。
- seed、hint、multi-stage、neighborhood repair、polishingの複数strategyはv0.2.0で同じ境界へ追加する。

## Consequences

solverがFEASIBLEな途中解を返しても、候補外配置や衝突を保存できない。ネイティブruntimeを含むためx64を正規配布対象として検証する。
