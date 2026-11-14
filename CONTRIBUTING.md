# コントリビューション

不具合報告や機能提案は [SUPPORT.md](SUPPORT.md) を参照してください。セキュリティに関する報告は [SECURITY.md](SECURITY.md) の非公開窓口へお願いします。

コードを変更する場合は、[README.md](README.md) の開発手順に従い、PowerShell 7.4以上と `global.json` に記載の.NET SDKを使用してください。初回は `pwsh -NoProfile -File scripts/Restore.ps1 -Online` で依存を復元し、変更後は `pwsh -NoProfile -File scripts/Test.ps1` を実行してください。

プルリクエストには、変更の目的、実行したテストと結果、未確認の動作を記載してください。BluetoothやXIAOを実機で試した場合は、送信方式、CPU、OS、受信側IMEと配列を添えてください。実際に送った機密本文や機器の識別情報は含めないでください。

参加者は [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) を守ってください。
