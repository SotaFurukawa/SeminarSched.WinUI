# Codex作業規約

このファイルは、このリポジトリ以下で作業するCodexおよび自動化エージェントに適用する。

## 最重要ルール：Python版は読み取り専用

- このリポジトリ `SeminarSched` は、既存のPython/PySide6版の参照実装である。
- Python版は **読み取り専用** とする。ユーザーが将来、明示的に凍結解除を指示しない限り、
  `src/`、`tests/`、`scripts/`、`installer/`、`packaging/`、`.github/`、設定、DB migration、
  tag、releaseを変更してはならない。
- 不具合や移植上の相違を見つけても、このPython版へ修正を入れない。内容を新しいWinUI版の
  issue、設計文書、テストへ記録して対応する。
- 実在する生徒・講師の氏名、回答CSV/XLSX、プロジェクトDB、ログ、生成帳票をGitへ追加しない。

## WinUI版の開発場所

- C# + WinUI 3版の正式名称は `SeminarSched.WinUI` とする。
- WinUI版は、このリポジトリのブランチ、サブディレクトリ、submoduleとして作らない。
- `seminarSched` と同階層の別ディレクトリに、独立したSolutionと独立した`.git`を作る。
  このリポジトリから見た推奨相対位置は `..\SeminarSched.WinUI` である。
- GitHubにも別リポジトリ `SotaFurukawa/SeminarSched.WinUI` を作成し、Python版の
  `SotaFurukawa/SeminarSched` とは履歴・release・issue・artifactを分離する。
- 新しいGitHubリポジトリを作ることはユーザーの明示要件である。ただし、作成前に同名
  リポジトリが既に存在しないか確認し、既存リポジトリを上書きしない。

## 移植原則

- [`docs/CODEX_HANDOFF.md`](docs/CODEX_HANDOFF.md)を最初に全文読む。
- Python版v1.9.5の全ユーザー向け機能を、原則として完全移植する。見た目だけの再実装や、
  最適化・検証・バックアップ・帳票の省略版で完了扱いにしない。
- Pythonコードを機械的にC#へ置換せず、ドメイン規則、データ安全性、トランザクション、
  独立validator、テスト可能な境界を維持してC#向けに設計する。
- 仕様の正本は、実装コードとテスト、`CHANGELOG.md`のv1.9.5、
  `docs/CODEX_HANDOFF.md`の順に確認する。古いREADMEやPhase文書だけを根拠にしない。
- `.jukuschedule`互換性、ライセンス、配布方式など未決事項は勝手に確定せず、設計判断を
  ADRへ記録し、必要ならユーザーへ確認する。

## 品質と安全

- 実データをテストfixture、ログ、スクリーンショット、GitHub Actions artifactへ含めない。
- テストには架空の氏名・匿名IDだけを使う。
- 最適化結果はsolverの出力をそのまま保存せず、独立validatorを通してからtransactionで保存する。
- ファイル出力、バックアップ、復元、DB migrationは失敗時に元データを壊さない原子的処理とする。
- WinUI版の各Phaseは、機能実装、単体テスト、統合テスト、UI確認、移植元との同等性確認が
  揃うまで完了扱いにしない。

## バージョンとRelease

- 開発版は`v0.x.x`とし、ユーザーの明示指示なしに`v1.0.0`へ上げない。
- 初期versionは`v0.0.0 (beta)`。bug fix・文言・UI微調整等はpatchを、新機能はminorを上げてpatchを0へ戻す。
- 原則として変更単位ごとにversionを更新し、公開済みまたはDraft Release作成済みのversionを使い回さない。
- versionの正本はルート`Directory.Build.props`とし、アプリ表示、assembly、build、GitHub Releaseへ反映する。
- versionごとにbuild/test、commit、push、GitHub Draft Release、`docs/CODEX_HANDOFF.md`更新を行う。
- Release Notesは`docs/releases/`にも保存し、何が変わったか追跡可能にする。
- ユーザーの明示指示なしにReleaseを公開しない。開発中は必ずDraftとする。
- push/Release操作は受理と即時errorの有無だけ確認し、GitHub Actionsやuploadを長時間pollしない。
- 長時間監視より実装、build、test、commit、HANDOFF更新を優先する。
