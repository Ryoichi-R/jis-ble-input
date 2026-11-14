# JIS BLE Input 1.20.1 Mac受信試験候補

## 概要

送信PCで確定した日本語・英数字の文章を、Bluetoothキーボードの操作として別のWindowsへまとめて入力するソフトです。受信側へのソフト導入・コマンド実行は不要です。Windows x64は内蔵Bluetooth方式、Windows ARM64は内蔵Bluetooth方式とUSB接続したXIAO nRF52840 Sense方式を使えます。1.16.0の実機受入状況は[VALIDATION.md](VALIDATION.md)を参照してください。

## 動作要件

送信側のCPUに合うWindows ZIP（`win-x64`または`win-arm64`）を使用します。両方に.NET 10.0.14を同梱します。内蔵方式には送信側のBLE Peripheral対応が必要です。受信側はBluetoothキーボードを接続できるWindowsと既存のキーボード・マウスを用意してください。日本語入力はMicrosoft IMEのローマ字入力を前提とします。

## Macで日本語・混在文を受信する

Macの「システム設定」→「キーボード」→「テキスト入力」の「編集」から「Unicode 16進数入力」を追加します。Windowsの送信アプリでは「日本語・混在文」を選び、「設定」の「日本語の受信側」を「Mac（Unicode Hex Input）」に切り替えます。Macで受信する入力欄にカーソルを置き、最初は標準速度で短文を送ります。

Mac向けは1文字ずつOptionと4桁の16進コードで入力します。本文の改行だけをReturnで送ります。Mac向けの速度と受信方式は起動中に選びます。XIAOとWindows受信の日本語は従来どおりMicrosoft IMEを使います。

Mac向けの速度は「標準」615 ms、「速い」420 ms、「さらに速い」280 ms、「最速」140 ms、「高速試験」70 msから選べます。数値は1文字あたりのキー間隔の設定値の合計です。実際の送信時間はWindowsの待機精度、Bluetoothの通信時間、送信開始時の入力ソース切り替えにも左右されます。

Mac側でKarabiner-Elementsなどを使ってF18を「Unicode 16進数入力」に割り当てておくと、主画面の「送信」を押すたびにF18を送り、解放から0.5秒待って本文を送ります。割り当てをしていない場合は、Macの入力メニューで「Unicode 16進数入力」を選んでください。

## インストール

ZIPの配布元とSHA-256を確認し、全体を新しいフォルダーへ展開してください。インストーラーやWindows起動時の常駐登録はありません。旧版をトレイから終了してから、新しいフォルダーの`JisBleInput.exe`を起動します。未署名の注意と初回操作は「起動と入力」、XIAOへの初回FW書込はARM64 ZIPの `firmware/README.md`を参照してください。

## 使用方法

受信PCを登録して送信先を選び、受信側の空のメモ帳へフォーカスを合わせ、まず短文で試してください。本文を確定して「送信」を押し、異常時は「停止」を使います。「クリア」は送信側の本文欄を空にし、「ペースト」はカーソル位置または選択範囲にクリップボードの文字を貼り付けます。具体的なIME・配列・改行の条件と手順は「起動と入力」に記載しています。

## 設定

右上の「設定」から送信方式、接続先、受信側のJIS/US配列、IME準備、自動接続待ちを選びます。本文・入力方法・速度は主画面で操作します。設定の保存範囲は「一時停止・続行・保存」を参照してください。XIAO方式を選ぶときは、設定画面の基板と送信先を確認してください。

## アンインストール

送信を停止し、トレイの「終了」で完全に終了してから、展開したアプリのフォルダーを削除します。アプリの削除だけではWindowsのBluetoothペアリングや基板の登録を解除しません。不要になった接続先はWindowsの標準Bluetooth設定で対象を確認して解除してください。XIAOの登録解除・FWの復旧はARM64 ZIPの `firmware/README.md`に従い、bootloaderやSoftDeviceをまとめて上書きしないでください。

利用者別の設定と登録済みの定型文も消す場合は、アプリを終了した状態で`%LOCALAPPDATA%\JisBleInput`を削除します。このフォルダーには送信方式・接続先の識別用ハッシュ・入力方法・明示的に登録した定型文を保存します。

## 既知の制限

入力先のフォーカスとIME状態は受信側で確認する必要があります。Enterで投稿・検索・保存が実行される欄へ改行を含む本文を送らないでください。停止や切断で、すでに入力した文字を自動削除しません。全PC・IMEでの動作保証はなく、内蔵Bluetooth方式とXIAO方式で制約が異なります。対応範囲と未確認項目は「対応範囲と確認状況」と[VALIDATION.md](VALIDATION.md)を参照してください。

