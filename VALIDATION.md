# 1.8.0 XIAO速度検証版

2026-11-09 JST。日本語の文字コード送信に、XIAO専用の固定待ち600・500・400・350msを追加した。速度以外のキー列と改行処理は従来のまま。実機で全文一致する最短設定は未確認。[手順と記録表](docs/XIAO-SPEED-TRIAL.md)を使って、受信PCの入力結果を計測する。

- Windows x64の自動検証はCore 985、診断17、WPF UI 77項目が成功。受信側の模擬モデルで同音異字・全角記号・空行・末尾改行の一致と、各速度の待機時間を確認した。
- 全文比較ツールではUTF-8 BOMとCRLF/LFの違いを吸収し、同音異字への置き換わり・末尾改行の欠落・別のUnicode行区切りを差分として検出した。

# 1.7.1 公開候補

2026-11-07 JST。停止時のキー解放結果を保持する修正と、XIAO成功応答の状態情報を検証する修正を含むPATCH版。アプリの表示・配布版を1.7.1へ統一した。FWは0.1.0、USB protocolは1。

- Windows x64でCore 933、診断17、WPF UI 74項目を実行した。WPFの停止ボタン回帰にはキー解放失敗時と解放操作送信時の両方を含む。
- FWホスト検証339項目とイメージ安全性10項目を実行し、純粋ロジックの行coverageは174/175 = 99.43%。Coreは841/915 = 91.91%。
- app互換メタデータを更新してFWを再ビルドし、HEXとUF2は既存候補とバイト一致した。DFU ZIPは生成時刻等でバイト一致しない。基板の書き換えは、この互換メタデータ更新には不要。
- C++整形6件は、書込済みFWとの対応を保つ既存判断に従って次のFW実装変更まで保留している。対応言語のformatterと静的解析、C++書式の保留は別々に記録する。
- 2026-11-07に、利用者が1.7.1のZIPを新しいフォルダへ展開し、ARM64での起動・設定表示・トレイからの終了、XIAOの短文送信と途中停止の成功を報告した。確認したEXEとUF2のSHA-256を公開時の検証記録に結合した。
- 受信PC再起動後の分類と接続障害の反復試験は、下記の2026-11-05〜06の受入、今回の同一UF2、変更経路の回帰テストを合わせて確認する。今回の利用者報告で反復実施したとは記録しない。

# 2026-11-07 JST 公開準備のローカル検証

独立リポジトリとしてSDK 10.0.204を固定し、出力・依存キャッシュ・テスト用設定をプロジェクト内へ収めた。Windows x64での検証を追加し、配布対象はWindows ARM64を維持する。lockfileの既存パッケージの版・content hashは変更していない。独自コードへのMIT、未署名の説明、サポートと非公開報告の方針、SBOMの生成を追加した。GitHub窓口は公開時に設定する予定で、現在は開設済みとは扱わない。

- C# Core 928、診断17、WPF UI70、C++339、FWイメージ安全性10、配布Pester9の合計1,373チェックが成功した（coverage用の同一チェック再実行を二重計上しない）。
- Coreの行coverageは840/915=91.80%。dotnet-coverage 17.14.1、独立した計測構成で測定した。WPF・診断・SDK境界はこのCore計測に含めない。
- FWのホスト検証可能な`src/*.h`の行coverageは174/175=99.43%。Zig 0.14.1のC++11検証とLLVM 23.1.2のC++14計測を分離した。`.ino`のSDK境界は含めない。
- ARM64アプリの自己完結ビルド、埋め込み.NET 10.0.14とSystem.IO.Portsの一致、Sense向けFWのコンパイル、HEX/UF2/DFUの整合性と保護領域の除外を検査した。
- C#静的解析、PowerShell解析と対応言語の書式検査を実施した。既知のC++書式6件は引き続き`NOT_CONFORMING`で、合格とは扱わない。次回FWソース変更時に整形する既存判断を維持する。
- FWのビルドでは`-ffile-prefix-map`を用い、再ビルド資料に現在のプロジェクトの絶対パスが埋め込まれることを防ぐ。FW本体・プロトコル・アプリの動作コードは変更していないが、ビルド時パスの置換により以前のimageとバイト一致するとは限らない。

以下の実機必須受入4件は、2026-11-05〜06に使用したpreview.6/.7の利用者報告である。今回のx64ホスト検証を新たなARM64実機受入へ読み替えない。新しく生成した配布物のARM64上でのクリーン展開・起動と公開前の正式監査は、別途確認する。

# 1.7.0 XIAO Sense USB接続候補

## 2026-11-07 JST アプリの保守修正

- 停止ボタンでは送信を取り消し、送信処理がキー解放結果を表示する。内蔵Bluetooth方式で停止ボタンを押し、解放失敗時の警告と解放操作送信時の表示を確認するWPF回帰テストを追加した。
- XIAOの成功応答は57バイトの状態情報を含む場合に限って処理する。状態情報の欠落・長さ超過・無効なモードを含む5ケースで、要求を成功扱いせずUSBを閉じ、自動再送しないことを確認した。アプリと基板のREADMEに実機受入で使ったZIP名と確認範囲を反映した。
- 移管元のmacOS検証ではHID定義照合・Core 933項目・診断17項目が成功した。標準手順のWPF検証はWindows ARM64用lockfileとmacOSのRIDの不一致、およびオフラインキャッシュに `System.IO.Ports 10.0.14` がないため復元で止まった。一時コピーでキャッシュ済みの `System.IO.Ports 10.0.3` と.NET 10.0.8の参照を使ったクロスビルドでは、アプリとWPFテストの警告・エラーは0件だった。ファームウェアのソースハッシュは実機受入時と一致した。
- 独立リポジトリへの移管後、Windowsで `pwsh -NoProfile -File scripts/Test.ps1 -Package` を実行した。HID定義照合、Core 933項目、診断17項目、WPF UI 74項目、配布レイアウト9項目が成功した。

## 2026-11-06 JST 実機受入完了（利用者報告）

`xiao-preview.7` と書込済みのXIAO nRF52840 Sense FW 0.1.0で確認した。**実機必須受入は4件すべてPASS（XIAO-02・03・06・07）**。

| XIAO-07の障害操作         | キー保持 | 残り本文の自動再送 | 復帰後の手動送信 | 判定 |
| ------------------------- | -------- | ------------------ | ---------------- | ---- |
| 送信中のUSB抜去           | なし     | なし               | 成功             | PASS |
| 送信中のアプリ強制終了    | なし     | なし               | 成功             | PASS |
| 送信中の受信PCでのBLE切断 | なし     | なし               | 成功             | PASS |

- **XIAO-02再起動後の分類はPASS**。受信PCの再起動後も、Bluetooth設定のキーボードのアイコンと入力機器の区分、およびデバイスマネージャーの「HID キーボード デバイス」の列挙を確認した。初回分と合わせてXIAO-02はPASS。
- **送信先未選択の案内は未確認**。記憶済みの送信先が自動復帰したため、未選択時の表示条件は成立しなかった。
- XIAO-06のARM待ち中の本文編集の次回適用、停止までの時間と残留入力数は未計測。受信OS build、GATT Appearance、Surface方式との比較、任意試験の追加報告はない。

## 2026-11-06 JST ソフトウェア側の残課題対応

