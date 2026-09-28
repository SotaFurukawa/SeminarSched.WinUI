# ADR 0005: Windows配布方式

- Status: Accepted for v0.1.0。v0.3.1でSetup.exeラッパー方式を追記（Amended 2026-09-20）。
  v0.13.2でPackage.appxmanifestのバージョン同期漏れを追記（Amended 2026-09-28）
- Date: 2026-09-17

## Decision

- 配布形式はMSIXのサイドロード（サイドローディング）とする。Microsoft Storeへの登録や購入した正式コード署名証明書は使わない。
- 署名は開発者側で1回生成するローカル自己署名証明書（Subject: `CN=SotaFurukawa`。`Package.appxmanifest`の`Identity/Publisher`と一致させる）で行う。秘密鍵（.pfx）とそのパスワードは`%LOCALAPPDATA%\SeminarSched.WinUI\packaging\`に保存し、**gitへは絶対にcommitしない**（`.gitignore`は`*.pfx`・`*.msix`・`*.cer`をすでに除外済み）。
- 生成物は`dist\`（gitignore対象）へ出力する:
  - `SeminarSched.WinUI-<version>-<platform>.msix` — インストールする本体
  - `SeminarSched.WinUI.cer` — 利用者が信頼する必要がある公開証明書
- 再現用スクリプトを`scripts\`に置く:
  - `New-SigningCertificate.ps1` — 証明書が無ければ生成し、`.pfx`（ローカルのみ）と`.cer`（`dist\`）を作る。既にあれば再利用する。
  - `New-MsixPackage.ps1` — 証明書を`Cert:\CurrentUser\My`から（無ければ`.pfx`から再import して）取得し、そのthumbprintで`dotnet build -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=true -p:PackageCertificateThumbprint=... -p:UapAppxPackageBuildMode=SideloadOnly`を実行して署名付き`.msix`を作る。

## 利用者側のインストール手順

初回のみ、管理者権限で1回だけ証明書を信頼させる必要がある（`Add-AppxPackage`はマシン単位の信頼ストアを見るため、`CurrentUser\TrustedPeople`や`CurrentUser\Root`だけでは不足することを実機で確認した）。

1. 設定 → システム → 開発者向け → 「開発者モード」を有効化（既存の手順のまま）。
2. 管理者としてPowerShellまたはコマンドプロンプトを開き、以下を実行する:
   ```
   certutil -addstore -f TrustedPeople "<dist\SeminarSched.WinUI.cer への絶対パス>"
   ```
3. `SeminarSched.WinUI-<version>-<platform>.msix` をダブルクリックしてインストールする（または`Add-AppxPackage -Path <msixへのパス>`）。

## 検証状況

- 証明書生成・署名付きMSIXのビルドまでは実機で確認済み（`Get-AuthenticodeSignature`で署名者が想定どおり`CN=SotaFurukawa`であることを確認）。
- `CurrentUser\TrustedPeople`・`CurrentUser\Root`への証明書追加は非対話的に実行できたが、`Add-AppxPackage`はこれらでは信頼せず、`LocalMachine\TrustedPeople`（管理者権限が必要）でのみ成功する見込みだった。この開発環境には管理者権限がなく検証できなかったため、ユーザー環境（管理者PowerShell）で上記手順2の`certutil -addstore -f TrustedPeople`を実行してもらい、成功を確認した（2026-09-18。`証明書 "SotaFurukawa" がストアに追加されました。` / `CertUtil: -addstore コマンドは正常に完了しました。`）。
- 残る未検証項目: `.msix`本体を実際にダブルクリック（または`Add-AppxPackage`）してインストールが完了することの確認。

## Deferred

- 正式なコード署名証明書の購入、Microsoft Store配布は今後ユーザーから明示的な指示があった場合のみ検討する。
- ARM64/x86向けパッケージ生成、複数platformをまとめた`.msixbundle`化は必要になった時点で追加する。

## Amendment（v0.3.1、2026-09-20）: Inno Setupラッパー方式の追加

ユーザーから「Python版で使っていたインストーラ形式にできないか」との要望を受け、Python版の`installer\SummerCourseScheduler.iss`（Inno Setup、日本語ウィザード、管理者権限不要）と同じ体裁のSetup.exeを追加した。ただしPython版はポータブルEXEをそのままコピーするだけだったのに対し、本アプリはMSIXパッケージのため、Setup.exeは内部で「`.cer`をCurrentUser\TrustedPeopleへ登録 → `Add-AppxPackage`でインストール」を`[Code]`セクションから呼び出す薄いラッパーとして実装した（`installer\SeminarSched.WinUI.iss`、`installer\Install-Package.ps1`/`Uninstall-Package.ps1`、`scripts\New-Installer.ps1`）。

- **本アプリの実機（証明書がLocalMachine\TrustedPeopleへも既に登録済みの開発機）で、Setup.exeのインストール→`shell:AppsFolder`経由の起動→登録済みアンインストーラでの削除まで、管理者権限なし（`PrivilegesRequired=lowest`）で一通り成功することを確認した。** デスクトップアイコンのタスクを有効にした場合のショートカット動作も確認済み。
- **未解決の疑問（上記「検証状況」との矛盾）:** 本ADR冒頭の検証状況には「`CurrentUser\TrustedPeople`だけでは`Add-AppxPackage`の信頼として不足することを実機で確認した」との記載があるが、今回の検証は`LocalMachine\TrustedPeople`にも同じ証明書が既に登録済みの状態で行っており、`CurrentUser\TrustedPeople`単独で十分かどうかを完全には切り分けられていない（`LocalMachine`側の証明書を管理者権限なしで一時的に削除できず、切り分け検証ができなかった）。Microsoft公式のsideloadガイドでは`CurrentUser\TrustedPeople`のみで per-user の`Add-AppxPackage`は成立するはずだが、本ADR記載時点の過去の失敗がDeveloper Mode未有効化など別要因だった可能性も残る。
- **推奨される次の検証:** 一度もこの証明書を信頼していない別のWindows PC（またはこの証明書をLocalMachineから削除できる管理者環境）でSetup.exeを実行し、`PrivilegesRequired=lowest`のままで実際にインストールが成功するかを確認する。失敗する場合は、`installer\SeminarSched.WinUI.iss`の`PrivilegesRequired`を`admin`に変更し、`Install-Package.ps1`の登録先を`Cert:\LocalMachine\TrustedPeople`（`Import-Certificate -CertStoreLocation Cert:\LocalMachine\TrustedPeople`、要管理者権限）へ切り替える。
- Draft Releaseへの`.msix`/`.cer`/Setup.exeの添付は、上記の実機インストール確認が取れたため開始した（v0.3.1から）。

## Amendment（v0.13.2、2026-09-28）: Package.appxmanifestのバージョン同期漏れ

ユーザー報告「0.13.1をGitHubからインストールしようとしたら、失敗してしまい、エラーコード1がでる」を
実機（開発機）で調査した。

- **直接の原因（開発機固有、コード上のバグではない）:** この開発機で`dotnet run`を多数回実行していた
  ため、Windowsが同じPackage Identity（`F70149DC-0D08-4E3F-B67F-189A3A5E1C51`）を
  「パッケージ化されていない開発モードのアプリ」として登録済みだった（`Get-AppxPackage`で
  `IsDevelopmentMode: True`、`SignatureKind: None`、`Version: 0.9.0.0`）。この状態へ署名済みの
  `.msix`を`Add-AppxPackage`しようとすると、Windowsは「現在のユーザーが、このアプリの
  パッケージ化されていないバージョンを既にインストールしています。これをパッケージ化された
  バージョンに置き換えることはできません」（HRESULT 0x80073CFB）を返し拒否する。この登録を
  `Remove-AppxPackage`で削除すれば解消する。エンドユーザー（`dotnet run`を実行しない）には
  発生しない、この開発機だけの事象。
- **副次的に発見したコード上の実バグ:** 上の調査中に、ビルド済み`.msix`のAppxManifest.xmlを直接
  展開して確認したところ、ファイル名は`SeminarSched.WinUI-0.13.1-x64.msix`なのに、埋め込まれた
  `Identity/@Version`は`0.9.0.0`のままだった。`src\SeminarSched.WinUI\Package.appxmanifest`の
  `Identity/Version`は単なる静的なXML属性であり、`GenerateAppxPackageOnBuild`（single-project
  MSIX packaging）はこれをそのままパッケージ化するだけで、`Directory.Build.props`の
  `AppxPackageVersion`プロパティからは一切反映されない。つまりv0.9.1からv0.13.1までのすべての
  Draft Releaseの`.msix`が、ファイル名・About画面の表示こそ正しいバージョンを示していたものの、
  Windowsパッケージマネージャーが実際に識別するIdentity Versionは一貫して`0.9.0.0`のまま出荷され
  続けていた（`Directory.Build.props`をversionの正本とする方針が、このファイルにだけ届いて
  いなかった）。
- **修正:** `scripts\New-MsixPackage.ps1`に、`dotnet build`実行前に`Directory.Build.props`の
  `VersionPrefix`を読み取り`Package.appxmanifest`の`Identity/Version`（`{version}.0`）へ同期する
  処理を追加した。あわせて`Package.appxmanifest`自体も`0.13.2.0`へ更新し、
  `RepositoryPolicyTests.PackageAppxManifest_IdentityVersionMatchesCentralVersion`という回帰
  テストを新設して、以降このズレが起きても即座にテストで検知できるようにした。
- **実機確認:** 開発機の`dotnet run`由来の開発モード登録を削除した上で、修正後にビルドした
  v0.13.2の`.msix`を`Add-AppxPackage`し、`Get-AppxPackage`でVersionが正しく`0.13.2.0`になって
  いることを確認した。