## 定型文

1. 「設定」→「定型文を管理…」を開き、「現在の本文から新規」を選びます。表示名、本文、入力方法を入力し、必要に応じて内蔵Bluetoothの速度と、ARM64ではXIAOの速度を指定して保存します。速度を「現在の設定を使う」にすると、その方式・入力方法で前回選んだ速度を使います。
2. 主画面の「定型文の選択…」から表示名を選び、「読み込む」を押します。別の本文が入っている場合は置き換えを確認します。本文欄・入力方法・使用中の方式の速度が切り替わります。
3. 受信側の入力欄を確認して「送信」を押します。登録済みの定型文は管理画面から編集・削除できます。

定型文は最大100件です。文字と改行は通常の送信と同じ条件で検査します。登録した本文は `%LOCALAPPDATA%\JisBleInput\text-presets.json` に暗号化せず保存するため、認証情報などの機密情報は登録しないでください。

形式の異常がある場合は、読み込めた定型文を使用できます。管理画面で保存するときに確認し、元のファイルを同じフォルダーに `text-presets-recovery-*.json` として退避します。旧版の速度番号は読み込み時に対応する速度へ引き継ぎます。保存ファイル内の `slow`、`standard`、`fastest` は方式・入力方法ごとの遅い順の3枠を指します。たとえば内蔵Bluetoothの日本語「標準」は最初の枠なので `slow` です。

## XIAOの速度設定（ARM64のみ）

| 入力方法       | 遅め     | 標準     | 最速     |
| -------------- | -------- | -------- | -------- |
| 日本語・混在文 | 約240 ms | 約205 ms | 約175 ms |
| 英数字・URL    | 約65 ms  | 約30 ms  | 約25 ms  |

数値はWindows ARM64で通信を模擬して計測した1文字あたりの処理時間を5 ms刻みに丸めた目安です。実際のUSB・BLE待ちや受信側IMEの処理は含みません。初回は両モードとも標準で、以後はモードごとの選択を保存します。実機確認の手順はARM64 ZIPの `docs/XIAO-SPEED-TRIAL.md` にあります。

## XIAO（USB接続）方式（ARM64のみ）

「設定」→「送信方式」で「XIAO（USB接続）」を選び、基板を選択して「PCを追加」を押します。受信Windowsで60秒以内に`JIS BLE Keyboard XXXX`を追加し、アプリの送信先を選びます。以後は「再接続」で登録済みPCを待ちます。「起動時に接続待ちを開始する」がオンの場合、USB通信が切れても同じ基板へ数秒おきに自動で再接続します。切断後に本文を自動再送しません。

必要なのはXIAO nRF52840 Senseとデータ通信対応USB-Cケーブルです。アプリはFW 0.1.0・0.1.1・0.1.2に対応します。登録解除の保存エラーを通知するにはFW 0.1.1が必要です。ARM64 ZIPの `firmware/README.md`を参照してください。通常版・Plus・書込モード（PID 0045）は「接続不可・対象外」と表示します。基板ID・FW版・キー定義が対応する場合だけ送信できます。登録先変更は「基板の登録を解除」で1件を解除します。

XIAO方式ではトレイ格納中もUSBとPINGを維持し、終了時は基板の停止・解放とUSB切断を行います。「停止」を押すと処理中の要求の応答を待ち、追加のキー送信を止めて解放を試みます。応答が途絶えた場合はUSB接続を閉じます。本文・速度・配列は送信準備より前に固定されます。初期設定の送信方式は内蔵Bluetoothです。

利用者の選択によりHMACは使わず、送信PC上でUSBを操作できるプロセスを信頼します。アプリが開いていない間は別プロセスも基板を操作できます。BluetoothのJust Worksによる暗号化と、相手のMITM認証は別です。明示的に登録した定型文は送信PCに保存します。本文・キー列・bond鍵はログへ保存しません。

2026-11-05〜06にXIAO nRF52840 Senseへの実書込、受信Windowsでのキーボード分類（再起動後を含む）、日本語の全文一致、停止・障害時のキー解放と手動再送を利用者が確認しました。実機必須受入4件はPASSです。これはすべてのPC・IME条件での動作保証ではありません。確認状況は[VALIDATION](VALIDATION.md)、通信契約はARM64 ZIPの `docs/XIAO-PROTOCOL.md`を参照してください。

## 起動と入力

**Windowsアプリは未署名です。** 配布元とZIPのSHA-256を確認してください。Windowsが起動を警告する場合は、ファイルの出所を確認してから実行を判断してください。SmartScreenやウイルス対策を全体的に無効化する必要はありません。

