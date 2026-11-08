# 保守項目

## 未完了

- [ ] JBI-XIAO-CLEAR-BOND-20261107 — trigger: XIAOファームウェアを次に変更するとき; deadline: 2026-11-16（FW変更予定がなければ状況だけ見直す）; scope: `firmware/xiao-nrf52840/` の登録解除と永続化。保存領域で解除に失敗した場合はUSB応答に反映し、再試行と再起動後の登録状態を検証する。[通信仕様](docs/XIAO-PROTOCOL.md)。

- [ ] JBI-XIAO-TOOLING-20261102 — trigger: 次にFWソースを変更するとき; deadline: 2026-11-16; scope: `firmware/xiao-nrf52840/` と `scripts/Test-FirmwareFormat.ps1`。C++の6ファイルをclang-formatで整形し、`-Enforce`へ切り替える。現在は書込済みFWとのsource hash対応を保つ判断により`NOT_CONFORMING`を記録する。行coverageは計測済みだが、書式合格とは扱わない。
- [ ] JBI-XIAO-FOLLOWUP-20261106 — trigger: 利用者が実運用で送信先案内や停止挙動の確認を依頼したとき; deadline: 2026-11-16（確認機会がなければ状況だけ更新）; scope: 送信先未選択時の案内、XIAO-06のARM待ち中の編集・停止時間・残留入力数、補足・任意の実機確認。実施範囲を[VALIDATION.md](VALIDATION.md)に追記する。必須4件の受入は2026-11-06に完了済み。

## 完了

- [x] JBI-PUBLIC-SUPPORT-20261107 — trigger: 所有者がGitHub初回公開を承認したとき; deadline: 2026-11-16; scope: `README.md`、`SECURITY.md`、`SUPPORT.md`と公開先設定。✅ 2026-11-07 v1.7.1の公開と窓口開設を確認。README・SUPPORT・SECURITYのリンク、[GitHub Issues](https://github.com/Ryoichi-R/jis-ble-input/issues)の有効状態、[非公開脆弱性報告](https://github.com/Ryoichi-R/jis-ble-input/security/advisories/new)の有効状態をアーカイブ前に再照合した。[Release v1.7.1](https://github.com/Ryoichi-R/jis-ble-input/releases/tag/v1.7.1)。

実機受入と過去の検証結果は[VALIDATION.md](VALIDATION.md)に記録する。
