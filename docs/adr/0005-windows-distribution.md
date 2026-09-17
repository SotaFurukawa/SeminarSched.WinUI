# ADR 0005: Windows配布方式

- Status: Accepted for v0.1.0
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
- Draft Releaseへの`.msix`/`.cer`添付は、実インストール確認が取れてから行う。