1. 送信するWindowsのCPUに合うZIPを、新しいフォルダーへ全体展開し、`JisBleInput.exe`を開きます。ZIPのファイル名は候補ごとに異なります。受信PCには配置しません。旧版はトレイの「終了」から先に終了してください。
2. 登録済みPCからの接続待ちを自動開始します。初回・追加の登録は「設定」→「PCを追加」を押し、受信PCの標準Bluetooth設定で追加します。接続先を選びます。次回は、前回選んだPCの接続・HID購読が戻ると送信先を復帰します。
3. 「入力方法」「入力速度」を選びます。英数字モードでは「設定」で受信Windowsが適用する配列（JIS/US）も合わせます。受信側の入力先をクリックし、日本語ならMicrosoft IMEのローマ字入力、英数字ならIMEオフ（A）を確認します。CapsLockはオフ、未確定文字なしにします。設定画面の「受信側のIMEを準備」はモード指定だけで、本文・F5・Enterは送りません。
4. 送信欄に文章全体を確定入力します。確認チェックボックスはありません。「改行」の回数と、文字数欄のツールチップにある先頭・末尾改行の情報を確認してください。改行は通常Enterとして送るため、改行を含む文章はメモ帳などEnterで改行する複数行欄だけに送ります。検索・投稿・保存を実行する欄には送らないでください。
5. 「送信」を1回押すか、Ctrl+Enterを押します。受信側で別の操作やフォーカス変更をせず、完了後に全文を確認します。コード・日時・再読み込み等の異常があれば「停止」を押します。入力済み内容や未確定コードの自動削除・自動再送はしません。

まずメモ帳の空の文書に初期値`日本語ABC橋。`をまとめて送り、コード・日時・余分な改行がないことを確認します。1文字ずつ人が確定する操作はありません。

## 主画面の操作

- 通常の画面は440×480、最小サイズは360×380です。Enterで改行、Ctrl+Enterで送信・続行、Tabで次の項目へ移動します。
- 文字数・改行数・接続状態を常時表示します。接続診断は接続状態のツールチップまたは設定画面で確認できます。同名PCは機器ID由来の8桁の識別表示で区別できます。
- 送信前の失敗は主画面に理由を表示します。XIAOの応答が途絶えた場合は、送信準備中・送信中のいずれでもその理由を表示します。途中停止時はキー解放操作の結果と受信側の確認が必要な場合を表示します。

## タスクトレイへの格納と終了

- 右上の×を押すと画面を隠し、時計の近くのタスクトレイへ格納します。アイコンが見えない場合は、通知領域の隠れているアイコン一覧（通常は「^」）を開いてください。
- 格納中もBluetoothの接続待ち・接続状態・送信処理を維持します。×ではBluetoothをオフにせず、送信の取消や本文の消去もしません。送信を止める場合は画面を開いて「停止」を押します。
- トレイアイコンのダブルクリック、または右クリック →「画面を開く」で元の画面に戻ります。EXEをもう一度起動したときも既存画面へ表示要求を送ります。
- 完全に終了するときは、トレイアイコンを右クリック →「終了」を選びます。この場合だけ従来の接続終了・Bluetooth復帰確認を行います。終了後はトレイアイコンを取り除きます。
- Windowsのサインアウト・シャットダウンでは、トレイへ格納する処理で終了を妨げません。Windows起動時の自動常駐登録は追加していません。
- 日本語の速度・キー列、保存する設定、.NET 10.0.14は1.6.2のままです。

## 入力方法と速度

PC内蔵Bluetooth方式の固定待機時間です。XIAOの速度は「XIAOの速度設定」を参照してください。

| 入力方法       | 入力速度 | 1文字の固定待機 |
| -------------- | -------- | --------------- |
| 日本語・混在文 | 標準     | 1.10秒          |
| 日本語・混在文 | 少し速め | 0.90秒          |
| 日本語・混在文 | 速い     | 0.70秒          |
| 日本語・混在文 | 試験：XIAO最速と同じ待ち | 0.025秒 |
| 英数字・URL    | ゆっくり | 0.12秒          |
| 英数字・URL    | 高速     | 0.08秒          |
| 英数字・URL    | 最速     | 0.05秒          |
| 英数字・URL    | 試験：XIAO最速と同じ待ち | 0.005秒（押下後3 ms、解放後2 ms） |

この表はWindows受信向けです。試験速度は内蔵Bluetoothの日本語・混在文と英数字・URLでそれぞれ選べます。速度選択は今回の起動中だけ有効で、定型文と入力設定には通常の速度を保持します。受信側の空の文書で全文を比較してください。

