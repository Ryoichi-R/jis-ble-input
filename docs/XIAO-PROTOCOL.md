# XIAO USB protocol 1 / firmware 0.1.0・0.1.1

対象はXIAO nRF52840 Senseとアプリ1.14.0。HMACは利用者の選択により採用しない。USBを開ける送信PCのプロセスを信頼する。CRC・session・USBシリアル番号は送信元認証ではない。

## 接続とフレーム

USB CDC、115200bps、8N1、DTRオン、flow controlなし。通常時VID/PIDは2886:8045。bootloaderの0045は「書込モード」、通常版（8044/0044）・Plus等の他PIDは「対象外」として一覧に表示するが開かない。列挙ではポートを開かず、PnP親デバイスの16桁USBシリアル番号を取得する。HELLOの8バイト基板ID（TinyUSBのserial生成順）との一致を必須にする。COM番号は識別情報として保存しない。1200bps touchは実行しない。

rawフレームをCOBS化し、0x00で終端する。raw上限128バイト、payload上限98バイト、COBS部分上限129バイト（終端込み130）。整数はlittle endian。sessionは.NET Guidの`TryWriteBytes`の16バイトを不透明な列として扱い、文字列表現へ変換しない。

| raw offset | size | 内容                                                                         |
| ---------- | ---- | ---------------------------------------------------------------------------- |
| 0          | 1    | version = 1                                                                  |
| 1          | 1    | command                                                                      |
| 2          | 2    | payload長                                                                    |
| 4          | 16   | USB openごとの乱数session                                                    |
| 20         | 4    | sequence                                                                     |
| 24         | 4    | BLE generation                                                               |
| 28         | 可変 | payload                                                                      |
| 末尾       | 2    | 直前までのCRC-16/CCITT-FALSE（poly1021、initFFFF、refin/out false、xorout0） |

250ms以上途切れた部分フレーム、長すぎるフレームは次の終端まで破棄する。不正CRC・形式・session・sequence・世代・未知commandで非ゼロ処理を破棄し、再HELLOを要求する。アプリは不正応答・timeout・書込開始後の取消で実USBハンドルを閉じる。キー入力commandは自動再送しない。

## 要求・応答

| ID  | 要求         | payload / 動作                                                                       |
| --- | ------------ | ------------------------------------------------------------------------------------ |
| 1   | HELLO        | 空。sequence0。以前のARM・未処理通知を破棄し、接続先がReadyならゼロ通知後に応答      |
| 2   | STATUS       | 空。最新状態。世代の更新を取得できる                                                 |
| 3   | ARM          | 空。現行世代、Ready、CapsLockオフまたは未取得を要求。ゼロ通知後に送信を許可          |
| 4   | REPORT       | 8バイト。modifier、予約0、6キー。Usage上限91。Report IDを先頭に足さない              |
| 5   | RELEASE      | 空。全ゼロ。通常のkey-upではARMを維持。未処理非ゼロを中断する場合はARMも失効         |
| 6   | DISARM       | 空。ARM失効・未処理非ゼロ破棄・ゼロ通知。停止と転送終了に使う                        |
| 7   | PING         | 空。STATUSと同じ。アプリは250msごとに送る                                            |
| 8   | PAIRING_OPEN | 空。登録なし・ARMなし・通知待ちなしの場合だけ60秒の登録窓を開く                      |
| 9   | CLEAR_BOND   | 空。登録1件の解除。送信終了とゼロ通知を先に処理。Readyでない場合は送信不能のまま解除 |

HELLOの後はsequence1から全要求（PINGを含む）を単一writerで採番する。重複・逆順・飛びを拒否する。FFFFFFFF到達前に新sessionが必要。ACKは要求command OR 80、同じsession/sequence、現行generation、payload先頭にresultを返す。成功時は後続57バイトの状態を必須にする。失敗時はresult1バイト。未要求の状態イベントはcommand F0、sequence0、payloadに57バイト状態を返す。

