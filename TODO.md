# 保守項目

## 未完了

- [ ] JBI-XIAO-TOOLING-20261102 — trigger: 次にFWソースを変更するとき; deadline: 2026-11-16; scope: `firmware/xiao-nrf52840/` と `scripts/Test-FirmwareFormat.ps1`。C++の6ファイルをclang-formatで整形し、`-Enforce`へ切り替える。現在は書込済みFWとのsource hash対応を保つ判断により`NOT_CONFORMING`を記録する。行coverageは計測済みだが、書式合格とは扱わない。
- [ ] JBI-XIAO-FOLLOWUP-20261106 — trigger: 利用者が実運用で送信先案内や停止挙動の確認を依頼したとき; deadline: 2026-11-16（確認機会がなければ状況だけ更新）; scope: 送信先未選択時の案内、XIAO-06のARM待ち中の編集・停止時間・残留入力数、補足・任意の実機確認。実施範囲を[VALIDATION.md](VALIDATION.md)に追記する。必須4件の受入は2026-11-06に完了済み。
- [ ] JBI-PUBLIC-SUPPORT-20261107 — trigger: 所有者がGitHub初回公開を承認したとき; deadline: 2026-11-16; scope: `README.md`、`SECURITY.md`、`SUPPORT.md`と公開先設定。実際のGitHub Issuesへリンクし、Private vulnerability reportingを有効にした後、非公開報告ボタンを確認する。外部設定の変更には個別の公開操作の指示が必要。

## 完了

実機受入と過去の検証結果は[VALIDATION.md](VALIDATION.md)に記録する。
