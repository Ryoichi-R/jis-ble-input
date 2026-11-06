# ライセンスと第三者コンポーネント

本プロジェクトの独自コード・独自アイコンは、ルートの[MITライセンス](LICENSE)で提供します。第三者のコード・素材・SDK・ランタイムは各許諾条件に従います。ルートのMITライセンスで第三者の条件を置き換えません。

| コンポーネント                                      | 用途                                                      | 許諾文と出所                                                                                                                                             |
| --------------------------------------------------- | --------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| BleHid v0.6.0のHID descriptor                       | キーボード定義を基にマウス部分を除外しJIS/IME usageを拡張 | MIT。`licenses/windows-ble-hid-LICENSE.txt`と[上流ソース](https://github.com/abhishek-raj/windows-ble-hid/blob/v0.6.0/src/BleHid.Core/HidDescriptors.cs) |
| .NET / Windows Desktop runtime / ARM64 host 10.0.14 | Windowsアプリに同梱                                       | Microsoft公式パックのMIT許諾文と各第三者notice。配布物の`licenses/`へ収集                                                                                |
| System.IO.Ports 10.0.14                             | USBシリアル通信                                           | パッケージのMIT宣言、同版.NETの許諾文、第三者noticeと出所を配布物の`licenses/system-io-ports/`へ収集                                                     |
| Seeed nRF52 SDK 1.1.13                              | Sense用FWのコアとライブラリ                               | LGPL-2.1-or-later、MIT、Nordic独自条件などが混在。採用SDK原本と許諾文を配布物の`firmware/rebuild/`、`firmware/licenses/sdk/`に保持                       |

SDK内のBluefruit、TinyUSB、FreeRTOS、LittleFS、Nordic nrfx / SoftDevice API等は、それぞれの原文を参照してください。Nordicの条件を含むため、FWとSDKを他社IC向けに無条件で利用できるものではありません。配布imageはアプリ領域だけで、基板のSoftDeviceやbootloaderを書き換えません。

FWの再ビルド資料には、使用したSDK原本、変更を再適用するビルドスクリプト、独自FWソース、ELF、リンク可能なobject/archiveを含めます。詳細は[FW手順](firmware/xiao-nrf52840/README.md)を参照してください。ツールのバイナリは配布せず、固定版を公式配布元から取得します。SDK原文の許諾・著作権表示を変更しません。

`licenses/windows-desktop-runtime/sources.json`はソース同梱noticeの取得元とハッシュを記録します。配布時は実際のNuGetパックからも許諾文を収集し、版とハッシュを照合します。依存関係の構成はlockfile、FWの固定版は`firmware/xiao-nrf52840/toolchain.json`とビルドスクリプトで確認できます。

配布物のsbom.cdx.jsonはCycloneDX 1.6形式の構成一覧です。SDK内の全ファイルを個別分類した一覧ではなく、SDK原本をまとめて識別する範囲を明示しています。manifest.jsonは同梱ファイルのSHA-256一覧です。
