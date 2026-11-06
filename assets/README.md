# アプリアイコン

2026-11-01、4案の比較後に利用者がA「キーボード＋無線」を選択した。

- `app.ico`: 選択したA案。EXEのアイコンとmanaged resourceに埋め込む。16/20/24/32/48/64/128/256pxを収録。
- `app.svg` / `app.png`: 選択デザインのベクター原稿と256pxプレビュー。
- `icon-options/`: A（キーボードと無線）、B（日本語と送信）、C（PC接続）、D（文章送信）の候補原稿・ICO・PNG。

候補は`python jis-ble-input/scripts/create_icon_options.py --preview result/icon-options.png`で再生成できる。再生成はWindowsのYu GothicフォントとPillowを使用する開発用操作で、アプリの実行やビルドにPython/Pillowは不要。候補の再生成だけで選択済み`app.*`を上書きしない。

アイコンは新規作成した図形であり、外部の画像素材を取り込んでいない。B案の「あ」はWindowsフォントを描画したもので、フォントファイルは同梱しない。
