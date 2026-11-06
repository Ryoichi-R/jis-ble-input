# 同梱ライセンス

- `windows-ble-hid-LICENSE.txt`: HID descriptorの参照元BleHidのMIT許諾文。
- `windows-desktop-runtime/`: .NET 10.0.14のWPF / WinForms公式タグから取得した第三者notice原文。取得元とSHA-256は`sources.json`に記録する。runtime更新時は同じ版へ同期し、梱包時に版とハッシュを検査する。
- 配布時の`dotnet-runtime/`と`dotnet-host/`、Windows Desktopの`LICENSE.TXT`は、実際に復元した同じ版のMicrosoft公式NuGetパックから`Publish.ps1`が収集する。Windows DesktopのNuGetパックは`LICENSE`というファイル名で、第三者noticeは含まれないため上記を補う。

第三者の著作権・許諾文は変更しない。これらはアプリ独自コードのライセンスを決めるものではない。
