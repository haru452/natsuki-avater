# 翻訳スクリプト（自分の声 → ペットの上の文字）

話した日本語は英語に、英語は日本語に訳して、アバターのペットの上（KAT）に出します。
スマホ（Termux）が声を聞いて訳し、PC の中継スクリプトが VRChat に届けます。

```
スマホ（vrc_translator.py） ──Wi-Fi──▶ PC（pc_osc_relay.py） ──▶ VRChat
            ▲                                   │
            └──── 翻訳トグルのオン・オフ ◀───────┘
```

## はじめに知っておくこと

- **VRChat の音はヘッドホンで聞いてください。** スピーカーだと、スマホのマイクがほかの人の声まで拾い、翻訳・記録してしまいます。
- **話した声は Google（Gemini）に送られます。** 無料枠では、送った内容が Google のサービス改善に使われることがあります。
- 翻訳の記録は、スマホの `~/vrc_translation_log.tsv` に残ります（日時、言語、原文、訳）。音声と API キーは残しません。
- 漢字はペットの上に出せないので、日本語はひらがなとカタカナで出ます（記録には漢字で残ります）。

## 必要なもの

- PC: Windows、VRChat（OSC を有効にする）、Python 3（https://www.python.org/ から入れる。「Add python.exe to PATH」にチェック）
- スマホ: Android、F-Droid から入れた **Termux** と **Termux:API**（Google Play 版は古いので使わない）
- PC とスマホが同じ Wi-Fi
- Gemini の API キー（https://aistudio.google.com/apikey で作る）
- アバターに TranslatorPet（翻訳ペット）が入っていること

## 1. PC の準備

1. VRChat で、アクションメニュー（R キー長押し）→ Options → OSC → **Enabled** にする。
2. `Tools/translator/pc/start_relay.bat` をダブルクリックする。黒い画面が開いたままになれば OK。
3. 初めて起動したとき Windows のファイアウォールの確認が出たら、**プライベート ネットワーク** にチェックして「許可」する。
4. PC で `ipconfig` を実行し、Wi-Fi の「IPv4 アドレス」（例 `192.168.1.10`）を控える。

遊ぶたびに、VRChat の前か後に `start_relay.bat` を起動してください。

## 2. スマホの準備（最初の 1 回だけ）

1. Termux を開いて、次を実行する。
   ```
   pkg update
   pkg install python pulseaudio termux-api
   ```
2. Android の **設定 → アプリ → Termux → 権限 → マイク** を「許可」にする。
3. `Tools/translator` フォルダごとスマホにコピーする（例: スマホの「ダウンロード」に入れてから）。
   ```
   termux-setup-storage
   cp -r ~/storage/downloads/translator ~/translator
   ```
4. `~/translator/phone/settings.ini` の `PC_IP` を、手順 1-4 で控えたアドレスに書き換える。
   ```
   nano ~/translator/phone/settings.ini
   ```

## 3. マイクの確認

```
pulseaudio --start --load="module-sles-source" --exit-idle-time=-1
cd ~/translator/phone
python mic_check.py
```

- 話すと棒が伸び、「● 録音」が出て、黙ると「✔ 1 回分」と出れば OK。Ctrl+C で終わる。
- **画面を消しても動くか** も試す: `python mic_check.py --seconds 30` を実行して画面を消し、何か話してから画面をつける。「✔」が出ていれば、画面を消して使えます。出ていなければ、画面をつけたまま使ってください。
- 棒がまったく動かないときは、手順 2-2 のマイクの許可と、`pulseaudio` の起動を確かめる。
- 小さな物音で「● 録音」が出すぎるときは、`settings.ini` の `FLOOR_MIN` を上げる。声で反応しにくいときは `MARGIN` を下げる。

## 4. 起動

```
termux-wake-lock
export GEMINI_API_KEY="（あなたのキー）"
pulseaudio --start --load="module-sles-source" --exit-idle-time=-1
cd ~/translator/phone
python vrc_translator.py
```

- PC の中継の画面に「スマホを見つけました」と出れば、つながっています。
- アバターの Expression メニューで **翻訳 → 翻訳** をオンにすると、聞き始めます。オフで止まります。
- 終わるときは Termux で Ctrl+C。`termux-wake-unlock` で省電力に戻ります。
- 省電力の設定で Termux を「制限しない」にしておくと、止まりにくくなります。

API キーを毎回打つのが面倒なら、`~/.bashrc` に `export GEMINI_API_KEY="..."` を書いておけます（キーはほかの人に見せないでください）。

## 設定（`settings.ini`）

| 名前 | 意味 |
|---|---|
| `PC_IP` | PC の IP アドレス |
| `SHOW_ORIGINAL` | 原文も出すか（`true` / `false`） |
| `DISPLAY` | `kat`（ペットの上）、`chatbox`（VRChat 標準の吹き出し。漢字も出る）、`both` |
| `KAT_SYNC` | アバター側の KAT の同期設定（通常 8） |
| `MODEL` | Gemini のモデル |
| `[voice]` の値 | 声の区切り方（`mic_check.py` を見ながら調整） |

## うまくいかないとき

| 症状 | 確かめること |
|---|---|
| 中継の画面に「スマホを見つけました」が出ない | `PC_IP` が正しいか。同じ Wi-Fi か。ファイアウォールで Python を許可したか |
| トグルをオンにしても聞き始めない | 中継が起動しているか。VRChat の OSC が Enabled か。アバターを変えたら、トグルを一度オフ→オン |
| ペットの上に何も出ない | 中継の画面に「スマホ → VRChat」の件数が出ているか。`KAT_SYNC` がアバター側と同じか |
| `(error)` が出る | Termux の画面に理由が出ます。「API キーが使えません」「回数の上限です」など |
| 「マイクを開けません」 | `pulseaudio --start ...` を実行したか。マイクの許可 |

## テスト（開発用、PC で実行）

```
python -m unittest discover -s Tools/translator
python -m unittest discover -s Tools/translator/phone
python -m unittest discover -s Tools/translator/pc
```
