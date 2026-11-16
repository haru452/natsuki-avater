# スマホの翻訳スクリプト A Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** スマホ（Termux）で自分の声を聞き、Gemini で訳して、PC の中継スクリプト経由でペットの上の KAT に出す。

**Architecture:** 部品ごとに小さなモジュールに分け、マイク、Gemini、ソケット、時計は外から差し込めるようにして、PC の unittest で全部確かめる。実機でしか分からないマイクの可否は `mic_check.py` で利用者に確かめてもらう。

**Tech Stack:** Python 3（標準ライブラリのみ）、Termux + PulseAudio（`parec`）、Gemini API（REST）、OSC over UDP

**Spec:** `docs/superpowers/specs/2026-11-16-phone-translator-a-design.md`

## Global Constraints

- Python 標準ライブラリのみ。pip を使わない
- API キーは環境変数 `GEMINI_API_KEY` だけ。URL、ログ、画面に出さない
- どのエラーでもプログラムを止めない（起動時の設定の誤りと、キーがないときを除く）
- 1ファイル 600 行以内
- テストは PC で `python -m unittest discover -s Tools/translator -p "test_*.py"`（各フォルダ）で通る。実機、ネットワーク、マイクに触らない
- コミットはこの作業のファイルだけを `git add` する

## Review Focus

1. 翻訳中や録音中にトグルをオフにする → 表示しない、録音は捨てる（Task 7）
2. Wi-Fi が切れて送信が失敗する → 例外で止まらず続く（Task 5、Task 7）
3. Gemini が壊れた JSON、`none`、429、タイムアウトを返す → 止まらず、表示は `(error)` か何もしない（Task 4）
4. 中継スクリプトに、スマホより前に VRChat の値が届く／アバターを変える → 古い値でスマホが聞き始めない（Task 6）
5. 128 文字を超える、かなにできない文字が混ざる → ページに分かれ、`?` で止まらない（Task 5）

---

### Task 1: OSC の読み取りと、消去なしの書き込み（`kat_osc.py` の拡張）

**Files:** Modify `Tools/translator/kat_osc.py`; Test `Tools/translator/test_kat_osc.py`

**Interfaces — Produces:**
- `parse_message(data: bytes) -> tuple[str, list]` … `,i` `,f` `,s` `,T` `,F` を読む。壊れていたら `ValueError`
- `build_messages(text, sync=8, clear=True)` … `clear=False` で先頭の消去グループを付けない
- `SYNC_CHOICES = (4, 8, 16)`（プレハブと合わせる）

- [ ] テスト: `osc_message` で作ったものを `parse_message` で読み戻せる（bool、int、float、str）。短すぎる・型タグなしで `ValueError`。`clear=False` で先頭が Pointer=255 でない。`sync=2` で `ValueError`
- [ ] 失敗を確かめる → 実装 → 通す → Commit

### Task 2: マイクの確認道具と、声の区切り（`phone/voice.py`、`phone/mic_check.py`）

**Interfaces — Produces（`voice.py`）:**
- `FRAME_MS = 30`、`RATE = 16000`
- `rms(frame: bytes) -> float`（16 ビット符号付き、リトルエンディアン）
- `class Segmenter(start_s=0.15, end_silence_s=0.8, preroll_s=0.3, max_s=60, min_s=0.5, margin=3.0, floor_min=200.0)`
  - `feed(frame: bytes) -> bytes | None` … 1 回分の声が終わったら PCM を返す
  - `reset()`、`recording: bool`、`noise_floor: float`
- `to_wav(pcm: bytes) -> bytes`
- `class Microphone(command=PAREC_CMD)` … `frames()` で 30 ms ごとの bytes を出すジェネレーター、`close()`

`mic_check.py`: `parec` を開いて、音の大きさを棒で表示し、雑音の大きさとしきい値も出す。`--seconds N` で終わる。

- [ ] テスト（`test_voice.py`）: 無音 1 秒 → 声 1 秒 → 無音 1 秒で 1 回分が返り、長さが約 1.3 秒（さかのぼり 0.3 秒を含む）。0.3 秒の声は捨てる。61 秒の声は 60 秒で区切られる。雑音が大きくなるとしきい値も上がる。`to_wav` の中身を `wave` で読み戻せる
- [ ] 失敗 → 実装 → 通す → Commit

### Task 3: 設定と記録（`phone/settings.py`、`phone/settings.ini`、`phone/translog.py`）

**Interfaces — Produces:**
- `load_settings(path) -> Settings`（dataclass: `pc_ip, relay_port, listen_port, model, show_original, display, kat_sync, log_file` と声の区切りの値）。`display` が `kat/chatbox/both` 以外、`kat_sync` が 4/8/16 以外なら `ValueError`
- `append_log(path, when: datetime, lang, original, translation, engine)` … タブと改行を空白に

