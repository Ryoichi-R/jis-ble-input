# JIS BLE Input 1.12.0

## 概要

送信Surfaceで確定した日本語・英数字の文章を、Bluetoothキーボードの操作として別のWindowsへまとめて入力するソフトです。受信側へのソフト導入・コマンド実行は不要です。内蔵Bluetooth方式に加え、USB接続したXIAO nRF52840をBLEキーボードにする方式を選べます。

## 動作要件

配布アプリはWindows ARM64のSnapdragon Surface向けで、.NET 10.0.14を同梱します。受信側はBluetoothキーボードを接続できるWindowsと、既存のキーボード・マウスを用意してください。日本語入力はMicrosoft IMEのローマ字入力を前提とします。XIAO方式にはXIAO nRF52840 Senseとデータ通信対応USB-Cケーブルが必要です。内蔵方式は送信PCのBluetooth周辺機能に依存します。確認済み範囲は後述の「対応範囲と確認状況」を参照してください。

## インストール

ZIPの配布元とSHA-256を確認し、全体を新しいフォルダーへ展開してください。インストーラーやWindows起動時の常駐登録はありません。旧版をトレイから終了してから、新しいフォルダーの`JisBleInput.exe`を起動します。未署名の注意と初回操作は「起動と入力」、XIAOへの初回FW書込は[基板の説明書](firmware/xiao-nrf52840/README.md)を参照してください。

## 使用方法

受信PCを登録して送信先を選び、受信側の空のメモ帳へフォーカスを合わせ、まず短文で試してください。本文を確定して「送信」を押し、異常時は「停止」を使います。「クリア」は送信側の本文欄を空にし、「ペースト」はカーソル位置または選択範囲にクリップボードの文字を貼り付けます。具体的なIME・配列・改行の条件と手順は「起動と入力」に記載しています。

## 設定

右上の「設定」から送信方式、接続先、受信側のJIS/US配列、IME準備、自動接続待ちを選びます。本文・入力方法・速度は主画面で操作します。設定の保存範囲は「一時停止・続行・保存」を参照してください。XIAO方式を選ぶときは、設定画面の基板と送信先を確認してください。

## アンインストール

送信を停止し、トレイの「終了」で完全に終了してから、展開したアプリのフォルダーを削除します。アプリの削除だけではWindowsのBluetoothペアリングや基板の登録を解除しません。不要になった接続先はWindowsの標準Bluetooth設定で対象を確認して解除してください。XIAOの登録解除・FWの復旧は[基板の説明書](firmware/xiao-nrf52840/README.md)に従い、bootloaderやSoftDeviceをまとめて上書きしないでください。

利用者別の設定も消す場合は、アプリを終了した状態で`%LOCALAPPDATA%\JisBleInput`を削除します。このフォルダーには送信方式・接続先の識別用ハッシュ・入力方法などを保存し、送信本文は保存しません。

## 既知の制限

入力先のフォーカスとIME状態は受信側で確認する必要があります。Enterで投稿・検索・保存が実行される欄へ改行を含む本文を送らないでください。停止や切断で、すでに入力した文字を自動削除しません。全PC・IMEでの動作保証はなく、内蔵Bluetooth方式とXIAO方式で制約が異なります。対応範囲と未確認項目は「対応範囲と確認状況」と[VALIDATION.md](VALIDATION.md)を参照してください。

## 1.12.0のXIAO速度設定

| 入力方法       | 遅め   | 標準   | 最速    |
| -------------- | ------ | ------ | ------- |
| 日本語・混在文 | 0.10秒 | 0.05秒 | 0.025秒 |
| 英数字・URL    | 0.05秒 | 0.01秒 | 0.005秒 |

数値は1文字あたりの固定待機時間です。通信とOSの待ち時間は別途加わります。初回は両モードとも標準を選び、以後はモードごとの選択を保存します。実測時間と受信結果の比較方法は[速度確認の手順](docs/XIAO-SPEED-TRIAL.md)を参照してください。

## 1.7.1の修正

停止後の表示にキー解放の結果を保持し、受信側の状態を確認できない場合はその旨を表示します。XIAOの成功応答では状態情報の長さ・内容を検証し、不正な応答を受けた場合は接続を閉じます。自動再送は行いません。

