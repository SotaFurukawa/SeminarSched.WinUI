# ADR 0011: データの保存先（workspace）を変更できるようにする

- Status: Accepted for v1.6.0
- Date: 2026-10-17

## 背景・ユーザーの要望

v1.5.6で、共通名簿Excel（基本情報xlsx）が反映・学年繰り上げ後に消えてしまう不具合を修正した
直後、ユーザーから続けて以下の要望があった。

> また、こういう不具合が発生したときのためにアンインストールボタンを追加したい。設定の
> アップデートの下にアンインストールを追加してください。
> また、インストールするときに保存先をどこにするかなどを指定できるようにすれば解決すると
> 思います。

アンインストールボタンは要望通りそのままv1.5.6で追加した。「保存先を指定できるようにする」に
ついては、実際の不具合の根本原因はインストール場所ではなくコードの不具合（反映・学年繰り上げ
後にExcelを削除するだけで再生成していなかった）だったことを説明したうえで、それでも機能として
別途作るか確認した。ユーザーの回答:

> インストール時にデフォルトの保存先を指定しておき、workspaceを置く場所を変更できるように
> する。デフォルトの場所はあなたが指定した場所です。

## Decision

### 既定の保存先は変更しない

既定の保存先は従来通り`%LOCALAPPDATA%\SeminarSched.WinUI`のまま維持する（ユーザーの回答
「デフォルトの場所はあなたが指定した場所です」に従い、こちらで決定）。

### 「変更できるようにする」は設定ページから行う（インストーラーの新しいウィザード画面は作らない）

インストーラー（Inno Setup）に保存先選択用の新しいウィザード画面を追加することは、今回は
**行わなかった**。理由:

1. 「設定ページから保存先を変更できる」機能があれば、新規インストール直後（まだ実データを
   取り込む前）にその場で変更すれば、実質的にインストール時に選ぶのと同じ結果になる。
2. Inno SetupのPascalスクリプトでのウィザード画面追加は、この開発環境では対話的なインストーラー
   画面を実際に動かして確認する手段が無く、検証が難しい。アップデート時にSetup.exeを再実行する
   既存の自動更新フロー（`PerformUpdate_Click`）にも影響するため、既存インストールを壊すような
   後方互換性のミスを避けたい。
3. ユーザーの回答の後半（「workspaceを置く場所を変更できるようにする」）が明確な要求であるのに
   対し、前半（インストール時に指定）は「デフォルトの場所はあなたが指定した場所」という記述と
   合わせて読むと、既定値の決定自体をこちらに委ねる文脈と解釈できる。

この判断はユーザーへ報告済み。インストーラー側のウィザード画面が別途必要であれば、改めて
明示的な指示を受けて対応する。

### 設計: 「目印ファイル」＋「次回起動時に反映」方式

- 既定の保存先の中の固定パス（`%LOCALAPPDATA%\SeminarSched.WinUI\workspace-root.txt`）に、
  実際の保存先（空なら既定のまま、値があればその場所）を書いた「目印ファイル」を置く。この
  目印ファイル自体の場所は、保存先をどこへ変更しても絶対に動かさない（最初に探す場所が動くと
  次回起動時に見つけられなくなるため）。
- `WorkspaceRootResolver.Current`がこれを読み、以後`ProjectService`（Projects/Backups）・
  `WorkspacePaths`（WinUI、Output/Forms）・`SharedRosterStore`（SharedRoster）・`App.xaml.cs`
  （settings.json/logs）が、すべてこの`Current`を起点にパスを組み立てる。
- 実行中のアプリ自身がログファイルやSQLite DBを開いたまま移動（コピー）すると、ファイルが
  使用中で失敗したり内容が半端になったりする恐れがあるため、**移動は即座には行わない**。
  設定ページの「保存先を変更する」ボタンは、移動先フォルダーの検証（空のフォルダーであること
  等）と、別の「保留中」マーカーファイル（`pending-workspace-move.txt`）への書き込みだけを行い、
  実際のコピーは**アプリを終了して次回起動した最初の瞬間**（`WorkspaceRootResolver.Current`が
  最初に参照され、他の何もまだ開いていない状態）に行う。
- 移動は「すべてコピーできたことを確認してから目印ファイルを書き換え、最後に元データを削除する」
  順序で行う（`WorkspaceRootResolver.Move`）。コピーの途中で失敗しても目印ファイルはまだ古い
  値のままなので、今までの場所が使われ続けるだけで、データは壊れたり失われたりしない
  （AGENTS.mdの「ファイル出力等は失敗時に元データを壊さない原子的処理とする」に従う）。
  コピーに失敗した場合のエラーは`WorkspaceRootResolver.LastMoveError`に記録し、設定ページが
  次回起動後に一度だけユーザーへ知らせる。

## 影響範囲

- `src/SeminarSched.Application/Settings/WorkspaceRootResolver.cs`（新規）: 上記の解決・移動
  ロジック。
- `src/SeminarSched.Application/Projects/ProjectService.cs`: `EnsureDirectory`が
  `WorkspaceRootResolver.Current`を起点にするよう変更。
- `src/SeminarSched.WinUI/WorkspacePaths.cs`: 同様。
- `src/SeminarSched.Infrastructure/MasterData/SharedRosterStore.cs`: 既定の`directory`引数が
  同様。
- `src/SeminarSched.WinUI/App.xaml.cs`: `SettingsStore`/`Logger`のパスが同様。
- `src/SeminarSched.WinUI/Pages/SettingsPage.xaml`/`.xaml.cs`: 「データの保存先」欄（現在の
  保存先の表示、「保存先を変更する」ボタン、`Microsoft.Windows.Storage.Pickers.FolderPicker`
  によるフォルダー選択、確認ダイアログ、アプリ終了）を追加。

## 検証について

- `WorkspaceRootResolver.Move`（経路に依存しない純粋なコピー処理）について、ネストした
  ファイル・フォルダーのコピーと元データの削除、目印ファイルの除外、移動元が存在しない場合に
  例外を投げないことを検証する単体テストを追加した。
- `WorkspaceRootResolver.Current`・`RequestMove`・`ApplyPendingMoveIfAny`自体（実際の
  `%LOCALAPPDATA%`を読み書きするOS依存の経路）は、既存の`ProjectService.
  DefaultProjectsDirectory`等と同じ理由で単体テストの対象外とした（この種のOS固定パスへの
  読み書きは、このコードベースでは一貫して単体テストしていない）。
- 実機での「保存先を変更する」→アプリ終了→再起動→実際にデータが新しい場所へ移動していることの
  確認は、この開発環境で実データを使った確認がしづらいため、本バージョン作成時点では行えていない
  （既知の制約）。
