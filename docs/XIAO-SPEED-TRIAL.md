# XIAOの日本語送信速度を実機で測る

送信側はJIS BLE Input 1.8.0、XIAO nRF52840 SenseはFW 0.1.0を使います。検証用ZIPを送信Surface上の新しいフォルダーに展開し、旧版をタスクトレイから終了してから `JisBleInput.exe` を起動します。すでに動作している基板はそのまま接続できます。受信側はWindows標準のメモ帳とMicrosoft IMEを使います。

## 0.70秒を基準に測る

1. 送信側で「XIAO（USB接続）」を選択し、いつも使う受信PCを接続します。受信側はMicrosoft IMEのローマ字入力・ひらがな、CapsLockオフにします。
2. 送信側で `samples/xiao-japanese-trial.txt` の全文をコピーして本文欄へ貼り付けます。空行・末尾を含めて貼ります。受信PCでは試行ごとに空のメモ帳の文書を開きます。
3. 「日本語・混在文」「速い（0.70秒/文字）」で送信します。完了後、送信側の「処理時間」を記録します。これはキー列処理開始から末尾の待機までの時間で、USBのARM待ち・一時停止・最後の解放処理は計測外です。キー列送信済みの表示が出たら、受信側に未確定文字が残っていないことを確認し、全文をUTF-8のテキストとして保存します。
4. 下記の方法で比較し、一致したら同じ原文・同じPCで `試験 0.60` → `0.50` → `0.40` → `0.35秒/文字` の順に進めます。途中で停止した試行やIME・フォーカスが変わった試行は測り直します。文字化けや未確定残りが出た速度は記録して、最後に一致した速度へ戻します。0.70秒でも不一致なら、短縮試験を進める前にIME・入力欄・接続状態を確認します。
5. 短文が通った速度のうち速いものから、`samples/AC-08-100.txt` でも基準と比較します。同じ条件で短文・100文字をそれぞれ3回ずつ送り、全試行が一致した最短の設定を記録します。実際の長文で使う場合は、`samples/AC-08-1000.txt` や代表的な自分の文章でも確認します。

| 固定待ち/文字  | 短文の一致回数 | 100文字の一致回数 | 100文字の処理時間（各回） | 備考 |
| -------------- | -------------- | ----------------- | ------------------------- | ---- |
| 0.70秒（基準） | /3             | /3                |                           |      |
| 0.60秒         | /3             | /3                |                           |      |
| 0.50秒         | /3             | /3                |                           |      |
| 0.40秒         | /3             | /3                |                           |      |
| 0.35秒         | /3             | /3                |                           |      |

候補間では同じ文字コード、キー押下時間、4桁コードのキー間隔を使い、IME準備・F5変換・Enter確定後の待ちだけを変えています。改行には従来どおり0.35秒の固定待ちがあります。実際の処理時間にはUSB・BLE・OSの待ちも含まれます。

## 全文を比較する

受信結果を送信側で参照できる場合、展開したZIPのフォルダーでPowerShell 7を開き、次を実行します。`MATCH` が全文一致です。`MISMATCH` の行・列と文字コードは最初の相違を示します。CRLF・LF・CRは同じ改行として比較します。空白、空行、末尾改行、大小文字、全角・半角も比較します。

```powershell
pwsh -NoProfile -File .\samples\Compare-XiaoTrialText.ps1 -ReceivedPath 'C:\path\received.txt'
pwsh -NoProfile -File .\samples\Compare-XiaoTrialText.ps1 -ExpectedPath .\samples\AC-08-100.txt -ReceivedPath 'C:\path\received-100.txt'
```

受信ファイルを持ち出せない場合は、送信側で `-ReceivedPath` を省いて `ExpectedSha256` を表示し、受信側ではWindows標準のPowerShellで次を実行して表示されたSHA-256を照合します。パスはメモ帳でUTF-8保存したファイルに置き換えます。

```powershell
$text = [IO.File]::ReadAllText('C:\path\received.txt') -replace '\r\n?', "`n"
$sha256 = [Security.Cryptography.SHA256]::Create()
try { [BitConverter]::ToString($sha256.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))).Replace('-', '') }
finally { $sha256.Dispose() }
```

再現用に、速度・処理時間・一致結果と、受信PCのWindows/IME設定、入力先アプリを控えます。試験速度は次回起動時に引き継がれません。実用に使う速度は、実際の受信PCと入力先で確認した結果から判断してください。