## 1.7.0で追加したXIAO（USB接続）方式

「設定」→「送信方式」で「XIAO（USB接続）」を選び、基板を選択して「PCを追加」を押します。受信Windowsで60秒以内に`JIS BLE Keyboard XXXX`を追加し、アプリの送信先を選びます。以後は「再接続」で登録済みPCを待ちます。切断後に本文を自動再送しません。

必要なのはXIAO nRF52840 Senseとデータ通信対応USB-Cケーブルです。初回は同梱の基板用FW 0.1.0を書き込みます。[書き込み・復旧・初回確認](firmware/xiao-nrf52840/README.md)を先に読んでください（配布ZIPとsourceの両方に同じパスで含まれます）。通常版/Plusは対象外で、設定の基板一覧では書込モード（PID 0045）と同様に「接続不可・対象外」と表示し、接続しません。基板ID・FW版・キー定義が対応しない場合は送信しません。登録先変更は「基板の登録を解除」で1件だけ解除します。

XIAO方式はSurfaceのBluetoothをオン/オフしません。トレイ格納中もUSBとPINGを維持し、終了時は基板の停止・解放とUSB closeを行います。既存の内蔵方式は引き続き選択できます。設定なしの場合の初期値も内蔵方式です。本文・速度・配列はUSBのARM待ちより前に固定し、待機中にも「停止」が使えます。

利用者の選択によりHMACは使わず、送信PC上でUSBを操作できるプロセスを信頼します。アプリが開いていない間は別プロセスも基板を操作できます。BluetoothのJust Worksによる暗号化と、相手のMITM認証は別です。本文・キー列・bond鍵はアプリの設定やログへ保存しません。

2026-11-05〜06にXIAO nRF52840 Senseへの実書込、受信Windowsでのキーボード分類（再起動後を含む）、日本語の全文一致、停止・障害時のキー解放と手動再送を利用者が確認しました。実機必須受入4件はPASSです。これはすべてのPC・IME条件での動作保証ではありません。確認状況は[VALIDATION](VALIDATION.md)、通信契約は[USB protocol](docs/XIAO-PROTOCOL.md)を参照してください。

## 起動と入力

**Windowsアプリは未署名です。** 配布元とZIPのSHA-256を確認してください。Windowsが起動を警告する場合は、ファイルの出所を確認してから実行を判断してください。SmartScreenやウイルス対策を全体的に無効化する必要はありません。

1. 配布されたWindows ARM64用ZIPを、送信するSnapdragon Surfaceの新しいフォルダーへ全体展開し、`JisBleInput.exe`を開きます。ZIPのファイル名は候補ごとに異なります。受信PCには配置しません。旧版はトレイの「終了」から先に終了してください。
2. 登録済みPCからの接続待ちを自動開始します。初回・追加の登録は「設定」→「PCを追加」を押し、受信PCの標準Bluetooth設定で追加します。接続先を選びます。次回は、前回選んだPCの接続・HID購読が戻ると送信先を復帰します。
3. 「入力方法」「入力速度」を選びます。英数字モードでは「設定」で受信Windowsが適用する配列（JIS/US）も合わせます。受信側の入力先をクリックし、日本語ならMicrosoft IMEのローマ字入力、英数字ならIMEオフ（A）を確認します。CapsLockはオフ、未確定文字なしにします。設定画面の「受信側のIMEを準備」はモード指定だけで、本文・F5・Enterは送りません。
4. 送信欄に文章全体を確定入力します。確認チェックボックスはありません。「改行」の回数と、文字数欄のツールチップにある先頭・末尾改行の情報を確認してください。改行は通常Enterとして送るため、改行を含む文章はメモ帳などEnterで改行する複数行欄だけに送ります。検索・投稿・保存を実行する欄には送らないでください。
5. 「送信」を1回押すか、Ctrl+Enterを押します。受信側で別の操作やフォーカス変更をせず、完了後に全文を確認します。コード・日時・再読み込み等の異常があれば「停止」を押します。入力済み内容や未確定コードの自動削除・自動再送はしません。

まずメモ帳の空の文書に初期値`日本語ABC橋。`をまとめて送り、コード・日時・余分な改行がないことを確認します。1文字ずつ人が確定する操作はありません。

## 1.6.5: 送信失敗時の表示と接続先の識別