- **C++行coverage**: 利用者の承認を受け、LLVM公式Release 23.1.2のWindows ARM64版（`clang+llvm-23.1.2-aarch64-pc-windows-msvc.tar.zst`、2026-09-22公開、SHA-256 `5A1B0140…B76C`はGitHub APIのdigestと一致、release manager鍵`FFB33689…CBA5`のGPG署名Good）から、clang・lld-link・llvm-cov・llvm-profdata・clang-formatとprofile runtimeだけを`_internal/build-cache/firmware/tools/llvm-23.1.2-aarch64-windows/`へ展開した（194MB、ネイティブARM64、配布物には含めない）。Zig 0.14.1は計測用runtimeがなくlinkに失敗した。`Test-Firmware.ps1`はZigのC++11 `-Werror`試験に加え、MSVC Build Tools 14.44 ARM64のSTLとclangでC++14の計測buildを作り、同じ339チェックを実行する。行coverageは`src/*.h`が174/175=**99.43%**（Controller 101/102、Pairing 12/12、Protocol 61/61）で、90%未満なら失敗する。`.ino`のSDK境界はホストでcompileできないため対象外で、実機項目に残す。`Prepare-FirmwareTools.ps1`へLLVMの取得・hash照合・展開を追加した（zstd CLIが必要。Windows標準tarはこのアーカイブのzstd長距離窓を復号できない）。
- **C++書式**: `firmware/xiao-nrf52840/.clang-format`と`scripts/Test-FirmwareFormat.ps1`を追加した。既存の書式に最も近い設定でも6ファイルすべてが不適合（1行に複数文を書く書式のため）。利用者の判断で、FW source hashと書込済みpreview.6/.7の対応を保つため、ソースの整形は次のFW変更時に`-Fix`で行い、それまでは`Test-Firmware.ps1`のreceiptへ`CppFormat: NOT_CONFORMING（Enforced=false）`として記録する（Passedとは扱わない）。一時コピーで`-Fix`後に`-Enforce`がPASSし、整形後のソースもC++11 `-Werror`で339チェックが成功することを確認した。`toolchain.json`の`lineCoverage`/`cppFormatting`欄はsource hashの計算対象のため、FW0.1.0のbuild時点の値のまま残す（現在値はhost-testsのreceiptを正とする）。
- **送信先未選択の案内**: XIAO方式で登録済みPCが接続済みかつ送信先が未選択のとき、主画面の状態を「送信先PCを選択してください」、送信先欄を「ここで送信先PCを選択」と表示する。自動選択はしない（記憶済みPCだけを自動選択する仕様は維持）。内蔵Bluetooth方式の表示は変えない。UIチェックは65→70。
- **配布候補**: `_internal/dist/jis-ble-input-1.7.0-xiao-preview.7-win-arm64.zip`（ZIPのSHA-256 `D8249213…FE18`、同梱`JisBleInput.exe` `996E69C0…F52D`）。FWはpreview.6と同一（source `49778D1A…0A0C`、hex/uf2/zipの各ハッシュも同一）なので再書込は不要。再ビルド資料に`Test-FirmwareFormat.ps1`と`.clang-format`を加えた。ZIPの`testzip`は正常。
- 検証: C++339（Zig）＋coverage計測339、Core928、診断17、UI70、配布Pester8、変更6ファイルのformatter Check（変更0）、Project Fast lint PASS。作業途中でCドライブの空きが0になったため、自分の一時ファイル（約450MB）を削除した。Pesterの実行時に空きがなかったため、ZIPの整合性は別途`testzip`で確認した。

2026-11-05 JST追記: 届いた基板が`Seeed XIAO nRF52840 Sense`（通常時`USB\VID_2886&PID_8045`、CDCはWindows標準`usbser`でCOM3、OK）だったため、利用者の指示で対象をSense版へ変更した。PID分類（8045通常起動/0045書込モード/通常版8044・0044等は対象外）、FQBN`Seeeduino:nrf52:xiaonRF52840Sense:softdevice=s140v6,debug=l0`、書込スクリプトのbootloader照合、配布検査のFQBNを切り替えた。Seeedコア1.1.13のSense variantは通常版とピン定義が同一で、定義マクロ1件だけが異なり、SoftDevice S140 7.3.0・`sd_fwid=0x0123`も同じ。Sense FQBNで実target compile（USB_PID=0x8045、135,880バイト、image検査PASS）、Core928/診断17/UI65/C++336/配布Pester8が成功。現sourceに対応する候補は`_internal/dist/jis-ble-input-1.7.0-xiao-preview.5-win-arm64.zip`（`xiao-preview.4`は通常版FQBNの旧候補で、配布検査では不一致となる）。以下の2026-11-02記録の「通常版」「8044/0044」は変更前の履歴。実書込・BLE・分類は引き続きNOT_RUN。

2026-11-05 JST書込前確認: 利用者がRESETを2回押し、bootloaderへ移行した。列挙は`USB\VID_2886&PID_0045`（Composite/CDCはCOM4/マスストレージ`Adafruit nRF UF2`、ドライブ`XIAO-SENSE`）。`INFO_UF2.TXT`は`Board-ID: Seeed_XIAO_nRF52840_Sense`、`SoftDevice: S140 version 7.3.0`、`UF2 Bootloader 0.6.1`（2021-11-12）。Board-ID・SoftDeviceは計画・DFU manifestの`softdevice_req=291`（0x0123）と一致。bootloaderはSeeedコア1.1.13の`boards.txt`参照値0.6.2ではなく0.6.1だが、計画どおりbootloaderは更新しない（application-only DFUの互換性は実書込で確認するまで未確認）。bootloader時のUSBシリアル番号（SHA-256先頭`ED2C1F6F`）は、出荷時FWの通常起動時のシリアル番号（同`EB5C99A8`）と異なる。書込スクリプトの`-BoardSerial`にはbootloader時の値を使う。自作FW起動後のシリアル番号とHELLO基板IDの一致は書込後に確認する。build receipt・hex/uf2/zipのSHA-256一致、`Test-FirmwareImage.py` PASS（135,880バイト）、build FQBN Sense一致を読み取りだけで確認した。`Flash-Firmware.ps1`（`-Apply`なしのdry-run）はClaude Codeの自動権限判定で実行を拒否されたため未実行。dry-runの各検査項目はスクリプト外で個別に照合済み。ドライブの`CURRENT.UF2`（出荷時アプリ領域の読出し、1,908,736バイト）は利用者の指示がないためコピーしていない。実書込・BLE・分類はNOT_RUN。

2026-11-05 JST出荷時イメージ保全: 利用者の指示で`CURRENT.UF2`と`INFO_UF2.TXT`を`_internal/backups/xiao-sense-factory-20261105/`（Git ignore対象）へ読み取りコピーし、`SHA256SUMS.txt`を作成した。コピー元・コピー先のSHA-256は一致（`CURRENT.UF2`は`24A7834C…A901E`）。UF2は3,728ブロック、全ブロックのmagic・連番が正常、flag 0x2000（family ID付き）、family `0x28860045`（Seeed VID/PIDベース。自作FWのUF2の`ADA52840`とは別）、範囲`0x01000`〜`0xEA000`連続。MBRとbootloader（0xF4000〜）、Bluefruitの設定・bond領域（0xED000〜0xF4000）は含まない。**SoftDevice領域（0x01000〜0x27000）を含むため、このUF2をそのまま書き戻さない。** 出荷時アプリのデータは`0x27000`〜`0x7A600`。出荷時アプリへ戻す必要が生じた場合は、アプリ領域だけを抜き出したimageを別途作成・検査してから使う（未作成）。

2026-11-05 JST初回書込: 利用者の指示で、JisBleInput終了を確認した後に`Flash-Firmware.ps1 -Port COM4 -BoardSerial <bootloader時serial> -Apply`を実行した。image検査PASS、基板`[ED2C1F6F]`照合後、`adafruit-nrfutil dfu serial`（115200bps、single bank、touchなし）で`Device programmed.`、終了コード0。bootloader0.6.1でのapplication-only DFUは成立した。書込後は`USB\VID_2886&PID_8045`、COM5（usbser、OK）で通常起動し、USBシリアル番号は**bootloader時と同じ値**（`ED2C1F6F`）になった（出荷時FWの値`EB5C99A8`とは異なる）。アプリでのHELLO（FW0.1.0・基板ID一致）、BLE広告・ペアリング・分類・入力はNOT_RUN。

2026-11-05 JST初回接続・分類（利用者の目視報告）: アプリ1.7.0（xiao-preview.5）でXIAO（COM5）を選択し、エラー・FW不一致表示なしで接続できた。「PCを追加」の登録窓で受信Windowsに`JIS BLE Keyboard XXXX`が表示され、追加できた。受信側の「Bluetoothとデバイス」でキーボードのアイコン・入力機器として表示され、デバイスマネージャーに「HID キーボード デバイス」が列挙された。**XIAO-02は初回発見・登録後の条件をPASS**（段階0aの目的成否判定は成立）。受信側の再起動後の分類維持、GATT`0x2A01`の値読み取り、Surface方式ベースラインとの並列比較記録、受信OS buildの記録は未実施。スクリーンショットは取得していない。XIAO-03（日本語・混在文）、XIAO-06（停止・解放）、XIAO-07（障害・復帰）の必須項目と任意項目はNOT_RUN。

