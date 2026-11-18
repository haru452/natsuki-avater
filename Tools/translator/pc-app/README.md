# VRC Translator PC（translator_pc.exe）

PC だけで翻訳します（スマホは要りません）。

PC のマイク → 音声認識（SenseVoice、日本語は ReazonSpeech で聞き直す。PC の中）→ DeepL → ペットの上の字幕（KAT）/ チャットボックス

- アバターの「ペット」がオンのあいだだけマイクを開きます。「翻訳」をオフにすると、訳さず何も出しません（名前を呼んだときの反応だけします。DeepL にも送らず、記録もしません）
- 自分は日本語か英語で話す前提です。日本語は英語（または中国語）に訳します。英語はそのまま出します（日本語には訳しません）
- 字幕に日本語は出しません。ペットの上は英語だけ、中国語の訳はチャットボックスに出ます

## ほかの人の声をスマホで読む

ほかの人が話した言葉を、**聞き取ったそのまま**（日本語も英語も中国語も）スマホに出します。今は DeepL を通しません（キーがなくても使えます）。

`settings.json` の `TranslateOthers` を `true` にすると、日本語以外を DeepL で日本語に訳して出します（日本語の声は訳さず、DeepL にも送りません）。

1. スマホを PC と同じ Wi-Fi につなぐ
2. スマホのブラウザで、画面の「相手の声」に出ているアドレス（例 `http://192.168.1.10:8765/`）を開く
3. 初めてのとき Windows が「ファイアウォールで許可するか」を聞いてきたら、**プライベート ネットワーク**を許可する

- スマホでページを開いている間だけ VRChat の音を聞きます（閉じたり画面が消えたりして 10 秒たつと止まります）。
- 声は PC の中で文字にします（訳す設定のときだけ、日本語以外の文を DeepL に送ります）。音声も文も保存しません（ページに最新 50 件を出すだけ）。
- 訳す設定のとき、たくさん話す人がいると DeepL の無料分（月 50 万文字）を早く使います。

## 使い方

1. DeepL API Free に登録して「認証キー」をコピーする（https://www.deepl.com/pro-api）
2. デスクトップの「VRC Translator PC」をダブルクリック
3. 初回だけ: **K** を押してキーを貼り付け、Enter（キーは `%APPDATA%\VRCTranslatorPC\settings.json` に保存。画面には出ません）
4. VRChat でアバターの「ペット」をオンにして話す（「翻訳」は最初からオン）

「ペット」がオンのあいだに（「翻訳」がオフでも） **ペットの名前（誉・ほまれ／なつき）を呼ぶと、ペットが振り向いて笑います**（DeepL を待たずにすぐ）。
名前は `settings.json` の `PetNames` で変えられます（ひらがな・カタカナ・漢字の読み・ローマ字のどれでも拾います）。
PC は「ほまれちゃん」の「ほ」をよく落とす（「まれちゃん」「おばれちゃん」など）ので、`PetNameSoundAlikes` の「ちゃん」だけでも名前として扱います（ふだん人をちゃん付けで呼ばないため）。「ちゃんと」は `PetCallIgnore` で除いてあり、反応しません。
アバター側は、翻訳ペットのプレハブを作り直したもの（パラメータ `TranslatorPetCalled`）が要ります。

名前のあとに（同じ文でも、6 秒以内の次の文でも）**日本語で命令**すると、そのとおり動きます（Gemini は使いません。PC の中で聞き分けます）:
- **座る**: 「座って」「座ってみて」「おすわり」「休んで」など。命令するか、あなたが歩いて離れると立ちます
- **待つ**: 「待ってて」「ここで待って」「ストップ」「そこにいて」など。どれだけ離れてもその場で待ち、20 m 離れると戻ってきます
- **来る**（待つ・座る・後ろ向きをやめる）: 「おいで」「こっち来て」「ついてきて」「もういいよ」など
- **立つ**: 「立って」「起きて」など
- **後ろを向く**: 「後ろ向いて」「あっち向いて」「背中向けて」など。歩き出すときは自分で前を向きます
- **前を向く**: 「前向いて」「こっち向いて」「振り向いて」など
言い方は `settings.json` の `PetCommandWords` で足したり消したりできます（ひらがなで書く）。
確かめ方: `translator_pc.exe --test-command "誉、座って" "誉、待っててね"`（読みと、名前・命令が聞き取れたかを表示）

ペットの上の字幕は、出しきってから **10 秒** で消えます（`settings.json` の `KatHoldSeconds` で秒数を変えられます。0 にすると次の文まで出たまま）。

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
