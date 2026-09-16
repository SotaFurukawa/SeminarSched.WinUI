# SeminarSched.WinUI

SeminarSchedのC# + WinUI 3版です。Python/PySide6版v1.9.5を読み取り専用の参照実装とし、利用者向け機能を段階的に完全移植します。

現在の開発バージョンは **v0.1.0 (beta)** です。Python版v1.9.5の全利用者向け機能を、この1つの開発versionで段階的に移植しています。

## 開発環境

- Windows 10 1809以降（Windows 11推奨）
- .NET SDK 10.0.401
- Windows App SDK 2.4.0

```powershell
dotnet restore SeminarSched.WinUI.sln
dotnet build SeminarSched.WinUI.sln --configuration Release
dotnet test SeminarSched.WinUI.sln --configuration Release --no-build
```

アプリの起動にはDeveloper Modeが必要です。

## データ保護

実在する生徒・講師の氏名、回答CSV/XLSX、`.jukuschedule`、DB、ログ、生成帳票をリポジトリ・CI artifact・Releaseへ含めないでください。テストデータは架空名または匿名IDだけを使用します。

## 文書

- [機能同等性台帳](docs/FEATURE_PARITY.md)
- [設計判断](docs/adr/0001-platform-and-architecture.md)
- [引き継ぎ](docs/CODEX_HANDOFF.md)
- [プライバシー](PRIVACY.md)
- [セキュリティ](SECURITY.md)

ライセンスはPython版のGPLv3コードを参照して再実装する際の扱いを含めて未決定です。決定までは再配布・公開を行いません。
