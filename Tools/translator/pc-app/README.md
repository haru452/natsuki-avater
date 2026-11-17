# VRC Translator PC（translator_pc.exe）

PC だけで翻訳します（スマホは要りません）。

PC のマイク → 音声認識（SenseVoice、日本語は ReazonSpeech で聞き直す。PC の中）→ DeepL → ペットの上の字幕（KAT）/ チャットボックス

- アバターの「翻訳」がオンのあいだだけマイクを開きます
- 自分は日本語か英語で話す前提です。日本語は英語（または中国語）に、英語は日本語に訳します
- ペットの上の字幕は英語だけです。日本語・中国語の訳はチャットボックスに出ます（字幕に日本語を混ぜると文字が化けるため）

## 使い方

1. DeepL API Free に登録して「認証キー」をコピーする（https://www.deepl.com/pro-api）
2. デスクトップの「VRC Translator PC」をダブルクリック
3. 初回だけ: **K** を押してキーを貼り付け、Enter（キーは `%APPDATA%\VRCTranslatorPC\settings.json` に保存。画面には出ません）
4. VRChat でアバターの「翻訳」をオンにして話す

キー: **K** = DeepL のキー、**L** = 相手の言語（英語 / 中国語）、**R** = 声の保存（オン / オフ）、**Ctrl+C** = 終わる。

声の保存をオンにしているあいだだけ、話した 1 文ごとの音声を `%APPDATA%\VRCTranslatorPC\samples\` に WAV で保存します
（音声認識の聞き比べ用。PC の外には送りません。要らなくなったらフォルダごと消してください）。
聞き比べ: `translator_pc.exe --compare`（保存した WAV を SenseVoice / ReazonSpeech で認識し、結果を `samples\compare.tsv` に書きます）。

画面の「最近のできごと」に 1 文ごとの時間（認識 / DeepL）、上に CPU 使用率が出ます。
記録: `%APPDATA%\VRCTranslatorPC\translation_log.tsv`（音声とキーは残しません）。

## 作り方（開発者向け）

1. 音声認識のモデルを置く: `sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17` の `model.int8.onnx` と `tokens.txt` を
   `models\sense-voice\` に（git には入れません）。
   日本語用（任意・おすすめ）: `sherpa-onnx-zipformer-ja-reazonspeech-2024-08-01` の `*.int8.onnx` 3 つと `tokens.txt` を `models\reazon\` に
2. `powershell -ExecutionPolicy Bypass -File publish.ps1` → `publish\translator_pc.exe` と `publish\models\`、デスクトップのショートカット
3. 確かめる: `publish\translator_pc.exe --test-wav <16 kHz mono の wav>`（認識・かな・DeepL の結果を表示）

テストは `dotnet test`。

使っているもの: .NET 8（MIT）、NAudio 2.2.1（MIT）、sherpa-onnx（Apache 2.0）、SenseVoice モデル（モデルの LICENSE を参照）、
ReazonSpeech の k2 モデル（Apache 2.0）、
LibNMeCab（GPL/LGPL/BSD のうち BSD）と IPA 辞書。

`Tools/translator/android/`（スマホ版）と `Tools/translator/pc/`（Python の中継）は古い版です。
