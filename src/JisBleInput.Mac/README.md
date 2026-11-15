# JIS BLE Input for Mac 0.20.0

Macの本文をBluetoothキーボードとしてWindowsに送ります。**英数字・URL** モードで半角英数字、記号、改行を最大5,000文字まで送れます。改行はCRLFも1文字として数えます。

## 使い方

1. MacでJIS BLE Inputを開き、Windowsで `JIS-HID` に接続します。
2. Macの入力方法を「英数字・URL」にし、本文を入力します。受信側のキーボード配列は「設定…」でJISまたはUSを選択します。
3. Windowsで入力欄をクリックし、入力表示を「A」、CapsLockをオフにします。改行を送る場合は、メモ帳などの複数行の入力欄を選びます。
4. Macで「送信」を押し、Windowsの入力結果を確認します。送信中はMacから一時停止、続行、停止ができます。

「日本語（利用不可）」はグレー表示です。以前の版で日本語を選んでいた場合は、入力方法で「英数字・URL」を選び直してください。

## 定型文

「定型文…」で現在の本文を名前付きで保存できます。保存した定型文は本文に挿入でき、「本文で上書き…」で更新できます。

## 接続と速度

CapsLockが「不明」なら、Windowsで一度オンにしてからオフに戻します。キー解放の再試行やBluetoothの広告の再開はMacの「設定…」にあります。

速度は「ゆっくり（0.12秒）」「高速（0.08秒）」「最速（0.05秒）」です。数値はキー入力の待ち時間で、Bluetooth通信の時間が加わります。

## ビルドと検証

macOSで、このディレクトリから `zsh build.sh` を実行すると `JIS-BLE-Input.app` が生成されます。 `zsh package.sh` で `dist/` にZIPとSHA-256を生成します。

`zsh checks/run-safety.sh` は送信制御を、 `zsh checks/run-key-parity.sh` はWindows版とのASCIIキー列の一致を検証します。後者にはリポジトリの `global.json` が指定する.NET SDKが必要です。画面検証は `zsh build.sh --check-ui` の後、 `checks/.build/JIS-BLE-Input.app/Contents/MacOS/JisBleInput` を実行してください。 [検証記録](VALIDATION.md)
