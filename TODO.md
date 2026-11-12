# 保守項目

## 未完了

- [ ] JBI-XIAO-SPEED-20261112 — trigger: 実機で入力の一致を測れる機会に、XIAO方式の速度を上げたいとき; deadline: 2026-12-12（測定機会がなければ状況だけ更新）; scope: firmware/xiao-nrf52840/xiao-nrf52840.ino の接続間隔と src/JisBleInput.Core/ImeKeyEncoder.cs のキー手順。XIAOの日本語3段階は表示上約240・205・175 ms/文字で差が小さい。1文字16回のキー状態送信に対し、タイマー粒度とBLE接続間隔（11.25〜15ms）が下限になっているとみられる（推測、実機未測定）。候補は接続間隔の7.5msへの短縮と、文字ごとのIME再設定を省く速度項目の追加。どちらも受信側の入力一致を実機で確認してから採用する。[検証記録](VALIDATION.md)。

- [ ] JBI-XIAO-ACCEPT-20261112 — trigger: アプリ1.15.0・FW 0.1.2を実機で使い始めるとき; deadline: 2026-11-30（確認機会がなければ状況だけ更新）; scope: src/JisBleInput.App/MainWindow.cs、src/JisBleInput.Core/StagedSender.cs、firmware/xiao-nrf52840/。USB抜き差し・スリープ復帰後の自動再接続、接続の一時断後の送信先復帰、画面操作中の送信でキーリピートが出ないこと、FW 0.1.2の書き込みと登録・再起動後の登録維持を実機で確認する。FW側の長押し自動解放は未実装。[検証記録](VALIDATION.md)。

- [ ] JBI-XIAO-CLEAR-BOND-20261107 — trigger: FW 0.1.1の実機受入を行うとき; deadline: 2026-11-16（実機の確認機会がなければ状況を見直す）; scope: firmware/xiao-nrf52840/ の登録解除と永続化。2026-11-10に削除失敗のUSB応答とアプリ表示を実装し、模擬通信で失敗・再試行を確認した。実機の再試行と再起動後の登録状態を確認する。[検証記録](VALIDATION.md)。

- [ ] JBI-XIAO-FOLLOWUP-20261106 — trigger: 利用者が実運用で送信先案内や停止挙動の確認を依頼したとき; deadline: 2026-11-16（確認機会がなければ状況だけ更新）; scope: firmware/xiao-nrf52840/ と src/。1.13.1で送信先未選択時の案内・ARM待ち中の編集・応答後の停止を模擬通信で確認した。XIAO-06の停止時間と残留入力数は実機で確認する。[検証記録](VALIDATION.md)。

## 完了

- [x] JBI-XIAO-TOOLING-20261102 — trigger: 次にFWソースを変更するとき; deadline: 2026-11-16; scope: firmware/xiao-nrf52840/ と scripts/Test-FirmwareFormat.ps1。✅ 2026-11-10 clang-format 23.1.2で6ファイルを整形し、書式検査を必須化。FWホスト341件、行カバレッジ98.34%。[検証記録](VALIDATION.md)。

- [x] JBI-PUBLIC-SUPPORT-20261107 — trigger: 所有者がGitHub初回公開を承認したとき; deadline: 2026-11-16; scope: `README.md`、`SECURITY.md`、`SUPPORT.md`と公開先設定。✅ 2026-11-07 v1.7.1の公開と窓口開設を確認。README・SUPPORT・SECURITYのリンク、[GitHub Issues](https://github.com/Ryoichi-R/jis-ble-input/issues)の有効状態、[非公開脆弱性報告](https://github.com/Ryoichi-R/jis-ble-input/security/advisories/new)の有効状態をアーカイブ前に再照合した。[Release v1.7.1](https://github.com/Ryoichi-R/jis-ble-input/releases/tag/v1.7.1)。

実機受入と過去の検証結果は[VALIDATION.md](VALIDATION.md)に記録する。