Bluetooth/APIの待ちは別途加わります。英数字の開始・続行時の準備に0.30秒、改行に0.35秒の固定待機があります。実機の文字/秒を示す値ではありません。乱れる場合は選べる範囲で遅い速度へ変更します。

日本語は各文字の前にLANG1（ImeOn）とCtrl+CapsLockでひらがなモードを指定し、4桁16進コード→F5→Enterで変換・確定します。英数字はLANG2（ImeOff）で準備し、受信配列に対応した直接キー入力を使います。英数字モードの非ASCIIは全文を送信前に拒否します。

## 一時停止・続行・保存

- 「一時停止」は現在の1文字の変換・確定とキー解放後に止まります。すぐ中断する場合は「停止」を使います。一時停止後は受信側の入力欄とIMEを確認して「続きから送信」を押します。
- 送信中は送信側PCの自動スリープと画面の消灯を抑止し、完了・停止・一時停止で解除します。カバーを閉じる操作や電源ボタンによるスリープは防げないため、長文の送信中は送信側を開いたままにしてください。
- 送信中・一時停止中は開始時の文章、方法、速度、配列、宛先・接続世代を固定します。編集欄の変更は次回分です。再接続先へ残りを自動送信しません。
- 送信した本文や編集中の本文は自動保存しません。方法・速度・配列を`%LOCALAPPDATA%\JisBleInput\input-preferences.json`へ、自動接続待ち設定と前回PCのIDのSHA-256を`connection-preferences.json`へ保存します。速度は送信方式と入力方法ごとに保存します。機器の名前・生の機器ID・ペアリング認証情報は保存しません。

## 対応範囲と確認状況

2026-11-06までに、XIAO nRF52840 Sense（FW 0.1.0）で実機受入の必須4項目を確認しました。受信Windowsでのキーボード分類と再起動後の維持、日本語・混在文の入力、停止・キー解放、USB抜去・アプリ強制終了・BLE切断からの復帰が対象です。検証に使用したZIPは `jis-ble-input-1.7.0-xiao-preview.7-win-arm64.zip` です。試験範囲は[VALIDATION](VALIDATION.md)、通信契約はARM64 ZIPの `docs/XIAO-PROTOCOL.md`を参照してください。

最大5,000送信単位です。CRLFは1単位、LF/CRも改行として扱い、先頭・末尾・連続改行を削除しません。日本語は対応するBMP文字、英数字は印字可能ASCII U+0020〜U+007Eを対象とし、Tab・補助平面文字・結合文字・書式制御文字等は全文を送信前に拒否します。欄によって内部改行コードが異なるため、LF/CRLFの厳密な保持は保証しません。

前版の基本一括入力にはメモ帳・Google側での正常動作の利用者報告がありますが、具体的な試験本文、OS/IME版、IME互換設定は未取得です。トレイ格納・復帰・明示終了はBluetoothを起動しないWindows UIテストで確認しています。接続相手を伴うトレイ滞在・再接続・終了時のラジオ復旧・長文・全記号の実機受入は未実施です。キー列の送信成功は、受信側のIME変換成功や本文完全一致の自動確認を意味しません。

同梱`samples/`にASCII URL、印字可能ASCII95文字、CRLF/LFの4行文、100文字/1,000文字の試験文があります。検索・投稿を実行しない空の文書で使い、受信結果を全文比較してください。

1.16.0の正式提供は、x64の内蔵Bluetooth受入とARM64実機回帰の完了後に判定します。新規発見可能な広告を継続する制約も残り、アプリ単独で新規ペアリング受付を操作時だけに限定したとは扱いません。OSの承認と、アプリの選択PCへの送信先固定を維持します。

## ビルドと梱包

リポジトリのルートでPowerShell 7.4以上を使用します。.NET SDK 10.0.204は`global.json`で固定しています。Windows x64またはARM64でビルドでき、Windows ARM64・x64の自己完結型ZIPを発行できます。`NuGet.Config`はオフライン設定です。初回だけ`Restore.ps1 -Online`で公式NuGetからlockfileに一致する依存を取得します。

配布前に公式NuGetへ接続できる環境で、`dotnet list src/JisBleInput.App/JisBleInput.App.csproj package --vulnerable --include-transitive --source https://api.nuget.org/v3/index.json` を実行し、依存の脆弱性情報を確認します。同梱 .NET については Microsoft のサポート情報を確認します。