- 送信が例外で停止した場合も、キー解放の操作を送信できたかを表示します。解放未確認なら、受信側の既存キーボード等でキー・IME・入力欄を確認してください。操作の送信成功だけでは受信側の実状態を保証しません。
- 同名PCを区別できるよう、接続先に識別用IDを8桁で表示します。接続先へマウスを置くと、完全なSHA-256を確認できます。PC名を変更しても同じ機器IDなら表示IDは維持します。初回の対応付けはPCを1台ずつ接続して確認します。
- 日本語速度、全文一括入力、440×480の画面、専用アイコン、トレイ格納は維持します。

## 1.6.4: コンパクトUIと専用アイコン

- 通常画面は幅440×高さ480、最小360×380（Windowsの論理ピクセル）です。送信先・方法・速度・本文・送信だけを中心に配置し、本文欄をリサイズに合わせて伸縮させます。
- 一時停止・停止は送信中に表示し、一時停止中は主ボタンが「続きから送信」へ切り替わります。Enterは改行、Ctrl+Enterは送信/続行、Tabは次の項目への移動です。
- ペアリング追加・再接続・接続先更新・受信配列・IME準備・自動接続待ち・詳しい診断は、右上の「設定」で開く別画面へ移しました。機能自体は削除していません。設定を閉じても本文と主画面のサイズは保ちます。
- 文字数と改行数は常時表示します。先頭/末尾の改行情報は文字数欄のツールチップ、接続診断は状態表示のツールチップか設定画面で確認できます。送信失敗・途中停止・キー解放未確認は主画面にも表示し、長い文言はツールチップで全文を確認できます。
- 4案から選択されたA案「キーボード＋無線」のオリジナルアイコンを、EXE・ウィンドウ・トレイに統一して使用します。16〜256pxの8サイズをICOに含め、表示倍率に合わせます。
- 日本語の速度・キー列・送信方式と.NET 10.0.14は維持しています。

## タスクトレイへの格納と終了

- 右上の×を押すと画面を隠し、時計の近くのタスクトレイへ格納します。アイコンが見えない場合は、通知領域の隠れているアイコン一覧（通常は「^」）を開いてください。
- 格納中もBluetoothの接続待ち・接続状態・送信処理を維持します。×ではBluetoothをオフにせず、送信の取消や本文の消去もしません。送信を止める場合は画面を開いて「停止」を押します。
- トレイアイコンのダブルクリック、または右クリック →「画面を開く」で元の画面に戻ります。EXEをもう一度起動したときも既存画面へ表示要求を送ります。
- 完全に終了するときは、トレイアイコンを右クリック →「終了」を選びます。この場合だけ従来の接続終了・Bluetooth復帰確認を行います。終了後はトレイアイコンを取り除きます。
- Windowsのサインアウト・シャットダウンでは、トレイへ格納する処理で終了を妨げません。Windows起動時の自動常駐登録は追加していません。
- 日本語の速度・キー列、保存する設定、.NET 10.0.14は1.6.2のままです。

## 1.6.2から継続する修正

- Bluetoothをオフにした後の終了処理で、オン要求の受理と実際のオン状態を確認します。オフ要求の完了前にオン要求が追い越さないようにします。復帰を確認できない場合は終了前に「OK」で再試行できます。「キャンセル」で終了する場合は、Windowsの設定 → BluetoothとデバイスからBluetoothをオンにします。
- 切断待ちが時間切れになった場合は、切断確認済みとして扱わず案内します。接続中にトレイメニューの「終了」を選ぶとSurfaceのBluetoothを短時間オフにするため、そのSurfaceのマウス・キーボード・イヤホン等も一時切断されます。
- この環境で広告開始が成立しなかった「新規発見を終了」を画面から外しました。接続待ちは新規発見可能な広告を使い、ペアリングの承認はWindows標準機能に従います。
- 1.6.2以降の複数起動を同じ利用者セッション内で防ぎ、2回目の起動は既存ウィンドウへ表示要求を送ります。旧版はこの制御に参加しないため、先に終了してください。
- .NET / Windows Desktop runtimeとARM64 hostを10.0.14へ固定します。説明書、試験サンプル、上流MIT license、実際の同梱runtimeのlicense/noticesを梱包し、EXE内のruntime版と必要ファイルを検査します。
- 日本語の速度・待機値・キー列は1.6.1のまま維持します。旧標準速度を選択肢へ戻す変更は行っていません。