result: 0成功、1不正形式、2session不一致、3sequence不一致、4世代不一致、5NotReady、6NotArmed、7Busy、8通知失敗、9拒否。成功ACKはSoftDeviceの通知投入成功を示す。無線到達、相手のキー状態、文字・IME変換・本文一致の確認ではない。
FW 0.1.1のCLEAR_BONDでは、登録ファイルを削除できなければ結果10（StorageFailed）を返す。登録状態を残して手動の再試行を可能にする。

アプリ要求timeoutは1秒、BLE通知の空き待ちは500ms。USB/DTR断または正当な通信の途絶1,500msでARMを失効し解放する。DISARM/中断RELEASEで以前の非ゼロjobを破棄し、ゼロ投入成功後に停止完了ACKを返す。待機中もPINGを処理する。純粋状態機械は単一Arduino loopが所有し、BLEコールバックはハードウェア状態を渡す。BLE通知は非ブロッキングの`sd_ble_gatts_hvx`をこのloopだけから呼ぶ。
アプリの「停止」は新しい入力要求を止め、進行中の要求には通常の応答期限を適用する。応答を受けてからRELEASE・DISARMへ進み、応答途絶時はUSBを閉じる。

## 57バイト状態

| offset | size | 内容                                                                                          |
| ------ | ---- | --------------------------------------------------------------------------------------------- |
| 0      | 4    | ASCII JBI1                                                                                    |
| 4      | 1    | flags: bit0接続、1暗号化、2必要な通知購読、3suspend、4LED取得済み、5CapsLock、6登録済み、7ARM |
| 5      | 1    | Protocol Mode: 0Boot / 1Report                                                                |
| 6      | 8    | USBシリアル番号と一致する基板ID                                                               |
| 14     | 8    | BLE identity address6バイト＋address type1バイト＋0（認証情報ではない）                       |
| 22     | 32   | descriptor SHA-256                                                                            |
| 54     | 3    | firmware major / minor / patch = 0 / 1 / 0                                                    |

Readyはflags低4bitが7。登録済み・暗号化された相手1台だけで暗号化flagを立てる。CapsLockオンは非ゼロ送信とARMを禁止し、解放は許可する。未取得は画面で手動確認を求める。接続・暗号化・購読・suspend・Protocol Mode・peer変化でgenerationを更新し、旧ARMを失効する。

キーボード専用descriptorの正本は`contracts/xiao-protocol-v1/descriptor.hex`。SHA-256は`6E1849E6B93E3C2ED1F8361ADBE2BD63AA307DD24738C894573AC0284DA242B3`。`Test-XiaoContract.ps1`がアプリ・FWと照合する。`vectors.tsv`はPython標準CRCで独立生成し、C#とC++の両方が正常4件・異常6件を読む。再生成はprotocolを意図的に変更するときだけ行う。

## ペアリング・保存・制約

キーボード専用HID 1812、GAP/広告Appearance03C1、名前はscan responseへ格納する。電池測定をしないためBattery Serviceを公開しない。Just Worksの暗号化を使い、MITM認証済みとは表明しない。既存登録との再接続は許可し、未知相手と窓外の再ペアリングは拒否する。Seeed同梱Bluefruitの自動承認を抑えるため、workspace内SDKのSEC_PARAMS_REQUESTに限定したhookを追加する。ビルド時は変更前後の固定ハッシュを照合する。

鍵はSDKのbond storeにだけ保存する。独自保存するのは登録相手のidentity addressだけ。本文・キー列・鍵をログやUSB状態に出さない。設定には方式、基板IDと前回相手IDのSHA-256のみを保存する。HMAC方式の鍵設定・復旧は対象外。

実USB抜去時のドライバー応答、BLEの実到達と接続競合、受信Windowsの分類は自動検証の範囲外。現物での確認事項は`firmware/xiao-nrf52840/README.md`を参照する。