- [ ] テスト: 初期値の読み込み、誤った値で `ValueError`、`~` の展開。記録にタブや改行が入っても 1 行
- [ ] 失敗 → 実装 → 通す → Commit

### Task 4: Gemini（`phone/gemini.py`）

**Interfaces — Produces:**
- `Result(lang, original, translation, kana)`（dataclass）
- `build_request(wav: bytes, model: str, api_key: str) -> urllib.request.Request`
- `parse_response(body: bytes) -> Result` … 壊れていたら `GeminiError`
- `translate(wav, model, api_key, opener=urllib.request.urlopen, sleep=time.sleep) -> Result`
  - タイムアウト 20 秒。ネットワークの失敗と 5xx は 1 回やり直す。429 は 2、4、8 秒待って最大 3 回。だめなら `GeminiError`

- [ ] テスト: キーがヘッダーにあり URL にない。音声が Base64 の `audio/wav`。`responseSchema` に 4 項目。`none` を読める。壊れた JSON で `GeminiError`。429 が 2 回のあと成功（待ち時間 2、4）。500 が続くと 1 回やり直して `GeminiError`
- [ ] 失敗 → 実装 → 通す → Commit

### Task 5: 表示（`phone/display.py`）

**Interfaces — Produces:**
- `compose_kat(result, show_original) -> str` … 設計書の表のとおり。原文は次の行の頭から（32 文字単位で空白を埋める）
- `paginate(text, limit=128) -> list[str]` … `kat_osc.encode_text` の数え方で分ける（濁点で 2 マスになる字を考える）
- `compose_chatbox(result, show_original) -> str` … 漢字まじり。144 文字まで
- `class Display(send, mode, sync, clock=time.monotonic, page_seconds=5, resend_seconds=3)`
  - `send(packet: bytes)` は失敗しても例外を外に出さない呼び出し側の関数
  - `thinking()`、`show(result)`、`error()`、`stop()`、`tick()`（メインのループから定期的に呼ぶ。ページ送りと送り直し）

- [ ] テスト: 日本語→英語と英語→日本語の並び、原文なし。130 文字が 2 ページ。`tick` で 5 秒後に 2 ページ目、最後のページは残り、3 秒ごとに消去なしで送り直す。`error` は 3 秒後に消える。チャットボックスの 144 文字。`send` が例外を出しても `Display` は止まらない
- [ ] 失敗 → 実装 → 通す → Commit

### Task 6: PC の中継（`pc/pc_osc_relay.py`、`pc/start_relay.bat`）

**Interfaces — Produces:**
- `class Relay(vrchat=("127.0.0.1", 9000), phone_port=9001)`
  - `from_phone(data, addr) -> list[tuple[bytes, tuple]]` … hello ならスマホを覚え、覚えている値を返す。ほかは VRChat へ
  - `from_vrchat(data) -> list[tuple[bytes, tuple]]` … 翻訳用の値と `/avatar/change` だけスマホへ。`/avatar/change` で覚えた値を消す
- `main()` … 9002 と 9001 を開き、転送と 1 行の表示。9001 が使えなければ説明を出して終わる

- [ ] テスト: hello の前は VRChat の値を転送しない（覚えるだけ）。hello で覚えた値が届く。`/avatar/change` のあと hello しても古い値は来ない。顔の値などは捨てる。スマホからの KAT は VRChat へ。壊れたパケットで止まらない
- [ ] 失敗 → 実装 → 通す → Commit

### Task 7: 本体（`phone/vrc_translator.py`）と README

**Interfaces — Consumes:** Task 1〜6 すべて
**Produces:** `class App(settings, mic_factory, translate, display, now=datetime.now)`
- `on_osc(address, args)` … `TranslatorActive` でマイクの開閉、`/avatar/change` でオフ扱い
- `run()` … マイクのスレッド、翻訳のスレッド（1 件ずつ）、受信のスレッド、hello を 5 秒ごと、`display.tick()`
- `main()` … キーがなければ説明を出して終わる

- [ ] テスト（偽物の部品）: オンで声を入れると thinking → show → 記録 1 行。オフのまま声を入れても何もしない。翻訳中にオフにすると表示しないが記録は残る。`/avatar/change` で聞くのをやめる。翻訳の例外で `error` が出て次の声も処理される
- [ ] README: Termux の準備、`pulseaudio` の起動、`mic_check.py`（画面を消しても試す）、`termux-wake-lock`、PC の中継の起動とファイアウォール、ヘッドホンの前提、Google に声が送られることの注意
- [ ] 失敗 → 実装 → 通す → Commit

### Task 8: 実機での確認（利用者）

- [ ] `mic_check.py` で、画面をつけた状態と消した状態で棒が伸びるか
- [ ] 設計書の受け入れ基準を上から確かめる。結果を設計書に追記して Commit
