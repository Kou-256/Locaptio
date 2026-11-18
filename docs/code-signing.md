# コード署名（自己署名証明書）

Locaptio の配布物は、自己署名のコード署名証明書 `CN=Locaptio` で署名できます。

```powershell
./scripts/build-portable.ps1 -Sign
./scripts/build-setup.ps1 -AppSource <staging\Locaptio> -Sign
```

- 署名するのは Locaptio 自身のファイル（`Locaptio*.exe`・`Locaptio*.dll`、字幕連携の `bridge\Locaptio.Bridge.exe`）と Setup です。Microsoft のランタイムや .NET など、すでに他社の署名があるファイルはそのままです。
- 証明書は開発PCの「現在のユーザー」の証明書ストアにあります（初回に自動作成。秘密鍵はエクスポート不可）。公開用の証明書は `artifacts/Locaptio-codesign.cer` に書き出します。
- 署名にはタイムスタンプ（DigiCert の RFC 3161 サーバー）を付けるので、証明書の期限が切れた後も署名は有効なままです。オフラインで作るときは `scripts/sign-files.ps1 -NoTimestamp` を使います。

## できること・できないこと

- **できる**：ファイルを誰が作ったか（`CN=Locaptio`）を示し、ダウンロード後に改ざんされていないかを確かめられます（エクスプローラーのプロパティ →「デジタル署名」）。
- **できない**：自己署名の証明書は Windows が最初から信頼していないため、SmartScreen の警告や「発行元：不明」は**そのPCが証明書を信頼するまで消えません**。一般の配布で警告を出さないには、Microsoft の Trusted Signing（Azure Artifact Signing）や、認証局が発行するコード署名証明書が必要です。

## 自分のPCで信頼する場合（任意）

自分や社内のPCに限って警告を消したいときは、管理者として次を実行します。**信頼すると、この証明書で署名されたものはすべて信頼されます。** 秘密鍵を持つのは開発PCだけにしてください。

```powershell
Import-Certificate -FilePath .\Locaptio-codesign.cer -CertStoreLocation Cert:\LocalMachine\Root
Import-Certificate -FilePath .\Locaptio-codesign.cer -CertStoreLocation Cert:\LocalMachine\TrustedPublisher
```

元に戻すときは、`certlm.msc` の「信頼されたルート証明機関」と「信頼された発行元」から `Locaptio` を削除します。
