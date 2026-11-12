# XIAOの送信速度を実機で測る

送信側はJIS BLE Input 1.15.0、XIAO nRF52840 SenseはFW 0.1.0・0.1.1・0.1.2のいずれかを使います。アプリのZIPを送信Surface上の新しいフォルダーに展開し、旧版をタスクトレイから終了してから `JisBleInput.exe` を起動します。すでに動作している基板はそのまま接続できます。受信側はWindows標準のメモ帳とMicrosoft IMEを使います。

## 日本語・混在文を測る

1. 送信側で「XIAO（USB接続）」を選択し、いつも使う受信PCを接続します。受信側はMicrosoft IMEのローマ字入力・ひらがな、CapsLockオフにします。
2. 送信側で `samples/xiao-japanese-trial.txt` の全文をコピーして本文欄へ貼り付けます。空行・末尾を含めて貼ります。受信PCでは試行ごとに空のメモ帳の文書を開きます。
3. 「日本語・混在文」「遅め（約240 ms/文字）」で送信します。完了後、送信側の「処理時間」を記録します。これはキー列処理開始から末尾の待機までの時間で、USBのARM待ち・一時停止・最後の解放処理は計測外です。キー列送信済みの表示が出たら、受信側に未確定文字が残っていないことを確認し、全文をUTF-8のテキストとして保存します。
4. 下記の方法で比較し、一致したら同じ原文・同じPCで「標準（約205 ms/文字）」「最速（約175 ms/文字）」の順に進めます。途中で停止した試行やIME・フォーカスが変わった試行は測り直します。文字化けや未確定残りが出た速度は記録して、最後に一致した速度へ戻します。遅めでも不一致なら、速度を上げる前にIME・入力欄・接続状態を確認します。
5. 短文が通った速度のうち速いものから、`samples/AC-08-100.txt` でも基準と比較します。同じ条件で短文・100文字をそれぞれ3回ずつ送り、全試行が一致した最短の設定を記録します。実際の長文で使う場合は、`samples/AC-08-1000.txt` や代表的な自分の文章でも確認します。

| 固定待ち/文字  | 短文の一致回数 | 100文字の一致回数 | 100文字の処理時間（各回） | 備考 |
| -------------- | -------------- | ----------------- | ------------------------- | ---- |
| 遅め 約240 ms         | /3             | /3                |                           |      |
| 標準 約205 ms         | /3             | /3                |                           |      |
| 最速 約175 ms        | /3             | /3                |                           |      |

各速度でキーの順序は共通です。F5解放後の変換待ちは遅め・標準で15ms、最速で6msです。改行には0.35秒の固定待ちがあります。実際の処理時間にはUSB・BLE・OSの待ちも含まれます。

## 英数字・URLを測る

受信PCの空のメモ帳で、IMEを英数（A）にしてCaps Lockをオフにします。送信側で「英数字・URL」と受信PCのJISまたはUS配列を選び、`samples/ascii-url.txt` の全文を使います。「遅め（約65 ms/文字）」で一致を確認し、同じ原文を別の空文書へ「標準（約30 ms/文字）」「最速（約25 ms/文字）」の順に送ります。記号を広く確認するには `samples/ascii-printable.txt` も比較します。

## 全文を比較する

受信結果を送信側で参照できる場合、展開したZIPのフォルダーでPowerShell 7を開き、次を実行します。`MATCH` が全文一致です。`MISMATCH` の行・列と文字コードは最初の相違を示します。CRLF・LF・CRは同じ改行として比較します。空白、空行、末尾改行、大小文字、全角・半角も比較します。

```powershell
pwsh -NoProfile -File .\samples\Compare-XiaoTrialText.ps1 -ReceivedPath 'C:\path\received.txt'
pwsh -NoProfile -File .\samples\Compare-XiaoTrialText.ps1 -ExpectedPath .\samples\AC-08-100.txt -ReceivedPath 'C:\path\received-100.txt'
pwsh -NoProfile -File .\samples\Compare-XiaoTrialText.ps1 -ExpectedPath .\samples\ascii-url.txt -ReceivedPath 'C:\path\received-ascii.txt'
```

受信ファイルを持ち出せない場合は、送信側で `-ReceivedPath` を省いて `ExpectedSha256` を表示し、受信側ではWindows標準のPowerShellで次を実行して表示されたSHA-256を照合します。パスはメモ帳でUTF-8保存したファイルに置き換えます。

```powershell
$text = [IO.File]::ReadAllText('C:\path\received.txt') -replace '\r\n?', "`n"
$sha256 = [Security.Cryptography.SHA256]::Create()
try { [BitConverter]::ToString($sha256.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))).Replace('-', '') }
finally { $sha256.Dispose() }
```

再現用に、速度・処理時間・一致結果と、受信PCのWindows/IME設定、入力先アプリを控えます。選んだ速度は入力方法ごとに次回起動時へ引き継ぎます。
