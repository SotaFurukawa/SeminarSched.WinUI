# ADR 0001: 初期プラットフォームとアーキテクチャ

- Status: Accepted for v0.0.0; distribution and data-format decisions remain open
- Date: 2026-09-16

## Context

Python/PySide6版v1.9.5の完全移植を、独立したWindowsデスクトップアプリとして開始する。UIだけでなく、transaction、独立validator、最適化、帳票、backup/recoveryをテスト可能な境界へ分離する必要がある。

## Decision

- .NET 10.0.401、C#、WinUI 3、Windows App SDK 2.4.0を使用する。
- Windows 10 1809を最小OSとし、Windows 11を主な検証対象とする。
- UIはsingle-project MSIXのpackaged WinUI appから開始する。
- `Domain <- Application <- Infrastructure/UI`の依存方向を守る。OptimizationとReportingはUIから独立させる。
- ViewModelからSQLite、OR-Tools、帳票rendererを直接呼ばない。
- versionはルート`Directory.Build.props`を正本とし、assembly metadata、アプリ表示、build、Releaseへ伝播させる。
- リポジトリはライセンス決定までprivate運用を想定し、ReleaseはすべてDraftとする。

## Deferred decisions

次は実装前に個別ADRで決定する。

1. `.jukuschedule`直接互換またはcopy-import変換
2. SQLite access/migration方式
3. OR-Tools .NET固定versionと再現性
4. Excel/PDF library、ライセンス、日本語font、preview
5. installer、portable相当、code signing
6. Python参照実装との関係を踏まえた最終license
7. logging、crash recovery、backup保存先

## Consequences

現行のMicrosoft公式CLI templateとstable Windows App SDKを利用できる。UIなしのレイヤーは通常の.NET projectとして高速に単体テストできる。一方、MSIX配布方式とデータ互換性は未確定であり、実データを開く機能は該当ADRとcopy-first検証が完了するまで実装しない。