```powershell
pwsh -NoProfile -File scripts/Restore.ps1 -Online
pwsh -NoProfile -File scripts/Test.ps1 -NoRestore
pwsh -NoProfile -File scripts/Prepare-FirmwareTools.ps1
pwsh -NoProfile -File scripts/Test-XiaoContract.ps1
pwsh -NoProfile -File scripts/Test-Firmware.ps1
pwsh -NoProfile -File scripts/Build-Firmware.ps1
pwsh -NoProfile -File scripts/Publish.ps1 -RestoreSource https://api.nuget.org/v3/index.json
pwsh -NoProfile -File scripts/Publish.ps1 -RuntimeIdentifier win-x64 -RestoreSource https://api.nuget.org/v3/index.json
pwsh -NoProfile -File scripts/Test.ps1 -Package
```

オフラインの梱包用復元には、固定した `win-arm64`・`win-x64` の両方の依存とランタイムパックが必要です。Core単体のテストにはRID別のランタイムパックは不要です。WPFテストと梱包には、それぞれAppが要求する依存とランタイムパックを用意します。`-PackagesPath` でプロジェクト内の保存先、`-FallbackPackagesPath` で参照するキャッシュを複数指定できます。梱包用の復元まで済んだ場合は `-NoRestore` を使えます。

ARM64の梱包には検証済みのFWビルド一式が必要です。x64はFWとXIAO専用資料を含みません。既定の `_internal/build-cache/firmware` にない場合は、上の手順でビルドするか、同じFWソースから作った信頼できるローカルキャッシュを `-FirmwareCachePath` で指定します。梱包時にFWソースとイメージのハッシュ、アプリEXEの版を照合します。

```powershell
& ./scripts/Publish.ps1 -FirmwareCachePath $verifiedFirmwareCache -FallbackPackagesPath @($arm64Cache, $x64Cache)
```

スクリプトは発行ごとに新しいステージングフォルダーを使います。検査済みZIPは`_internal/dist/`へ生成し、同名ZIPは上書きしません。`manifest.json`に自身を除いた全同梱ファイルのSHA-256を記録します。単独の`dotnet publish`ではruntime noticesの収集・配布検査を完了しません。

C# Coreの行coverageは`dotnet-coverage` 17.14.1を`_internal/tools/coverage`へローカル導入し、`scripts/Test-CoreCoverage.ps1`で計測します（90%以上、計測用構成は配布用と別）。導入例: `dotnet tool install dotnet-coverage --version 17.14.1 --tool-path _internal/tools/coverage --configfile NuGet.Config --add-source https://api.nuget.org/v3/index.json`。

`Test.ps1`はWindowsでCore・能力診断・UIの各チェックをビルドして実行します。復元済みなら`-NoRestore`を付けられます。UI試験はBluetoothを開始せず、本文を送信せず、専用の一時設定を使います。`-Package`は現在の版の両ZIPを作成した後に指定します。候補ZIPを検査する場合は `-Arm64Package` と `-X64Package` で絶対パスを渡し、配布検査にPester 5.7.1以上を使います。ログと結果は`_internal/build-cache/validation/`へ保存します。独自のチェックプログラムのため、`dotnet test`の終了コード0だけで成功としません。C#静的解析には`dotnet format analyzers src/JisBleInput.App/JisBleInput.App.csproj --verify-no-changes --no-restore`を使います。単なる書式検査を意味解析やカバレッジ測定の代わりにしません。

## 来歴・資料

HID descriptorのキーボード部分は[BleHid v0.6.0](https://github.com/abhishek-raj/windows-ble-hid/blob/v0.6.0/src/BleHid.Core/HidDescriptors.cs)由来です。マウス部分を除外しJIS/IME usageを拡張しています。原著作権・MIT許諾文を`licenses/windows-ble-hid-LICENSE.txt`に保持します。上流プログラムは同梱・実行していません。

APIの根拠は[Microsoft GATT Server](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-server)、[対象宛先への通知](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattlocalcharacteristic.notifyvalueasync)、[日本語IMEキー定義](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/keyboard-japan-ime)、[Radio.SetStateAsync](https://learn.microsoft.com/en-us/uwp/api/windows.devices.radios.radio.setstateasync)です。

開発・受入の記録は[VALIDATION.md](VALIDATION.md)、旧版の説明と経緯は開発用チェックアウトの`docs/HISTORY.md`を参照してください。配布ZIPには開発履歴を含めません。

## ライセンス

独自コード・素材は[MIT](LICENSE)、第三者の条件は[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)に従います。ソースと配布物は[GitHub](https://github.com/Ryoichi-R/jis-ble-input)、通常の不具合・要望は[Issues](https://github.com/Ryoichi-R/jis-ble-input/issues)を参照してください。脆弱性の非公開報告は[SECURITY.md](SECURITY.md)の手順に従ってください。
