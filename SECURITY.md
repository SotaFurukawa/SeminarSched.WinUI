# Security Policy

## Supported versions

開発中は最新の`v0.x.x`だけをサポートします。正式版`v1.0.0`への移行はプロジェクト所有者の明示指示後に行います。

## Reporting a vulnerability

個人情報を含めず、再現手順、影響範囲、確認したバージョンを添えてリポジトリ所有者へ非公開で連絡してください。公開issueへ実データ、DB、ログ、生成帳票を添付しないでください。

## Development requirements

- dependency vulnerability warningをビルド失敗として扱います。
- credential、署名証明書、tokenをcommitしません。
- solver結果は独立validatorを通過後、transaction内でのみ保存します。
- DB migration、backup、restore、file出力にはfailure testを追加します。
