# ADR 0004: Excel/PDF帳票ライブラリ

- Status: Accepted for v0.1.0
- Date: 2026-09-17

## Decision

- ExcelはClosedXML 0.105.1、PDFはPDFsharp-MigraDoc 6.2.4を固定する。いずれもMIT license。
- DBから切り離した`ScheduleReport`を共通入力とし、rendererへSQLite objectを渡さない。
- PDFはWindows同梱の日本語TTFをfont resolverで埋め込む。生成testでPDF signatureと実ファイル生成を確認する。
- 出力前にSQLite integrityを再確認し、新規一時folderですべての形式を生成してから最終folderへ移動する。

## Deferred

Python版の生徒別・講師別配布帳票、カレンダー改ページ、logo、previewは同じsnapshot境界上へ追加する。
