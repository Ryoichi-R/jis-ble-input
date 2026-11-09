# XIAO nRF52840 Sense / JIS BLE Keyboard 0.1.0

アプリ1.7.1・1.8.0・1.9.0・1.10.0・1.11.0・1.12.0に対応するXIAO nRF52840 Sense（通常時PID 8045）用FW 0.1.0です。USB protocol 1を使用します。実機受入に使用した配布ZIPは `jis-ble-input-1.7.0-xiao-preview.7-win-arm64.zip` です。2026-11-05〜06にWindows ARM64標準ドライバー（usbser）での接続、DFUによる書込、BLE接続、受信Windowsでのキーボード分類と再起動後の維持、日本語・混在文の入力、停止・接続障害からの復帰を利用者報告で確認しました。試験範囲はアプリ同梱の `VALIDATION.md` を参照してください。

## 使い始めるまで

必要機材はXIAO nRF52840 Sense 1台、データ通信対応USB-Cケーブル、送信Surface、Bluetooth LE対応の受信Windowsとその操作用キーボード。Senseのマイク・IMUはFWで使わない。通常版/Plus、はんだ付け、電池、追加無線アダプターはこの構成に含めない。

1. アプリと旧版をトレイから終了する。基板の型番を現物で確認する。通常時USBはVID2886/PID8045、bootloaderは0045（通常版8044/0044を使わない）。初回は基板1台だけを接続する。
2. 既存アプリが入っている場合は、元の書込用ファイル・版・ハッシュを別に保持する。この手順は現物から既存フラッシュやbond/keyを吸い出さない。初期状態へ完全に戻すバックアップを取得したとは扱わない。
3. 基板のRESETを素早く2回押し、bootloaderのCOM番号とUSBシリアル番号を確認する。`INFO_UF2.TXT`が見える場合はBoard-IDとSoftDevice情報も確認し、Sense版・S140 7.3.0と一致しなければ止める。基板の旧bootloaderを推測で更新しない。[Seeed手順](https://wiki.seeedstudio.com/XIAO_BLE/)
4. 開発workspaceでは次のdry-runを使う。COM番号・16桁serialは現物の値へ置き換える。dry-runでも対象を検査するが、COMを開かない。`-Apply`を付けた実行だけがアプリ領域を書き換える。

```powershell
pwsh -NoProfile -File scripts/Flash-Firmware.ps1 -Port COM7 -BoardSerial 0011223344556677
# 対象・復旧用ファイルを確認してから、同じ引数に -Apply を追加する。
```

5. UF2ドライブが正しいSense版基板と確認できた場合は、配布候補の`xiao-nrf52840.ino.uf2`を手動コピーする方法もある。コピー先を自動推測しない。UF2はnRF52840 family ADA52840、開始27000のアプリ領域だけ。bootloader/SoftDevice/UICRを含まない。[Adafruit UF2仕様](https://github.com/adafruit/Adafruit_nRF52_Bootloader#making-your-own-uf2)
6. 通常起動に戻ったらJIS BLE Inputの「設定」→「送信方式」→「XIAO（USB接続）」を選び、USB基板を選択して「PCを追加」を押す。受信Windowsで60秒以内に`JIS BLE Keyboard XXXX`を追加する。初版の登録は1台。接続後、送信先を選び、まずメモ帳に短文を送る。
7. 受信WindowsのBluetooth設定でキーボードとして分類され、デバイスマネージャーにHIDキーボードが現れることを確認する。名前にKeyboardと表示されるだけでは合格にしない。必要に応じGAP Appearance2A01=03C1も確認する。

HMACは使わず、送信PC上でUSBを操作できるプロセスを信頼する。アプリ非起動時に別プロセスが基板を操作できる。アプリは使用中のCOMを排他的に開く。BLEのJust Worksは暗号化を行うが、MITM認証を保証しない。

## 停止と復旧

「停止」は未処理の本文を破棄し、DISARMとゼロ解放を試みる。結果未確認の場合は受信側の既存キーボードでキー・IME・入力欄を確認する。基板のUSBを外す場合も自動再送しない。復帰後は新規送信として開始する。

書込失敗・アプリが応答しない場合は、RESETを2回押してbootloaderへ戻し、保持した既知の正常アプリimageを再書込する。アプリ領域だけを扱い、フラッシュ全消去、bootloader更新、SoftDevice更新は行わない。bootloaderにも入れない場合はこの手順の対象外で、SWD復旧を自動実行しない。基板の登録変更はアプリの「基板の登録を解除」で1件を明示確認して行う。PC全体のBluetooth登録は消さない。

## 開発と再現

リポジトリのルートでPowerShell7.4以上、Python3.11以上、Windows x64またはARM64を使用する。Arduino CLIとZigはx64版（ARM64では互換実行）、coverage用LLVMはホストに対応する版を使用する。MSVCのC++ヘッダーとWindows SDKが必要。MCU用ツールの固定版は`toolchain.json`、ホスト用LLVMの取得元とSHA-256は`scripts/Get-FirmwareHostTools.ps1`に記録する。グローバルのArduino設定・SDK・ドライバーを変更しない。

```powershell
pwsh -NoProfile -File scripts/Prepare-FirmwareTools.ps1
pwsh -NoProfile -File scripts/Test-XiaoContract.ps1
pwsh -NoProfile -File scripts/Test-Firmware.ps1
pwsh -NoProfile -File scripts/Build-Firmware.ps1
```

初回prepareは公式サイトからダウンロードする。以後build/testはキャッシュだけを使う。ビルド中に基板は開かない。旧GCCのMAX_PATH制約のため同じ固定コンパイラーをキャッシュ直下`gcc9`へコピーする。出力は`_internal/build-cache/firmware/build/`、.hex/.zip/.uf2とreceiptにハッシュを保存する。`Test-FirmwareImage.py`でIntel HEX checksum・範囲・UF2 family/範囲/HEX一致を検査する。失敗後は古いreceiptのハッシュと一致しないため書込・梱包を拒否する。

SDK 1.1.13のBluefruit0.21.0/TinyUSB1.7.0と実ヘッダーを照合して実target compile済み。上流masterのAPIを無条件には使用しない。BLECharacteristicの待機型notifyを避け、単一loopがSoftDeviceの非ブロッキング通知を呼ぶ。SEC_PARAMS_REQUESTの窓外拒否だけSDKへ追加し、変更はworkspace内に限定する。patch前後のハッシュはビルドで検査する。

自動検証と実機検証は別。初回にUSB serial/HELLO一致、書込・復旧、広告/GATT/分類、JIS/US ASCII95文字と日本語、100/1000/2000単位、CapsLock・Boot/Report・suspend、USB/BLE抜去・再接続・停止、長時間トレイ滞在を現物で確認する。C++の`-Wall -Wextra -Werror`はホストテスト（Zig、C++11）で適用する。2026-11-06から`Test-Firmware.ps1`がLLVM 23.1.2（`Prepare-FirmwareTools.ps1`でホスト対応版を取得。対応するMSVC C++開発ツールが前提。ARM64版の展開にはzstd CLIも必要）で`src/*.h`の行coverageを計測し、90%未満で失敗する（初回99.43%。`.ino`のSDK境界は対象外）。C++書式は`.clang-format`と`scripts/Test-FirmwareFormat.ps1`で検査する。現ソースは未整形でreceiptには`NOT_CONFORMING`と記録され、次のFW変更時に`-Fix`で整形してから`-Enforce`へ切り替える。workspace共通formatterにはC++用adapterがない。`toolchain.json`の`lineCoverage`/`cppFormatting`はsource hashの対象のためbuild時点の値のまま残し、現在値はhost-testsのreceiptを正とする。

## ライセンスと再ビルド資料

descriptorは既存MIT原文を保持する。SDKはArduino LGPL2.1以降、Bluefruit/TinyUSB/FreeRTOS等のMIT、Nordic独自条件、その他同梱条件が混在する。すべてMITとは扱わない。候補には採用SDK原本アーカイブ、license/notice、アプリのFW sourceとスクリプト、ELF/objectを含める。SDKはNordic IC用。配布候補はローカル検証用で、公開の監査・許可を意味しない。