## 入力方法と速度

PC内蔵Bluetooth方式の固定待機時間です。XIAOの速度は「1.12.0のXIAO速度設定」を参照してください。

| 入力方法       | 入力速度 | 1文字の固定待機 |
| -------------- | -------- | --------------- |
| 日本語・混在文 | 標準     | 1.10秒          |
| 日本語・混在文 | 少し速め | 0.90秒          |
| 日本語・混在文 | 速い     | 0.70秒          |
| 英数字・URL    | ゆっくり | 0.12秒          |
| 英数字・URL    | 高速     | 0.08秒          |
| 英数字・URL    | 最速     | 0.05秒          |

Bluetooth/APIの待ちは別途加わります。英数字の開始・続行時の準備に0.30秒、改行に0.35秒の固定待機があります。実機の文字/秒を示す値ではありません。乱れる場合は選べる範囲で遅い速度へ変更します。

日本語は各文字の前にLANG1（ImeOn）とCtrl+CapsLockでひらがなモードを指定し、4桁16進コード→F5→Enterで変換・確定します。英数字はLANG2（ImeOff）で準備し、受信配列に対応した直接キー入力を使います。英数字モードの非ASCIIは全文を送信前に拒否します。

## 一時停止・続行・保存

- 「一時停止」は現在の1文字の変換・確定とキー解放後に止まります。すぐ中断する場合は「停止」を使います。一時停止後は受信側の入力欄とIMEを確認して「続きから送信」を押します。
- 送信中・一時停止中は開始時の文章、方法、速度、配列、宛先・接続世代を固定します。編集欄の変更は次回分です。再接続先へ残りを自動送信しません。
- 本文・キー列は保存しません。方法・速度・配列を`%LOCALAPPDATA%\JisBleInput\input-preferences.json`へ、自動接続待ち設定と前回PCのIDのSHA-256を`connection-preferences.json`へ保存します。速度は送信方式と入力方法ごとに保存します。名前・生の機器ID・ペアリング認証情報は保存しません。

## 対応範囲と確認状況

2026-11-06までに、XIAO nRF52840 Sense（FW 0.1.0）で実機受入の必須4項目を確認しました。受信Windowsでのキーボード分類と再起動後の維持、日本語・混在文の入力、停止・キー解放、USB抜去・アプリ強制終了・BLE切断からの復帰が対象です。検証に使用したZIPは `jis-ble-input-1.7.0-xiao-preview.7-win-arm64.zip` です。試験範囲は[VALIDATION](VALIDATION.md)、通信契約は[USB protocol](docs/XIAO-PROTOCOL.md)を参照してください。

最大2,000送信単位です。CRLFは1単位、LF/CRも改行として扱い、先頭・末尾・連続改行を削除しません。日本語は対応するBMP文字、英数字は印字可能ASCII U+0020〜U+007Eを対象とし、Tab・補助平面文字・結合文字・書式制御文字等は全文を送信前に拒否します。欄によって内部改行コードが異なるため、LF/CRLFの厳密な保持は保証しません。

前版の基本一括入力にはメモ帳・Google側での正常動作の利用者報告がありますが、具体的な試験本文、OS/IME版、IME互換設定は未取得です。トレイ格納・復帰・明示終了はBluetoothを起動しないWindows UIテストで確認しています。接続相手を伴うトレイ滞在・再接続・終了時のラジオ復旧・長文・全記号の実機受入は未実施です。キー列の送信成功は、受信側のIME変換成功や本文完全一致の自動確認を意味しません。

同梱`samples/`にASCII URL、印字可能ASCII95文字、CRLF/LFの4行文、100文字/1,000文字の試験文があります。検索・投稿を実行しない空の文書で使い、受信結果を全文比較してください。

2026-11-02の利用者指示により、未実施の実機受入を別プランや完成待ちゲートとして管理せず、実際の運用で不具合が出た場合に都度修正する方針です。これは未実施試験を合格へ変更するものではありません。新規発見可能な広告を継続する制約も残り、アプリ単独で新規ペアリング受付を操作時だけに限定したとは扱いません。OSの承認と、アプリの選択PCへの送信先固定を維持します。