2026-11-05 JST登録不成立の修正（FW不具合）: 上記の登録後、アプリは「接続待ち」のまま送信先PCが空で、「受信側のIMEを準備」「送信」が無効だった。受信PCには`JIS BLE Keyboard 07C9`が「ペアリング済み」（未接続）として残り、CapsLockは未取得だった。Seeedコア1.1.13同梱Bluefruitのsourceを確認した。`BLE_GAP_EVT_AUTH_STATUS`でSDKは`bond_save_keys`をコールバックタスクへ遅延投入し、その後にユーザーのイベントコールバックを呼ぶ。そのためFWの`onPairComplete`で直後に呼んだ`loadBondKey`は、初回ペアリングではまだ保存されていない鍵を読みに行き失敗する。その結果、FWは登録を保存せず切断し、登録窓の終了後は未登録として広告を止めるため、受信PCは再接続できない（source上の原因。実機上でフラグを直接読んでの確認はしていない）。修正: `onPairComplete`は接続アドレスだけをloopへ渡し、loopが`bond_load_keys`で保存済みidentityを読めるまで登録窓内で再試行する。読めた時点で登録を保存し、窓が閉じたら中止する。この判定を純粋関数`registrationStep`として追加し、ホストテストはC++336→339。FW版は0.1.0のまま（source SHA-256 `49778D1A…0A0C`、135,992バイト、image検査PASS）。利用者が受信PCから当該基板1件を削除し、アプリを終了してRESETを2回押した後、修正版をDFU書込した（`Device programmed.`、COM5で通常起動）。配布候補`_internal/dist/jis-ble-input-1.7.0-xiao-preview.6-win-arm64.zip`（ZIPのSHA-256 `CC2FE2D1…E323`。同梱`JisBleInput.exe`のSHA-256は`A2CEF058…A8C1`。2026-11-06訂正: 当初はEXEの値をZIPの値として記載していた）を作成し、Core928/診断17/UI65/Pester8が成功。`xiao-preview.5`は修正前のFW imageを同梱する旧候補である。修正版での再登録・再接続・送信はNOT_RUN。

2026-11-05 JST修正版の実機確認（利用者報告）: 修正版FWで「PCを追加」から受信PCに再登録した。受信PCは「接続済み」、アプリのCapsLock表示は「オフ」（LED取得済み）となった。主画面は送信先未選択のため「接続待ち」と表示された。初回は送信先が自動選択されず、記憶済みPCだけを自動選択する仕様である。「送信先PC」で「登録済みPC（XIAO）」を選ぶと送信可能になった。**登録保存の修正は実機で有効**。XIAO-03: 空のメモ帳へ`日本語ABC橋。`を「日本語・混在文」「速い（0.70秒/文字）」で送信し、全文一致（未確定・余分な文字/改行なし）。続けて「標準」速度でも全文一致し、**XIAO-03（必須）はPASS**。初回に送信先を手動選択する必要がある点はUI上分かりにくい（改善候補）。

2026-11-05 JST XIAO-06（利用者報告）: 本文`ABCDEFGHIJKLMNOPQRSTUVWXYZ日本語の停止試験です。`×2（大文字でShift修飾を含む）、「標準」速度で次を確認した。(1) 送信中に一時停止すると入力が止まり、「続きから送信」で末尾まで正しく入った。(2) 英字の途中で停止すると入力が続かず、受信側の既存キーボードで`a`が小文字で入った（修飾キー保持なし）。(3) 送信準備中（ARM待ち）の停止で本文が1文字も入らなかった。**XIAO-06（必須）はPASS**。停止要求から完了までの時間と残留入力数は計測していない（目視で「すぐ止まった」）。ARM待ち中に編集した本文が次回分だけに適用される確認は実施していない。

2026-11-02 JST。利用者が実装開始、HMACなしのローカルプロセス信頼、Microsoft System.IO.Portsの本番依存追加を明示承認した。System.IO.PortsはMITの10.0.14へ固定し、lockfileを更新した。SDK/runtime 10.0.14と日本語の速度・キー列は維持する。既存1.6.5の配布物・設定と他案件の差分は保持する。

- 基板用FW 0.1.0 / protocol1をSeeed非mbedコア1.1.13、Bluefruit0.21.0、TinyUSB Arduino1.7.0、S1407.3.0の通常版FQBNで実target compileした。Windows ARM64上でCLI x64とGCC9 win32を互換実行する。ネイティブARM64ツールとは扱わない。
- CRC/COBSとsession/sequence/generation、通知の直列化、ARM/DISARM、通常key-up、watchdog、通知枠不足中のPING、CapsLock時の解放、古い送信の破棄を偽USB/BLE・仮想時間で検査した。Python標準CRCで作った共通TSVをC#とC++が読み、正常・異常を照合する。アプリ/FW/正本のdescriptor70バイトとSHA-256も一致する。
- Core 928、診断17、WPF/USB接続59、C++325チェックが成功（同日の不足修正後はUI65・C++336・Pester8）。既存baselineはCore638/診断17/UI39で、ファサード抽出直後も既存件数は成功した。追加UI試験はARM前のsnapshot、待機中の停止・終了・二重開始防止、独立したcleanup token、基板/FW/descriptor不一致、世代変更とCapsLockを検証する。USB列挙・実SerialPortのOS動作はfakeの検証に含めない。
- .hex/.uf2はapplication27000〜ED000未満だけで、checksum、UF2 family ADA52840、ブロック順とHEX全バイト一致を検査する。SDKの窓外ペアリング拒否patchは原本/変更後ハッシュを固定する。採用sourceとbuild receipt、配布imageのハッシュがずれた場合は梱包を拒否する。
- ARM64配布候補には基板image、通信仕様、書込/復旧手順、SDK原本、license/notice、再ビルドsource・linkable objectを含める。正式な公開処理や基板書込は行わない。
- 同日の計画照合で不足を修正した。FWの登録窓・既知相手判定を`src/Pairing.h`の純粋関数へ分け、60秒境界・millis wrap・未知/未bond/別address・type拒否、sequence重複/逆順、ACK待ち中のBLE切断→再接続でゼロ解放だけ・旧世代拒否をホストテストへ追加した（C++ 325→336）。アプリはUSB設定（115200bps・DTRオン・1200bps不使用）、PID分類（8044通常/0044書込モード/その他対象外）、書込モード・対象外基板を開かないこと、COM占有、USB抜去後の停止と宛先解除を偽I/Oで検証した（UI 59→65）。一覧では0044・Sense等を隠さず「接続不可・対象外」と表示する。
- 内蔵Bluetooth方式の1.6.5挙動（送信中も続く接続先の定期更新、未開始時の「接続先を更新」無効、停止時文言の置換）を戻した。XIAO方式だけ転送中の更新を止め、停止時はDISARM結果へ追記する。
- 配布の`firmware/compatibility.json`へFQBN・descriptor SHA-256・FW source hashを追加し、配布検査でFQBN/descriptorの不一致を拒否、Pesterで公開候補と現sourceの一致を検査する（Pester 6→8）。現sourceに対応する候補は`_internal/dist/jis-ble-input-1.7.0-xiao-preview.4-win-arm64.zip`。旧FW sourceの`jis-ble-input-1.7.0-win-arm64(.zip)`・`xiao-preview.1/.2`と、同梱文書が更新前の`xiao-preview.3`は、利用者の指示で2026-11-02にWindowsのごみ箱へ移動した（ごみ箱を空にするまで復元可能）。1.6.x系の配布物は保持する。
- REPORTのACK待ち中に停止した場合、アプリは結果不明としてUSBを閉じ、基板側のUSB/DTR断・watchdogで解放する。この経路では停止完了ACKを得られず「解放未確認」表示になる。キー間の待機中の停止だけがDISARMの完了ACKを得る。

**実機確認はNOT_RUN**: 基板なしのため、USB/PnPシリアル番号の実一致、DFU/UF2書込・復旧、Bluetooth広告/GATT、キーボード分類、JIS/USとMicrosoft IME入力、実抜去/再接続、長文・連続運用は未確認。特にキーボード分類は利用目的そのものであり、自動検証から合格へ繰り上げない。段階0aの実機先行条件を満たしたとは扱わず、実物なしで準備できるソフトウェアを候補として実装した。