## ビルドと梱包

リポジトリのルートでPowerShell 7.4以上を使用します。.NET SDK 10.0.204は`global.json`で固定しています。Windows x64またはARM64でビルドでき、配布アプリの対象はWindows ARM64です。`NuGet.Config`はオフライン設定です。初回だけ`Restore.ps1 -Online`で公式NuGetからlockfileに一致する依存を取得します。

```powershell
pwsh -NoProfile -File scripts/Restore.ps1 -Online
pwsh -NoProfile -File scripts/Test.ps1 -NoRestore
pwsh -NoProfile -File scripts/Prepare-FirmwareTools.ps1
pwsh -NoProfile -File scripts/Test-XiaoContract.ps1
pwsh -NoProfile -File scripts/Test-Firmware.ps1
pwsh -NoProfile -File scripts/Build-Firmware.ps1
pwsh -NoProfile -File scripts/Publish.ps1 -RestoreSource https://api.nuget.org/v3/index.json
pwsh -NoProfile -File scripts/Test.ps1 -NoRestore -Package
```

復元済みの場合は`Publish.ps1 -NoRestore`を使えます。`-PackagesPath`でプロジェクト内のNuGet保存先、`-FallbackPackagesPath`で既存キャッシュの参照先を指定できます。スクリプトは元EXEを`_internal/backups/`へハッシュ照合付きで退避し、指定されたpublishフォルダーを更新します。検査済みZIPは`_internal/dist/`へ生成し、同名ZIPは上書きしません。`manifest.json`に全同梱ファイルのSHA-256を記録します。単独の`dotnet publish`ではruntime noticesの収集・配布検査を完了しません。

C# Coreの行coverageは`dotnet-coverage` 17.14.1を`_internal/tools/coverage`へローカル導入し、`scripts/Test-CoreCoverage.ps1`で計測します（90%以上、計測用構成は配布用と別）。導入例: `dotnet tool install dotnet-coverage --version 17.14.1 --tool-path _internal/tools/coverage --configfile NuGet.Config --add-source https://api.nuget.org/v3/index.json`。

`Test.ps1`はWindowsでCore・能力診断・UIの各チェックをビルドして実行します。復元済みなら`-NoRestore`を付けられます。UI試験はBluetoothを開始せず、本文を送信せず、専用の一時設定を使います。`-Package`は現在の版をpublishした後に指定し、配布検査にPester 5.7.1以上を使います。ログと結果は`_internal/build-cache/validation/`へ保存します。独自のチェックプログラムのため、`dotnet test`の終了コード0だけで成功としません。C#静的解析には`dotnet format analyzers src/JisBleInput.App/JisBleInput.App.csproj --verify-no-changes --no-restore`を使います。単なる書式検査を意味解析やカバレッジ測定の代わりにしません。

## 来歴・資料

HID descriptorのキーボード部分は[BleHid v0.6.0](https://github.com/abhishek-raj/windows-ble-hid/blob/v0.6.0/src/BleHid.Core/HidDescriptors.cs)由来です。マウス部分を除外しJIS/IME usageを拡張しています。原著作権・MIT許諾文を`licenses/windows-ble-hid-LICENSE.txt`に保持します。上流プログラムは同梱・実行していません。

APIの根拠は[Microsoft GATT Server](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-server)、[対象宛先への通知](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattlocalcharacteristic.notifyvalueasync)、[日本語IMEキー定義](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/keyboard-japan-ime)、[Radio.SetStateAsync](https://learn.microsoft.com/en-us/uwp/api/windows.devices.radios.radio.setstateasync)です。

開発・受入の記録は[VALIDATION.md](VALIDATION.md)、旧版の説明と経緯は開発用チェックアウトの`docs/HISTORY.md`を参照してください。配布ZIPには開発履歴を含めません。

## ライセンス

独自コード・素材は[MIT](LICENSE)、第三者の条件は[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)に従います。ソースと配布物は[GitHub](https://github.com/Ryoichi-R/jis-ble-input)、通常の不具合・要望は[Issues](https://github.com/Ryoichi-R/jis-ble-input/issues)を参照してください。脆弱性の非公開報告は[SECURITY.md](SECURITY.md)の手順に従ってください。