行coverageは未測定で90%達成を表明しない。共通formatter/Fast lintにはC++ adapterがないため、C++は`-Wall -Wextra -Werror`とホストテスト、手動書式確認を別に記録する。計測・C++ formatter統合の残作業はTODOのJBI-XIAO-TOOLING-20261102で管理する。通信実測の最大遅延や物理BLE競合も未確認。

再現コマンド・未実施の初回確認は基板READMEと通信仕様を参照する。旧版での運用保守方針は以下の履歴であり、新規XIAOの実機確認を済ませたという意味ではない。

# 1.6.5 送信失敗時の解放表示・接続先識別・運用移行

2026-11-02 JST。実装計画との照合で判明した表示不足を修正した。実機受入について、利用者は「実際に運用して不具合があれば都度直す」「特にプランは必要ない」と決定した。全代表欄・長文・再接続等の未実施項目は合格へ読み替えず、別受入プランは作成しない。以下の旧版の「受入待ち」は当時の管理方針であり、現在の完成待ちゲートではない。

- MainWindowの送信結果処理を共通化し、通知例外と解放失敗が同時に起きた場合も「キー解放は未確認」と手動確認の案内を表示する。解放操作を送信できた場合も受信側実状態とは区別する。本文検査・接続確認で開始前に拒否した場合は、過去の解放結果を表示しない。
- 接続先に機器IDのSHA-256先頭8桁を付け、ツールチップに完全なハッシュを表示する。生の機器IDや本文を保存・ログ出力しない。通知先の検査は従来どおり完全な機器ID・接続世代・ペアリング・購読先を使い、表示用ハッシュで認証しない。
- 実際のStagedSenderとMainWindowへfake transportを接続し、通知/解放の両失敗、通知失敗/解放送信成功、取消、本文検査拒否、開始前接続拒否の画面結果を検証した。同名PC・改名時の識別も含めUI39チェック成功。機器への広告・接続・キー入力・ラジオ操作は行わない。
- 実在しないsolutionや未実行のdotnet testに依存せず、各チェックプログラムをビルド・実行する`Test.ps1`を追加する。チェック実行件数が出ない場合も失敗とし、ログ・結果を残す。
- 1文字ずつの手動確認・改行確認チェック・旧日本語速度は戻さない。440×480・最小360×380、専用アイコンとトレイ格納を維持する。

既知の制約: 非発見広告は実機で開始不良が報告されたため使わず、新規発見可能な広告とWindowsのペアリング承認を使う。新規受付をアプリ単独で時間/操作限定したとは表明しない。遠隔IME・フォーカス・本文一致・キー解放実状態の自動確認も行わない。運用上の問題が報告された時に、版・入力方法・症状を特定して修正する。

# 1.6.4 コンパクトUI・専用アイコン

2026-11-01 JST。利用者の指定により440×480を基本とする小型UIへ変更し、4案から選ばれたA「キーボード＋無線」のアイコンを採用した。

- 主画面は送信先・入力方法・速度・本文・送信を中心とし、最小360×380まで縮小可能。ペアリング/再接続/受信配列/IME準備/診断は別の設定画面へ移した。重要なエラーと改行数は主画面に残す。
- 送信中だけ一時停止/停止を表示し、一時停止中は続行を主ボタンにする。Ctrl+Enterで送信/続行、Enterで改行、Tabでフォーカス移動。送信workerの宛先・原文・速度固定と接続世代の扱いは変更していない。
- AppIcon.icoをEXEのWin32 resourceとmanaged resourceへ埋め込み、ウィンドウとNotifyIconも同じICOから読み込む。16/20/24/32/48/64/128/256pxの8サイズを保持する。画像・フォントの外部通信は行わない。
- UIテストは利用者の設定を変更しないよう、workspaceのscratchpadにある専用設定ディレクトリを使用する。Bluetooth/GATT/ラジオ切替は起動しない。設定ウィンドウ、方法別の速度保持、最小サイズ、操作ボタンの状態、専用アイコン、トレイのライフサイクルを検査する。
- Windows UIテスト31チェック成功。440×480、最小360×380、設定画面のWPF描画を作成して表示を確認した。送信側Coreのソース・日本語速度・キー列は作業前とのSHA-256で確認する。

受信PCを伴う実入力・トレイ格納中の実接続・実機のタッチ操作/各DPIの目視は今回の検証には含めない。過去の受入範囲は以下の履歴を参照する。

# 1.6.3 タスクトレイ格納

2026-11-01 JST。利用者の指示により、右上の×は終了ではなくタスクトレイへの格納へ変更した。

- Closingを取り消してからDispatcherへHideを予約する。WPFのClosing中に直接Visibilityを変更しない。格納では送信取消・lifetimeの取消・GATT破棄・Bluetoothのオンオフを実行しない。
- NotifyIconを表示し、ダブルクリックまたは右クリック →「画面を開く」で同じウィンドウを復帰する。二重起動からの表示要求も同じ復帰処理を使う。最小化状態からも通常表示へ戻す。
- トレイの「終了」だけが既存の送信停止・接続終了・Bluetooth復帰確認へ進む。終了とApplication.Exitでアイコンを破棄する。サインアウト/Windows終了は格納として取り消さない。Windows起動時の自動常駐登録は行わない。
- Windows UIテスト12チェック成功。実際のMainWindow/Closing/NotifyIconを使い、格納中の生存、反復復帰、最小化からの復帰、トレイメニュー、明示終了、終了後の復帰抑止、隠れた状態からのセッション終了を検査した。テスト用windowは画面外・タスクバー非表示で生成する。テスト用オプションでBluetooth開始を無効にし、GATT公開・本文送信・無線切替は行わない。
- Microsoft.WindowsDesktop.Appは既存の参照/runtimeを使い、新しい依存パッケージは追加していない。.NET 10.0.14と日本語の速度・キー列は維持する。

トレイの見た目の手動確認、接続した受信PCを伴う格納/復帰、実際のOSサインアウトは未実施。上記のUIコード試験と実機受入は区別する。下の1.6.2以前の未検証事項は、当時の状態を記録した履歴である。

# 1.6.2 接続終了・配布構成の修正記録

2026-11-01 JST。今回はWindowsの開発環境で確認した。下の旧版記録にあるmacOSの記述は当時の環境であり、今回の実行環境ではない。

## 現行版の変更と検証

- 利用者の指示により、日本語の速度短縮は問題として扱わず、1.6.1の速度・待機値・キー列・表示ラベルを維持する。旧標準速度の追加はしない。
- 終了時のオフ要求受理と実際のオフ状態、接続消失、オン要求受理と実際のオン状態を分けて確認する。オフ要求が遅れて完了する場合も、オン復帰が先行しない。復帰拒否・状態待ち時間切れは成功扱いせず、終了前に再試行・手動復帰の案内を表示する。
- 切断未確認・サービス解放失敗も結果として返す。未接続時はラジオを操作しない。
- 非発見広告が成立しなかった「新規発見を終了」をUIとその呼び出し経路から外す。
- 名前付きmutexで1.6.2以降の同一セッション内の複数起動を抑止し、後発の起動から既存ウィンドウへ表示要求を送る。
- READMEを現行手順に統一し、旧チェックボックス・旧速度の説明は明示したdocs/HISTORY.mdへ保存する。入力欄に改行Enter回数・先頭/末尾改行を表示する。確認チェックは復活させない。
- 共通global.jsonを変えず、製品のruntime/host/ILLinkを10.0.14へ固定する。Microsoft公式NuGetから取得した4パックの署名検証は成功。Windows DesktopのNuGetパックにないnoticeは、同じv10.0.14の公式WPF/WinFormsタグから原文を取得し、出典とハッシュを保存する。
- 既存618チェックに復帰失敗・状態変化待ち・遅延オフ要求・二重起動の20チェックを追加し、Core638チェック成功。実機を操作しないradio portと、実際の終了coordinatorを組み合わせて検査する。
- 梱包スクリプトは元EXEをハッシュ付きで退避し、説明・サンプル・ライセンスを収集する。必要ファイル、ARM64 PE、EXEバージョン、EXEに実際に含まれるruntime版、notice版とハッシュを検査し、manifestと新規ZIPを作る。
- 実際の1.6.2 publishを使う配布検査4テスト成功（現物の正常検査、版の不一致、内蔵runtimeの不一致、上流license欠落の拒否）。テスト用のレジストリとTestDriveを無効にして実行した。製品範囲formatter Checkは42ファイル・変更必要0、モジュールのトップレベル副作用検査は1モジュール成功。

受入の制約: 実際のSurfaceラジオ切替、終了時の再試行画面、実機の再接続、二重起動時の前面表示、長文・IMEの全文一致は今回未検証。Coreテストの成功を実機受入へ読み替えない。前版の正常動作報告は下の履歴を参照する。

## 1.6.1以前の検証履歴

以下は当時の記録であり、現行操作はREADME.md、今回の変更は上の節を参照する。

# 実験版の検証記録

2026-11-01 JST。開発環境はmacOS ARM64、SDKはワークスペースの`global.json`に従います。

## 2026-11-01 JST: 1.5.1の発見不能と0x8000000Eの調査

利用者の追加画像で、1.5.1が「広告開始」の段階でInvalidOperationException/HResult 0x8000000Eとなることを確認した。受信側の一覧にSurfaceが現れず、以前の版では接続できたという報告も受領した。1.5.1の実機成功は認定しない。

Microsoftの[汎用COMエラー一覧](https://learn.microsoft.com/en-us/windows/win32/com/com-error-codes-1)ではE_ILLEGAL_METHOD_CALLに当たる。旧版との直接比較で、1.5.1にはStartAdvertising前のAdvertisementStatus読取が増えている。最初の読取はCore helperのtryの外にあり、ここで例外が出るとStart/Stopに到達しない。旧1.3.0のtransportは開始前にこのpropertyを読まない。

製品Coreを参照するignoredなprobeで、状態読取・開始呼出・再試行時の停止に同じHRESULTをそれぞれ注入した。いずれも同じメッセージとなり、最初のケースはStart呼出0回で終わることを確認した。これは制御経路の模擬検証であり、Surfaceのnative getterが実際に失敗することの証明ではない。現在の「広告開始」診断だけでは失敗APIを判別できず、Windows側の残存状態や複数起動もまだ排除していない。

正常動作報告のある1.3.0の保存済みZIPを元のSHA-256で検証してLLM連絡用へ再配置し、新旧同時起動を避けて既存ペアリングのまま比較する質問を利用者へ提示した。比較結果は未受領。製品sourceと配布済みZIPは変更していない。詳細はignoredな`result/jis-ble-input/advertisement-investigation-20261101.md`と3ケースのprobe-results.jsonlに記録した。

## 2026-11-01 JST: 1.5.1 起動失敗の調査・広告状態と再試行の修正

利用者提供の1.5.0の画面で、`TimeoutException`により開始が失敗し、接続操作の3ボタンが無効になっていることを確認した。画像自体をコピー・加工せず、表示内容を失敗の証拠として扱った。旧版には段階・広告状態の診断がなかったため、GATT作成の5秒待ちと広告の10秒待ちのどちらが実機で時間切れになったかは特定できない。

確認した実装欠陥:

- 広告の`Started`だけで開始待ちを完了し、`StartedWithoutAllAdvertisementData`を成功した要求として取り込まず、`Aborted`も即時失敗へ移さない。どちらも汎用timeoutへ化け得た。
- 1.5.0で自動起動を新規発見不可へ変えた。Microsoftは両広告フラグがtrueの場合にサービスUUIDを広告へ加えるとしており、HID識別情報の再告知に影響し得る。ただし今回の実機timeoutの直接原因だと断定できる証拠はない。
- 失敗後にproviderが残り、再度の開始を拒否する。UIもattemptedStartでボタンを無効にし、アプリ内の復旧経路がなかった。

修正内容:

- 自動起動も従来成立した接続可能・新規発見可能の設定へ戻した。送信先の前回PC照合・確認チェック・本文の非自動送信は維持する。
- 実際のWindows transportから呼ぶ`AdvertisementStartup`をCoreへ切り出し、広告開始2種類・中断・停止・timeout・取消を別に処理する。省略ありは接続成功とはせず警告する。現在の状態も100ms周期で読み、イベントだけに依存しない。状態取得で不明なBluetoothErrorは「未取得」とする。
- startGateで初期化を直列化し、広告だけが失敗した場合は作成済みのGATT graphを再利用する。作成途中の失敗時はキャンセル後にイベントを解除し、provider/characteristic/機器参照を片付ける。広告設定変更では停止を確認してから開始する。同じ設定で既に広告中なら重複開始しない。
- 広告イベントでは現在のpropertyを読み、古いcallbackが前のStarted状態を復活させないようにした。公開失敗中は送信可能にせず、宛先も自動復帰しない。
- 「Bluetoothを再試行」を追加し、開始失敗後も接続操作を有効にする。接続診断欄を別に設け、停止通知でエラー表示が上書きされないようにした。段階、状態、取得したError、HResultを表示する。秘密情報・本文・機器IDを記録しない。
- Core 584チェック成功（既存553＋追加31）。full/reduced開始、同期イベントとpropertyのみ、非同期イベント欠落、中断の即時通知、同じpublisherの再試行、timeout/取消/同期例外後の後始末、停止と開始の順序、停止timeoutと重複開始抑止を検査した。1.5.0のStarted-only不備に対応する回帰を含む。
- Windows ARM64 build/publish成功（警告0・エラー0）。Project formatter/Fast lintと関連2文書のCheckを実施。入力encoder、速度、送信worker、HID descriptorは変更前とのハッシュ一致を確認する。新依存は追加していない。
- 新候補は`jis-ble-input-1.5.1-win-arm64.zip`。旧ZIPは保持し、標準ペアリングの削除・PC再起動・Bluetooth設定リセットを修正の前提にしない。

**修正版での実機の広告開始・再接続・HID購読復帰は未実施**。画像のtimeoutが広告開始の取りこぼしによるものか、GATT構築/OS側の別要因かは、新しい段階診断と実機結果で区別する。入力の正常動作報告を再接続の成功へ拡張しない。

仕様根拠: [広告状態enum](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattserviceprovideradvertisementstatus?view=winrt-26100)、[広告イベントのError](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattserviceprovideradvertisementstatuschangedeventargs.error?view=winrt-26100)、[Microsoft GATT Server](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-server)。操作は[README](README.md)。

## 2026-11-01 JST: 1.5.0 起動時の接続待ちと前回PCの復帰

利用者から、アプリを閉じて開き直すとBluetooth接続が切れ、一般的なキーボードのように自動復帰できないかとの指摘があった。旧版は`Dispose`で広告を停止し、次回は手動ボタンを押すまでGATT公開を開始しないことをコードで確認した。

- 起動時に接続可能・新規発見不可の広告を開始し、登録済みPCからの接続を待つ。初回/追加の登録用に「新しいPCとペアリング」で新規発見可能へ切り替える。「新規発見を終了」は接続可能・新規発見不可へ戻す。HID service UUID・descriptor・送信キー列・速度は維持する。
- ユーザーが接続先を選んだ際のIDのSHA-256と自動再開オン/オフ、schema versionだけを送信側のLocalApplicationDataへ保存する。名前・生のID・本文・キー列・ペアリング認証情報は保存しない。Windowsのペアリング情報の変更や、PC起動時の常駐登録は行わない。
- 接続・ペアリング済みHID購読クライアントの一覧に前回PCが戻った場合だけ送信先を復帰し、確認チェックを外す。他のPCへの代替・送信中の宛先変更・本文の自動再送/続行は追加しない。機器IDの表記がOS側で変化したときは照合できず、ユーザーの手動選択を必要とする。
- `ConnectionPreferencesStore`は最大4KiBのJSONを検証して読み、書き込みは同一ディレクトリの一時ファイルと置換で行う。schema/ハッシュ不正・破損・大きすぎる設定を拒否する。読み込み失敗は自動再開オフへ、保存失敗は次回の手動選択案内へ進める。設定の復旧のために既存ペアリングを削除しない。
- 起動処理のTaskを終了処理から待ち、20秒の開始timeoutとアプリ終了のCancellationTokenをGATT作成/広告待ちへ伝える。閉じた後に広告を開始し直すことを避ける。開始失敗は型名と手動案内だけで表示し、設定ファイル内容や機器IDを出さない。
- Core 553チェック成功（既存529＋追加24）。設定の実ファイル保存/新instanceでの再読込・オン/オフの永続化・送信先の明示置換・原子保存・破損/サイズ/未知schema拒否・不正保存で元ファイルを保持・前回PCだけの選択・本文の非送信を検査した。Windows GATT APIや実機の再接続をこのモデルで実行したとは扱わない。
- Windows ARM64 publish成功（警告0・エラー0）。Project formatter/Fast lintと関連2文書のCheckを実施。新依存は追加しない。接続復帰のWindows実機受入・HID購読復帰・新規/非新規発見の実際の効果は未検証である。
- 新版ZIPは`jis-ble-input-1.5.0-win-arm64.zip`。1.3.0/1.4.0を含む旧ZIPと既存サンプルを保持する。初回は接続先を1回選んで記録し、その後アプリ終了→起動で公開と同じPCの購読/選択が戻ること、勝手な文字入力がないこと、受信側の電源オフ/別PCだけでは未接続のままであることを実機確認する。

仕様根拠: [Microsoft GATT Serverの広告・購読クライアント](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-server)。広告を接続可能にすることと、相手WindowsのHOGP再接続が成立することを分けて記録する。受信側への追加ソフト不要・Bluetoothのみ・コマンド実行不可を維持する。

## 2026-11-01 JST: 1.4.0 入力方法と速度の選択欄を分離

利用者は追加UIの形式として「入力方法と速度を別々の選択欄にする」と回答した。画面上部を入力方法（日本語・混在文/英数字・URL）、入力速度、受信側配列へ分け、「現在の設定」を表示する。速度の選択を入力方法ごとにセッション内で保持し、アプリ終了後に保存しない。

- 日本語は標準1.30秒/文字・少し速め1.05秒/文字、英数字は高速0.08秒/文字を1.3.0と同じキー列・待機で維持する。英数字にゆっくり0.12秒/文字を追加し、押下・解放を各40→60msへ延ばす。初回/続行のImeOff準備0.30秒と改行0.35秒は変えない。実機より速い設定の追加や実速度測定は行っていない。
- `InputSettings`は選択中の方法、各方法の速度、配列を保持し、送信時にimmutableな`SendOptions`へcaptureする。方法切り替え時に速度欄を再構成する間は選択イベントを抑止し、意図しない初期値への書き戻しを防ぐ。無効な選択は現在値を変えず拒否する。
- 方法・速度・配列の変更時は受信側確認を解除する。送信中・一時停止中は3選択欄を無効にする。既存workerの原文・宛先世代・capture済み速度にはdraftの変更を反映しない。停止・続行・非自動再送を維持する。
- Core 529チェック成功（既存497＋追加32）。方法往復での速度保持、初期値、無効選択時の非変更、captureの独立性、両ASCII速度×JIS/USで95文字とURL・改行の一致、実際の待機積算、停止中のdraft変更後も同じ速度・同じキー列で続くことを検査した。元の497チェックを維持する。
- Windows ARM64 build/publish成功（警告0・エラー0）。Project formatter Check/Fast lintと関連2文書のCheckを実施した。新依存は追加せず、HID descriptorと日本語encoderは変更前と同一であることを直接ハッシュ比較する。C#分岐カバレッジは未測定。
- 新版は`jis-ble-input-1.4.0-win-arm64.zip`。正常動作報告がある1.3.0のZIPとそれ以前の配布物を保持する。既存のASCII・改行・長文サンプルを同じ内容で同梱する。
- **分離した選択UI、セッション内の設定保持、英数字ゆっくりのWindows実機受入は未実施**。日本語少し速め・英数字高速の正常動作報告は下の1.3.0の記録へ限定する。全記号・両配列・長文・改行・再接続の受入と実測速度は保留する。

操作は[README](README.md)を参照。送信前の全文検査、受信側への追加ソフト不要・Bluetoothのみ・コマンド実行不可の条件を維持する。

## 2026-11-01 JST: 1.3.0の速度モードの正常動作報告

配布後、利用者から「日本語の少し速めモードも、英数字・URLの高速モードも正常に動作しました」と明示報告があった。両モードの基本入力が利用者環境で成立した実機報告として記録する。

試験したアプリ・入力欄、受信配列、IME/OSのversion、実際の文章・文字数・文字コード列・所要時間・実行EXEのhashは取得していない。JIS/US両配列、ASCII 95文字全部、2,000字、改行、一時停止/続行、再接続の全試験が完了した報告へは拡張しない。Core 497チェックの受信モデルとも区別する。

直前に案内した配布候補は`jis-ble-input-1.3.0-win-arm64.zip`、SHA-256は`b0ec9fba1425164d828a9167aa0c1bf81f13e35cd01731ce4e502376847ec0c0`。配布済みZIPは再生成せず、当時の記録を保持する。追加の速度切り替えUIの実機受入とは分ける。

## 2026-11-01 JST: 1.3.0 日本語の速度設定と英数字・URL直接入力

利用者の実装依頼に基づき、画面で「日本語（標準）」「日本語（少し速め）」「英数字・URL（高速）」を選ぶ機能を追加した。初期値は標準で、正常動作報告がある本文処理へ戻せる。受信側への追加ソフト不要・Bluetoothのみ・コマンド実行不可を維持する。

- 日本語の標準キー列・待機値は変更しない。少し速めは全キー・押下時間・16進数字の待機を維持し、IMEモード待機150→100ms、250→200ms、F5変換待機300→200ms、確定待機200→150msとする。固定合計1,300→1,050ms（約19.2%短縮）で、実機測定値ではない。
- 英数字はU+0020〜U+007Eの95文字と確認済みの改行だけを許可し、全文検査が済むまでImeOffも送らない。大小文字・数字・ASCII記号を直接HIDキーへ変換し、押下40ms＋解放40ms。初回準備はImeOff（LANG2 `0x91`）50ms＋解放250ms。F5・文字確定Enter・原文にない末尾Enterは送らない。
- 受信側の106/109 JISと101/104 US配列を選択できる。記号のキー位置は受信Windowsの配列によるため、受信側で選択一致・IMEオフ（A）・CapsLockオフ・未確定文字なしを確認する。日本語や非ASCIIが含まれた英数字送信は全文拒否し、勝手な混在モードへの切り替えを行わない。
- mode/speed/layoutはimmutableな`SendOptions`としてsnapshot・宛先世代と一緒にworkerへ固定する。送信中と一時停止中のUIのモード選択は無効にし、選択変更時は確認チェックを外す。続行は元の次の文字から、英数字の場合はImeOffを再送してから続ける。取消・切断・世代変更では残りを中止し、自動修復や再送はしない。
- HID descriptorのlogical/usage maximumを`0x91`まで拡張し、8-byte report幅は維持する。旧定義のキャッシュは送信側から読み取れず、自動ペアリング削除・再作成は行わない。
- Core 497チェック成功（既存214＋追加283）。標準/少し速めの同じキー列・押下時間、1,050msの積算、日本語/改行の受信モデル、JIS/USそれぞれの95文字・重複キー・URL・2,000字、ASCII全文拒否、無効options、準備だけ、キー途中の停止・pause/resume・世代変更・元のモード固定・ImeOff再指定を検査した。ImeOff無視/配列不一致のモデルではAPI成功でも一致しない反例を確認し、実機成功の保証へ読み替えない。
- Windows ARM64 build/publish成功（警告0・エラー0）。Project formatter/Fast lintと関連2文書のCheckを実施。新依存は追加していない。C#分岐カバレッジは未測定で90%達成を宣言しない。
- 新版は`jis-ble-input-1.3.0-win-arm64.zip`。旧ZIPを保持し、改行試験文・既存長文に加え、末尾改行なしのASCII URL/95文字サンプルを同梱する。本文の自動保存や外部通信は追加していない。
- **短縮した日本語待機、英数字入力、モード選択UI、実際のJIS/US配列・ImeOffのWindows実機受入は未実施**。既存の利用者報告は旧版の基本一括入力に限定する。速度は固定待機の計算のみで、実Bluetooth/API待ちを含む文字/秒の測定は残る。

仕様根拠: [Microsoft ImeOn/ImeOff](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/keyboard-japan-ime)、[Microsoft 106配列の文字対応表](https://github.com/microsoft/Windows-driver-samples/blob/main/input/layout/fe_kbds/jpn/106/kbd106.c)、[USB HID Usage Tables](https://www.usb.org/sites/default/files/hut1_21_0.pdf)、[QMKのJISキー位置定義](https://github.com/qmk/qmk_firmware/blob/master/quantum/keymap_extras/keymap_japanese.h)。[操作手順](README.md)も参照する。

## 2026-11-01 JST: 1.2.0 メモ帳・通常の複数行欄への改行一括入力

利用者は今回の対象を「メモ帳と通常の複数行入力欄」と明示した。Enterで改行する欄を前提に、通常の文字のIME処理を保ち、改行だけを独立したEnter押下・解放（50ms + 300ms待機）の送信単位へ分離した。受信欄の自動判別・アプリ別設定・文字ごとの確認は追加していない。

- `TextSnapshot`は原文を保持し、CRLFを1単位、LF/CR単独を各1単位とする読み取り専用の一覧を持つ。元のoffset/lengthを保持し、先頭・末尾・連続改行を除去しない。最大2,000単位で、CRLFのみなら原文4,000 UTF-16文字まで扱う。進捗も同じ単位を使う。
- Core APIの既定値は改行拒否を維持する。WPFは送信前の確認チェックに「Enterで改行できる複数行欄」の条件を加え、その確認時に改行を許可する。未対応のTab・補助平面・結合文字等が後方にあっても、全文検査が終わるまでモード指定も送らない。
- `EncodeCharacter`の処理本体と待機値は1.1.0との直接比較で同一。改行にF5・16進コード・Shiftは使わず、各改行で通常Enterを1回だけ送る。直前の文字の確定と解放を終えてから改行し、続く文字には従来どおりIMEモードを再指定する。
- Core 214チェック成功（既存128 + 追加86）。受信モデルでCRLF/LF/CR、先頭・末尾・連続改行、改行だけ、受信側LF/CRLF表現、2,000単位の上限、全体事前拒否、改行前後と押下中の一時停止・続行、停止・世代変更を検査した。キー送信成功を実機のIME変換成功に読み替えない。
- Windows ARM64 publish成功（警告0・エラー0）。Project formatter/Fast lintと関連2文書のCheckを通過した。TODO.mdの無関係な既存空行は変更しない。新依存は追加していない。
- `jis-ble-input-1.2.0-win-arm64.zip`にCRLF/LFの合成試験文を追加する。各文章は途中の空行と末尾改行を含む。正常動作報告がある旧一括版と1.1.0のZIPは保持する。
- **改行入力・新UIのWindows実機受入は未実施**。受信欄の内部改行表現はその欄に従い、元のLF/CRLF/CRのバイト・コードポイント列がすべての欄で一致するとは保証しない。行の再現と厳密な改行コード一致を区別し、元の全受入・製品完成は未完了とする。

仕様根拠: [Microsoft Edit Controls（ES_WANTRETURNとEnter）](https://learn.microsoft.com/en-us/windows/win32/controls/about-edit-controls#text-and-input-styles)。詳細な操作は[README](README.md)。受信側への追加ソフト不要・Bluetoothのみ・コマンド実行不可は維持する。

## 2026-11-01 JST: 1.1.0 一時停止・続行と長文送信の表示

利用者の「実装を続けてください」に基づきP2/P3を継続した。正常動作の報告を得た一括入力版は配布済みZIPのまま保存し、IMEキー列・待機時間を定義する`ImeKeyEncoder.cs`は変更前とのSHA-256一致を確認して維持した。

- 一時停止要求は文字の変換・確定を終えた境界で適用し、all-keys-up送信後に`Paused`とする。途中のコードやShift押下中のまま確認を待つ方式にはしない。すぐ止める送信停止は途中でもキャンセルする。
- 続行は同じworker、元のsnapshot、同じ宛先と接続世代に限る。最初からの再送や、別PC・再接続先への残りの自動送信はしない。停止/終了/エラーで続行を破棄し、停止中も送信停止を受け付ける。
- `TransferProgress`は処理済み/総文字数、処理時間、残り時間の推定だけを返し、本文・宛先名・機器IDを含めない。組込みの`TimeProvider`による単調時刻で計測し、一時停止中の時間を除く。WPFに文字数・進捗バー・時間と一時停止/続行ボタンを追加した。
- 公開中は接続先を約3秒ごとに更新し、送信先の使用可否を約0.5秒ごとに確認する。`RefreshHostsAsync`は実行中の更新へ次の更新を積み上げない。接続先の自動選択・ペアリング操作・文章の自動再送は追加していない。
- App versionを`1.1.0`とし、画面とWindowsのファイル情報で識別できるようにした。既存依存の追加/更新はなく、旧配布物を上書きしない。
- Windows ARM64 build/publish成功（警告0・エラー0）。PEのMachine `0xAA64`、バージョン1.1.0、新UIの文字列を実際のEXEで照合した。Project formatter Check 25件変更なし・Fast lint PASS、関連する計画/実機報告2文書のCheck PASS。TODO.mdにある既存の無関係な空行1行の形式差は変更しない。
- Core 128チェック成功（既存96＋追加32）。コード途中での一時停止要求、文字確定後のキー解放、複数回の続行、停止中のキャンセル、世代/宛先変更の続行拒否、キー解放失敗、二重workerの拒否、最後の文字での一時停止要求、10分の停止時間を加えても処理時間/推定が変わらないことを検査した。旧版同様の12条件・100/1,000/2,000文字は受信モデルでの試験であり、新版の実機受入ではない。
- 新版のWPF画面の実機表示、一時停止/続行、停止中のフォーカス再確認、実際のBluetooth切断/再接続と状態更新、長文のコードポイント全文一致は未検証である。1.0相当の旧一括版の利用者報告を1.1.0の新機能の成功へ読み替えない。改行・Tab・補助平面・結合文字の要件は引き続き未達。

実行コマンドはREADMEのCore試験・Windows ARM64 publish・Project formatter/Fast lintを使う。変更前との比較用snapshotと新配布物の検証記録はworkspace内のignored領域へ保存する。API仕様は[Task.WaitAsync](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.waitasync?view=net-10.0)、[TimeProvider](https://learn.microsoft.com/en-us/dotnet/api/system.timeprovider?view=net-10.0)、[GATTの対象宛先指定通知](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattlocalcharacteristic.notifyvalueasync?view=winrt-26100)を参照した。

## 2026-11-01 JST: 一括入力版の正常動作報告

最新の一括入力版ZIPを「LLM連絡用」へ格納した案内の後、利用者から「正常動作しました。メモ帳です。Googleもうまくできました。」との報告があった。メモ帳とGoogle側の入力で一括入力が正常動作した実機報告として記録する。「Google」は直前のChromeでの入力試験の文脈と解釈するが、具体的なサービス・入力欄やアプリのversionは未特定である。

実際に送った文章・文字数・コードポイント列、旧IMEの互換設定、OS/IMEのversion、受信側で実行されたEXEのhashは取得していない。したがって基本一括入力の利用者による成功報告に範囲を限定し、100/1,000/2,000文字の実機試験、全アプリ対応、再接続、新旧IMEすべてでの対応やP0b全体の合格へ拡張しない。Core 96チェックの受信モデル試験とも区別する。

配布した候補は`jis-ble-input-bulk-ime-win-arm64.zip`。案内したZIPのSHA-256は`9074b81e0729b8c35b28a5173d2e3e225b435446adcdd7dc018ce73769aea418`で、格納時にコピー元との一致を確認済み。配布済みZIPはこの追記で作り直さず、配布時点の内容を保持する。

## 2026-11-01 JST: IME制御の見直しと文章一括入力

利用者から「1文字ずつ手動で確認するのではなく、日本語の文章をまとめて正しく入力できるように、IMEの制御方法から見直す」指示があった。直前の段階確認版は診断用の履歴とし、通常の操作から文字ごとの確認を撤去した。

- `ImeKeyEncoder.EncodePreparation`はLANG1 `0x90`のImeOnと`Ctrl+CapsLock`を押下・解放する。Microsoft公式資料は前者をひらがなモードへの移行、後者をひらがなキー相当と記載する。トグル式のキーは使わない。
- `EncodeCharacter`は各文字の直前に上記のモード指定を再実行する。その後に大文字4桁コード、F5、変換待ち、Enter、確定待ちを順序付ける。確定後の入力モードを次の文字へ無条件に持ち越さない。1文字の固定待機合計は1,300msで、受信側での実測値ではない。
- `StagedSender.SendAsync`は全文検査・immutable snapshotの作成後、1つのworkerで全文章を逐次送信する。利用者が押す送信ボタンは1回だけで、途中の人による確定操作はない。任意の`PrepareImeAsync`はモード指定だけを行い、本文・F5・Enterを送らない。
- Core 96チェック成功。初期状態がIMEオフ・ひらがな・英数の3種、LANG1の無視、文字確定のたびの英数モードへの復帰を組み合わせた12条件を検査した。従来列では「日」の確定後に次の`672C`が通常文字となりF5/Enterの副作用が出る反例を再現し、再指定する列で混在文を一致させた。100/1,000/2,000文字の全文一致・worker競合・編集中のsnapshot保持・切断世代・モード/コード/F5/確定の途中取消・解放失敗も検査した。
- 受信モデルはIMEのモード遷移を検査する独立したテストモデルであり、実際のWindows IMEやBluetoothを実行したものではない。両方のモード指定が効かない受信モデルでは、API成功でも日本語は一致しないことを別の反例で確認した。`KeysSent`を`Inserted`へ読み替えない。
- 受信側の「以前のバージョンのMicrosoft IMEを使う」は、今回の利用者回答で**未確認**。現行IMEのF5対応を確認できた一次資料はなく、デンソーウェーブの自社機器FAQにはWindows 10 2004以降で旧IMEへの互換設定が必要になる場合の説明がある。同社方式と今回のキー列の同一性は未確認であり、全環境で旧IME必須・新IMEで不可能とは断定しない。
- Windows ARM64のbuild/publish成功（警告0・エラー0）。PEのMachine `0xAA64`と一括入力版のUI文字列をEXEで照合した。Project formatter Check 25件変更なし・Fast lint PASS、関連の計画/実機報告2文書のCheck PASS。TODO.mdには既存の無関係な項目間の空行1行で一括形式検査の差が残るため、その空行は変更していない。Windows ARM64のビルド・配布物の検証と、実機の受入を分離する。新配布物は`jis-ble-input-bulk-ime-win-arm64.zip`、段階確認版と旧自動送信版は上書きしない。実機受入はメモ帳とChromeの無害な欄に`日本語ABC橋。`を一括入力し、全文一致・日時/コード/余分なEnterがないことを確認してから100字以上へ進む。配布時点では新しい自動処理のWindows実機受入は未実施だった。その後のメモ帳・Google側の正常動作報告は冒頭の節へ記録した。1文字ずつの手動確認を要求達成に数えない。

仕様根拠: [Microsoft日本語IMEのキーボード操作](https://support.microsoft.com/en-au/windows/hardware/input-devices/microsoft-japanese-ime)、[ImeOn/ImeOffのHID仕様](https://learn.microsoft.com/ja-jp/windows-hardware/design/component-guidelines/keyboard-japan-ime)、[デンソーウェーブの互換設定FAQ](https://www.denso-wave.com/ja/adcd/support/faq/scanner/setup/scn_h21122101.html)。

## 旧・段階確認版の記録: 日本語IMEが働かない場合の修正

- 最新の利用者報告を優先する。添付のメモ帳出力には`65E5`等のコードと日時が並び、Chromeでも英数字入力の後のEnterによる異常が報告された。添付自体は変更・複製していない。
- 旧版は受信側IMEの状態を確認せず、各文字の16進コード→F5→Enterを自動連続送信していた。送信成功は日本語変換の成功を意味せず、日時はIMEに消費されなかったF5の副作用と整合する。受信側IMEがオフ・直接入力だったのか、コード変換が効かなかったのかは送信側から確定できない。
- ImeOn専用キー（HID LANG1 `0x90`）を各コードの前に送り、アプリのHID定義のlogical/usage maximumも同じ値まで拡張した。半角/全角のトグルは使わない。
- コード入力の後はF5前で停止し、利用者が未確定コードを確認した操作でだけF5を送る。F5後も停止し、利用者が正しい未確定文字を確認した操作でだけEnterを送る。すべての文字で同じ段階を踏み、残りの自動一括送信を撤去した。
- 自動試験: Core 81チェック成功。通常の連続ステージ、IME-onが無視される受信モデル、F5変換に失敗する受信モデル、Enterの順序・二重確定拒否、段階ごとの切断・取消、実際のHID定義と8-byte reportを検査した。受信モデルの成功はWindows実機での互換性の証明ではない。
- Windows ARM64版の再ビルド・publishに成功（警告0・エラー0）。PEのMachineが`0xAA64`で、EXEに段階確認版の3つのUI文字列が含まれることを照合した。旧配布物は保持し、新規ZIPを別名で生成する。実機での段階確認版のIME切替・変換・ペアリングキャッシュは未検証である。
- Project formatter CheckとFast lintはPASS。関連する計画・実機報告の形式検査もPASS。TODO.mdの一括形式検査は既存の別項目間の空行1行で不一致となり、その無関係な空行は変更しなかった。
- 100字・1,000字の自動送信探索は保留。まずメモ帳の`日`で「あ」・下線付きコード・変換後の文字を確認し、その後同じ段階をChromeのダミー欄で確認する。詳細は[使い方](README.md)。

## 旧版の実施履歴

- 2026-11-01 JST、実験版から`ABC 123 日本語 髙橋 﨑 ｱｲｳ ①㈱ ～−`を送る案内に対し、利用者がメモ帳とChromeの両方で文字抜け・重複・余分な改行なく同じ表示になったと明示報告。両アプリの混在短文について目視一致を記録する。欄の詳細・version・コードポイント全文一致は未確認。
- 2026-11-01 JST、実験版の公開・ペアリング・最初の1文字の確認・残りの送信という案内後に、利用者から「すべて成功しました」と報告。直前の`A橋B`送信手順全体への成功報告として記録する。こちらによる実機観測ではなく、実際の対象アプリ一覧・OS/build・厳密なコードポイント比較は未取得。表示名変更・長文・再接続の成功には読み替えない。
- .NETのWindows API/WPF参照を使ったWindows ARM64向けビルド: 成功、警告0・エラー0。
- ARM64の自己完結EXEへのpublish: 成功。PEのMachineは`0xAA64`。EXE内に開発者ホームパスのUTF-8/UTF-16表現がないことを確認。
- Project範囲のformatter CheckとFast lint: PASS。Fast lintはformatterの検査であり、C#の専用静的解析を実行したという意味ではありません。
- Coreの実行試験: 44チェック成功。HIDの独立した期待usage、4桁ゼロ埋め、Shift、F5/Enter、押下/解放、全文の事前拒否、immutable snapshot、確認前の非送信、取消、世代変更、部分失敗、解放失敗、非自動再送を検査。
- 試験は外部テストフレームワークを追加せず、終了コードで失敗を伝える.NETコンソールとして実行。C#の分岐カバレッジは未測定で、計画の90%を達成したとはしない。
- 既存のP0補助ツールについては、[検証準備記録](../docs/jis-ble-input-preflight.md)に24件のPowerShell試験と17件の診断判定試験を別記。

## 未実施・未達

- HOGPの購読状態・通信経路の独立したログ確認。上記の送信成功報告から基本経路の成立が示されるが、内部状態を直接観測した結果ではない。
- Windows実機でのWPFのIME開始/確定/取消、タッチキーボード、貼り付け。
- 全代表欄・全必須文字・混在文章・長文のコードポイント列一致。
- 100文字の初回探索、長文対象欄の独立した容量確認、計画の100字20反復・1,000字3反復。初回探索は各10文字の`01日本語ABC橋。`から`10日本語ABC橋。`までを改行なしで結合した100文字とし、正式なAC-08コーパス・反復・全文照合の代わりにはしない。
- 改行は1.2.0でソフトウェア実装済みだが、今回対象のメモ帳・通常の複数行欄で実機受入は未実施。Tab・補助平面・結合文字は引き続き全文拒否する。
- Windows再接続、PIN、LED取得、スリープ・アプリ再起動の受入、文字/秒の実測。

P0b全体の合格は宣言していません。手動入力とSurface能力の部分確認を根拠に、利用者の継続指示を受けて通信試作を先行しています。本文の広い互換性と完成判定は保留しています。受信側へのソフト導入不要・Bluetoothのみ・コマンド実行不可は維持しています。
