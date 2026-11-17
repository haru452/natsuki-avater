# 翻訳アプリ 計画 1：確かめる作業と、自分の声（ローカル AI）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** AQUOS R9 の Unity アプリで、自分の声をスマホの中だけで訳し（SenseVoice + Gemma 1B）、今の Python の中継を通して KAT / チャットボックスに出す。

**Architecture:** Unity に依存しない処理（OSC、KAT の文字、声の区切り、表示、指示文、流れの管理）は `Translator.Core`（`noEngineReferences`）に置き、Python 版のテストを移して EditMode テストで確かめる。マイク、SenseVoice、LLMUnity、ソケット、画面は `Translator.App` の薄いつなぎにする。最初の作業で、R9 で SenseVoice と Gemma の中国語を確かめ、設計書の代わりの方法に切り替えるかを決める。

**Tech Stack:** Unity 2022.3.62f3（Android、IL2CPP、arm64）、LLMUnity 3.0.3（`https://github.com/undreamai/LLMUnity.git#v3.0.3`）、sherpa-onnx（C API を P/Invoke。リリース版の Android arm64 の `.so` と、公式の C# ラッパー `scripts/dotnet/*.cs`）、SenseVoice `sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17`（int8）、Gemma 3 1B / 4B（Q4_K_M、`ggml-org` の GGUF）、NMeCab + IPADIC、NUnit（Unity Test Framework）

**Spec:** `docs/superpowers/specs/2026-11-17-translator-app-design.md`（Termux 版の `2026-11-16-phone-translator-a-design.md` の表示と OSC の決まりも引き継ぐ）

この計画の範囲外（後の計画）: PC の C# `.exe`、相手の声、Gemini、API キーの欄、モデルのダウンロード、記録の共有、ライセンス画面。この計画の間は、PC 側は今の `Tools/translator/pc/pc_osc_relay.py` を使う。

## Global Constraints

- 置き場所: Unity プロジェクトは `Tools/translator/app/`。`Library/`、`Temp/`、`Logs/`、`UserSettings/`、`Build/`、`*.gguf`、`*.onnx`、`*.apk` は git に入れない
- 1 ファイル 600 行以内（利用者のルール）
- `Translator.Core` は UnityEngine を参照しない（`"noEngineReferences": true`）
- ローカル AI のとき、声も文もインターネットへ出さない
- PC の中継との約束: スマホ → `PC_IP:9002`、PC → スマホ `9001`。hello は `/translator/hello`、引数 `(int session, bool othersOn)`、5 秒ごと。`session` は起動ごとに `1..2^31-1` の乱数
- KAT: 32 文字 × 4 行（128）、同期設定 8、ポインタ 255 で消去。チャットボックスは 144 文字
- 時間の値: ページ 5 秒、送り直し 3 秒、1 段 0.25 秒、`(error)` 3 秒、20 秒以上待った声は捨てる、1 件 15 秒を超えたら「遅くなっています」
- 声の区切り: 始まり 0.15 秒、終わりの無音 0.8 秒、さかのぼり 0.3 秒、最大 60 秒、最短 0.5 秒、`margin` 3.0、`floor_min` 200、フレーム 30 ミリ秒（16 kHz で 480 サンプル）
- 「ローカルAI」の値がまだ届いていないときは、ローカル AI として扱う
- 使っている間は画面をつけたまま（`Screen.sleepTimeout = SleepTimeout.NeverSleep`）
- 部品とモデルは無料での配布・利用が許されるもの

## Review Focus

1. **アプリが裏に回る・画面が消える**: Unity が止まって戻ったとき、マイクと翻訳が固まらず、聞き直す → Task 9 のテスト `ResumeAfterPauseRestartsMic`（`OwnVoicePipeline.OnPause/OnResume`）
2. **Gemma が訳以外を返す**（「もちろん、」、引用符、ローマ字の括弧、`>`、2 行目の説明、空）→ Task 6 の `Clean` のテスト
3. **かなに直せない文字**（漢字の固有名詞、絵文字、中国語の漢字）→ KAT に出せない文字は落とし、空なら原文の行を出さない → Task 5 / Task 4 のテスト
4. **PC の中継がない・IP が空**: 送信の失敗で止まらず、画面に「中継: 見つからない」→ Task 8 のテスト `SendFailureDoesNotThrow`、`EmptyPcIpSendsNothing`
5. **SenseVoice が言語を決められない・韓国語など対象外**: 何も出さず、`...` も消す → Task 7 のテスト `UnsupportedLanguageClearsDots`

---

## ファイル構成

```
Tools/translator/app/
├ .gitignore
├ Packages/manifest.json                          … LLMUnity を追加
├ Assets/Translator/
│  ├ Core/Translator.Core.asmdef                  … noEngineReferences
│  ├ Core/KatOsc.cs                               … kat_osc.py の移植（文字表、OSC の組み立てと読み取り）
│  ├ Core/Segmenter.cs                            … voice.py の Segmenter の移植
│  ├ Core/Lang.cs                                 … Lang、RecognizedSpeech、TranslationResult
│  ├ Core/Compose.cs                              … 表示文の組み立て、ページ分け
│  ├ Core/KatDisplay.cs                           … display.py の Display の移植 + 中国語の振り分け
│  ├ Core/KanaConverter.cs                        … IKanaConverter、MeCabKana
│  ├ Core/TranslationPrompt.cs                    … Gemma の指示文と後片付け
│  ├ Core/OwnVoicePipeline.cs                     … vrc_translator.py の App の移植（自分の声）
│  ├ Core/TranslatorSettings.cs                   … 設定の値と初期値
│  ├ Core/TranslationLog.cs                       … translog.py の移植
│  ├ Core/RelayProtocol.cs                        … hello、受け取る値の読み取り
│  ├ Core/RelayLink.cs                            … UDP の送受信と hello（UnityEngine を使わない）
│  ├ Tests/Translator.Core.Tests.asmdef           … EditMode
│  ├ Tests/*Tests.cs                              … 上の各ファイルのテスト
│  ├ App/Translator.App.asmdef
│  ├ App/AndroidMic.cs                            … Unity の Microphone → 480 サンプルずつ
│  ├ App/SherpaSenseVoice.cs                      … ISpeechRecognizer の実装
│  ├ App/LlmUnityTranslator.cs                    … ITextTranslator の実装（1B / 4B）
│  ├ App/SettingsStore.cs                         … PlayerPrefs に JSON で保存
│  ├ App/TranslatorMain.cs                        … 全部をつなぐ MonoBehaviour
│  ├ App/MainScreen.cs                            … 画面（状態、相手の言語、設定）
│  ├ Feasibility/FeasibilityCheck.cs              … 確かめる作業の画面（Task 1。あとで消す）
│  └ Plugins/                                     … sherpa-onnx の .so と C# ラッパー、NMeCab
├ Assets/Fonts/                                   … Noto Sans JP / SC（OFL）
└ Assets/Editor/BuildApk.cs                       … バッチでの APK ビルド
```

テストの実行（Unity を閉じた状態で）:

```bash
"/c/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe" -batchmode -projectPath Tools/translator/app -runTests -testPlatform EditMode -testResults "$SCRATCH/results.xml" -logFile "$SCRATCH/test.log"
```
合格: 終了コード 0、`results.xml` の `failed="0"`。

APK のビルド: `Unity.exe -batchmode -quit -projectPath Tools/translator/app -executeMethod BuildApk.Build -logFile -`（出力 `Tools/translator/app/Build/translator.apk`）。インストール: Unity 付属の `adb`（`.../AndroidPlayer/SDK/platform-tools/adb.exe`）で `install -r`。

---

### Task 1: プロジェクトを作り、R9 で SenseVoice と Gemma の中国語を確かめる

**Files:**
- Create: `Tools/translator/app/`（`Unity.exe -batchmode -quit -createProject`）、`.gitignore`、`Packages/manifest.json` に LLMUnity
- Create: `Assets/Translator/Plugins/sherpa-onnx/`（`libsherpa-onnx-c-api.so`、`libonnxruntime.so` を `Plugins/Android/libs/arm64-v8a` 扱いに設定、C# ラッパー）
- Create: `Assets/Translator/Feasibility/FeasibilityCheck.cs`、`Assets/Editor/BuildApk.cs`
- Modify: `docs/superpowers/specs/2026-11-17-translator-app-design.md`（結果を追記）

**Interfaces:**
- Produces: 動くプロジェクトの雛形、`BuildApk.Build()`、モデルの置き場所 `Application.persistentDataPath + "/models/"`（`sense-voice/model.int8.onnx`、`sense-voice/tokens.txt`、`gemma-3-1b-it-Q4_K_M.gguf`、`gemma-3-4b-it-Q4_K_M.gguf`）。モデルは開発中 `adb push` で入れる
- プレイヤー設定: IL2CPP、ARM64 のみ、最小 API 28、パッケージ名 `com.haru452.vrctranslator`、マイクの権限

- [ ] **Step 1:** プロジェクトを作り、LLMUnity と sherpa-onnx を入れて、空のシーンで APK がビルドできることを確かめる。sherpa-onnx の版は最新のリリースを使い、版を設計書に書く
- [ ] **Step 2:** `FeasibilityCheck` を書く。起動すると次を順に行い、結果を画面と logcat（タグ `FEAS`）に出す
  - SenseVoice: モデルに付いている `test_wavs/{zh,en,ja,ko,yue}.wav` を 3 回ずつ認識し、言語、文、`Event`、1 回の時間、音声の長さを出す。加えて、無音 3 秒と、BGM の WAV（`test_wavs` にない場合は、フリー素材の音楽 10 秒を `adb push`）を認識し、`Event` が話し声以外になるか、文が空になるかを出す
  - Gemma 1B と 4B: 下の 16 文を、Termux 版のテストと同じ指示文の形（`Translate this into natural, casual {lang}, like friends chatting. Reply with only the translation.`）で訳し、時間と訳を出す。LLMUnity の `LLM.SetModel` に端末上のフルパスを渡して読み込めるかもここで確かめる
  - 日 → 中: こんばんは！今日はどこから来たの？／そのアバターめっちゃかわいいね／ちょっと待って、マイクの調子が悪いみたい／日本に住んでるよ。英語はまだ勉強中なんだ／一緒に写真撮ろうよ／ごめん、もう一回言ってくれる？／このワールドすごくきれいだね／明日も同じ時間にインするよ
  - 中 → 日: 你好！你是第一次来这里吗？／你的头像好可爱，在哪里买的？／不好意思，我没听清楚，你能再说一遍吗？／我是从台湾来的，一直想去日本玩。／我们去另一个世界吧，这里有点卡。／没关系，慢慢来。／明天我差不多同一时间上线。／哈哈哈，你太搞笑了
- [ ] **Step 3:** R9 で動かす（画面をつけたまま）。合格の目安: SenseVoice は 10 秒の声で 1 秒以内、5 言語の判定が正しい、BGM・無音で話し声と判定しない。Gemma 1B の日 → 中が意味の通る中国語
- [ ] **Step 4:** 結果を設計書の末尾「確かめる作業の結果（日付）」に表で書く。設計書の代わりの方法を使うとき（Whisper small、中国語は 4B）はここで決め、以降の Task の該当箇所を書き換える。機能を減らすことになるときは、利用者に聞くまで先へ進まない
- [ ] **Step 5:** Commit（`feat(app): Unity project and feasibility check on AQUOS R9`）

---

### Task 2: KatOsc（`kat_osc.py` の移植）

**Files:**
- Create: `Assets/Translator/Core/Translator.Core.asmdef`、`Core/KatOsc.cs`
- Create: `Assets/Translator/Tests/Translator.Core.Tests.asmdef`、`Tests/KatOscTests.cs`

**Interfaces:**
- Produces:
  - `static class KatOsc { const string ParamPrefix = "/avatar/parameters/"; const int TextLimit = 128; const int ClearPointer = 255; }`
  - `static List<int> EncodeText(string text)`（表にない文字は落とす。`kat_osc.encode_text` と同じ）
  - `static int CharWidth(char ch)`、`static float CodeToFloat(int code)`
  - `static byte[] OscMessage(string address, params object[] values)`（`bool`、`int`、`float`、`string`）
  - `static (string Address, object[] Args) ParseMessage(byte[] data)`（壊れていたら `FormatException`）
  - `static List<List<byte[]>> BuildMessages(string text, int sync = 8, bool clear = true)`

- [ ] **Step 1:** `Tools/translator/test_kat_osc.py` のテストを、同じ値のまま NUnit に移す（名前は PascalCase。例: `test_round_trip_types` → `RoundTripTypes`）。Python の `ValueError` は `FormatException`
- [ ] **Step 2:** テストを実行し、コンパイルエラーで失敗することを確かめる
- [ ] **Step 3:** `KatOsc.cs` を書く。文字表は `kat_osc.py` の `_build_table` と同じ並び（KAT v1.3.2 の `KAT_CharTiles.png`）
- [ ] **Step 4:** テストが通ることを確かめる
- [ ] **Step 5:** Commit（`feat(app): port KAT/OSC encoding to C#`）

---

### Task 3: Segmenter（`voice.py` の移植）

**Files:**
- Create: `Core/Segmenter.cs`、`Tests/SegmenterTests.cs`

**Interfaces:**
- Produces:
  - `class SegmenterSettings { float StartS=0.15f, EndSilenceS=0.8f, PrerollS=0.3f, MaxS=60, MinS=0.5f, Margin=3.0f, FloorMin=200f; }`
  - `class Segmenter { const int Rate = 16000; const int FrameSamples = 480; Segmenter(SegmenterSettings s); float Threshold { get; } void Reset(); short[] Feed(short[] frame); }`（区切りができたら PCM、それ以外は `null`）
  - `static float Segmenter.Rms(short[] frame)`

- [ ] **Step 1:** `Tools/translator/phone/test_voice.py` の Segmenter と `rms` のテストを、同じ値で移す（WAV に関するテストは移さない。SenseVoice には PCM を直接渡すため）
- [ ] **Step 2:** 失敗を確かめる
- [ ] **Step 3:** `voice.py` の `Segmenter` と同じ動き（`TAIL_S = 0.2` を含む）で書く
- [ ] **Step 4:** 通ることを確かめる
- [ ] **Step 5:** Commit（`feat(app): port voice segmenter to C#`）

---

### Task 4: 表示（`display.py` の移植 + 中国語の振り分け）

**Files:**
- Create: `Core/Lang.cs`、`Core/Compose.cs`、`Core/KatDisplay.cs`、`Tests/ComposeTests.cs`、`Tests/KatDisplayTests.cs`

**Interfaces:**
- Produces:
  - `enum Lang { None, Ja, En, Zh, Other }`、`static Lang LangCodes.Parse(string code)`（`"ja"|"en"|"zh"`、ほかは `Other`、空は `None`）
  - `class TranslationResult { Lang Source; Lang Target; string Original; string Translation; string KanaOriginal; string KanaTranslation; }`
  - `enum DisplayMode { Kat, Chatbox, Both }`
  - `static class Compose { const int Row = 32; const int ChatboxLimit = 144; string Kat(TranslationResult r, bool showOriginal); string Chatbox(TranslationResult r, bool showOriginal); List<string> Paginate(string text, int limit = 128, int wordBacktrack = 20); }`
  - `class KatDisplay { KatDisplay(Action<byte[]> send, DisplayMode mode, int sync, bool showOriginal, Func<double> clock, double pageSeconds = 5, double resendSeconds = 3, double stepSeconds = 0.25, double errorSeconds = 3); void Thinking(); void Show(TranslationResult r); void Error(); void Clear(); void Stop(); void Tick(); bool TargetIsChinese { get; set; } }`

`Compose.Kat` の並び（設計書の表）:

| Source → Target | 1 行目から | 原文（`showOriginal`） |
|---|---|---|
| Ja → En | `Translation` | `(` + `KanaOriginal` + `)` |
| En → Ja | `KanaTranslation` | `(` + `Original` + `)` |
| Zh → Ja | `KanaTranslation` | 出さない |

`KanaOriginal` / `KanaTranslation` が空なら、その部分を出さない。

- [ ] **Step 1:** `phone/test_display.py` のテストを同じ値で移す（`Result("ja", ...)` は `TranslationResult` に置き換える）。加えて次を書く:
  - `ZhTargetGoesToChatboxEvenInKatMode`: `mode=Kat`、`Source=Ja, Target=Zh` を `Show` → 送られたのは `/chatbox/input`（`Translation + "\n(" + Original + ")"`、`true`、`false`）だけで、`KAT_` の値は 0 件。`Thinking()` も KAT に `...` を書かない（相手が中国語のとき KAT は使わない）
  - `ZhSpeechShowsKanaWithoutOriginal`: `Source=Zh, Target=Ja, KanaTranslation="ありがとう"`、`showOriginal=true` → `Compose.Kat` は `"ありがとう"`
  - `EmptyKanaOriginalDropsTheBracket`: `Ja→En`、`KanaOriginal=""` → `Compose.Kat` は `Translation` だけ
- [ ] **Step 2:** 失敗を確かめる
- [ ] **Step 3:** `display.py` と同じ動き（表示中の答えは切らない、後ろに最大 3 件まで並ぶ、`MAX_BACKLOG = 3`）で書く。`Target == Zh` の結果は、`mode` にかかわらずチャットボックスだけに送る。`Thinking()` は、KAT とチャットボックスのどちらに出すかを、直前に `KatDisplay.TargetIsChinese`（`bool`、`OwnVoicePipeline` が設定する）で決める
- [ ] **Step 4:** 通ることを確かめる
- [ ] **Step 5:** Commit（`feat(app): port KAT display with Chinese routed to chatbox`）

---

### Task 5: かなへの変換

**Files:**
- Create: `Core/KanaConverter.cs`、`Tests/KanaConverterTests.cs`
- Create: `Assets/Translator/Plugins/NMeCab/`（`LibNMeCab.dll`）、`Assets/StreamingAssets/ipadic/`（辞書。Android では起動時に `persistentDataPath` へコピーしてから開く）

**Interfaces:**
- Produces: `interface IKanaConverter { string ToKana(string japanese); }`、`class MeCabKana : IKanaConverter { MeCabKana(string dicDir); }`
  - 漢字の語は読み（カタカナ）をひらがなにする。かな、英数字、記号はそのまま。読みのない語（未知語）は、KAT の表にない文字を落としたものにする（`KatOsc.EncodeText` で表にある文字だけ残す）

- [ ] **Step 1:** テスト（辞書は `Assets/StreamingAssets/ipadic` を直接開く）:
  - `ToKana("今日は寒いね")` → `"きょうはさむいね"`
  - `ToKana("マイクの調子が悪い")` → `"マイクのちょうしがわるい"`
  - `ToKana("VRChatで会おう!")` → `"VRChatであおう!"`
  - `ToKana("")` → `""`
  - 結果の各文字が `KatOsc.EncodeText` で 1 文字も落ちない（`ToKana("東京タワーに行った😊")` で確かめる。絵文字は消える）
- [ ] **Step 2:** 失敗を確かめる
- [ ] **Step 3:** 書く（NMeCab の `MeCabIpaDicTagger` の `Reading` を使う）。NMeCab と IPADIC のライセンスを `Tools/translator/app/LICENSES.md` に書く
- [ ] **Step 4:** 通ることを確かめる
- [ ] **Step 5:** Commit（`feat(app): kana conversion with NMeCab for KAT`）

---

### Task 6: Gemma の指示文と後片付け

**Files:**
- Create: `Core/TranslationPrompt.cs`、`Tests/TranslationPromptTests.cs`

**Interfaces:**
- Produces:
  - `interface ITextTranslator { Task<string> CompleteAsync(string prompt, CancellationToken ct); }`（Task 9 で LLMUnity につなぐ）
  - `static string TranslationPrompt.Build(Lang source, Lang target, string text)`: Gemma 3 の形 `<start_of_turn>user\n{指示}\n\n{text}<end_of_turn>\n<start_of_turn>model\n`。`{指示}` は向きごとに固定の文（訳だけを 1 行で返す、前置き・引用符・読み仮名・説明をつけない）と、向きごとの例 2 つ。`text` より前の部分は向きごとに毎回同じ文字列にする（LLMUnity の `cachePrompt` を効かせるため）
  - `static string TranslationPrompt.Clean(string raw)`

- [ ] **Step 1:** テスト:
  - `BuildEndsWithModelTurn`: `Build(Ja, En, "こんにちは")` が `"<start_of_turn>model\n"` で終わり、`"こんにちは"` を含む
  - `BuildPrefixIsStablePerDirection`: `Build(Ja, En, "a")` と `Build(Ja, En, "b")` の、`text` の手前までが同じ。`(Ja,En)`、`(Ja,Zh)`、`(En,Ja)`、`(Zh,Ja)` の 4 つで違う
  - `Clean` の入力 → 出力:
    - `"もちろん、もう一度言ってくれる？"` → `"もう一度言ってくれる？"`
    - `"Sure! Let's take some pictures!"` → `"Let's take some pictures!"`
    - `"“That avatar is so cute, right?”"` → `"That avatar is so cute, right?"`
    - `"> Yeah, same time tomorrow!"` → `"Yeah, same time tomorrow!"`
    - `"何してるの？ (Nani shiteru no?)"` → `"何してるの？"`
    - `"もちろん！\nそれ、めっちゃ可愛いね！"` → `"それ、めっちゃ可愛いね！"`（前置きだけの行を捨て、残りの最初の行）
    - `"Hello\n\nThis means a greeting."` → `"Hello"`
    - `"  "` → `""`
- [ ] **Step 2:** 失敗を確かめる
- [ ] **Step 3:** 書く。前置きの一覧: `もちろん` `はい` `了解` `Sure` `Of course` `OK` `Okay`（後ろの `、` `,` `!` `！` `。` と空白も取る）。括弧の中がローマ字（ASCII の英字と空白と記号だけ）で、本文が日本語か中国語のときだけ、その括弧を取る
- [ ] **Step 4:** 通ることを確かめる
- [ ] **Step 5:** Commit（`feat(app): Gemma translation prompt and output cleanup`）

---

### Task 7: 自分の声の流れ（`vrc_translator.py` の App の移植）と設定、記録

**Files:**
- Create: `Core/TranslatorSettings.cs`、`Core/TranslationLog.cs`、`Core/OwnVoicePipeline.cs`、`Tests/OwnVoicePipelineTests.cs`、`Tests/TranslationLogTests.cs`

**Interfaces:**
- Consumes: `Segmenter`（Task 3）、`KatDisplay`・`TranslationResult`・`Lang`（Task 4）、`IKanaConverter`（Task 5）、`TranslationPrompt`・`ITextTranslator`（Task 6）
- Produces:
  - `interface ISpeechRecognizer { RecognizedSpeech Recognize(short[] pcm); }`、`class RecognizedSpeech { Lang Lang; string Text; bool IsSpeech; }`
  - `class TranslatorSettings { string PcIp = ""; int RelayPort = 9002; int ListenPort = 9001; bool ShowOriginal = true; DisplayMode Display = DisplayMode.Kat; int KatSync = 8; Lang TargetLang = Lang.En; string OwnModel = "1B"; string OtherModel = "4B"; SegmenterSettings Segmenter = new(); }`
  - `class TranslationLog { TranslationLog(string path); void Append(DateTime when, string who, Lang lang, string original, string translation, string engine); static string FormatLine(...); }`（`who` は `"self"` / `"other"`。タブと改行は空白にする）
  - `class OwnVoicePipeline { const double StaleSeconds = 20; const double SlowSeconds = 15; OwnVoicePipeline(TranslatorSettings s, ISpeechRecognizer stt, ITextTranslator translator, KatDisplay display, IKanaConverter kana, TranslationLog log, Func<double> clock, Func<DateTime> now, Action<string> say); bool Active { get; } bool? LocalAI { get; } bool UseLocalAI { get; } void OnOsc(string address, object[] args); void FeedFrame(short[] frame); Task<bool> ProcessNextAsync(CancellationToken ct); void OnPause(); void OnResume(); event Action<string> Status; }`
  - `UseLocalAI` は `LocalAI != false`（値が来ていない `null` もローカル）。この計画では Gemini がないので、`false` のときも訳はローカルで行い、`say("Gemini はまだありません。ローカル AI で訳します")` を変わったときだけ 1 回出す

訳す向き: 話した言語 `Ja` → `settings.TargetLang`、`En` / `Zh` → `Ja`、`Other` / `None` / `IsSpeech == false` → `display.Clear()` して記録しない。

- [ ] **Step 1:** `phone/test_app.py` の `AppTests` と `ReviewFixTests` を同じ筋書きで移す（偽の `ISpeechRecognizer`、`ITextTranslator`、時計）。Gemini の例外は `ITextTranslator` の例外に置き換える。加えて:
  - `JapaneseToChineseWhenTargetIsZh`: `TargetLang = Zh`、認識 `(Ja, "こんにちは")` → `TranslationPrompt.Build(Ja, Zh, "こんにちは")` が渡り、`display.Show` の `Target == Zh`
  - `EnglishSpeechGetsKanaTranslation`: 認識 `(En, "thank you")`、訳 `"ありがとう"` → `KanaTranslation == kana.ToKana("ありがとう")`
  - `UnsupportedLanguageClearsDots`: 認識 `(Other, "안녕")` → `Thinking` のあと `Clear`、記録 0 行、翻訳は呼ばれない
  - `NonSpeechIsDropped`: `IsSpeech = false` → 同上
  - `LocalAIUnknownMeansLocal`: 起動直後 `UseLocalAI == true`。`/avatar/change` のあとも `true`
  - `SlowTranslationIsReported`: 偽の翻訳が時計を 16 秒進める → `Status` に `"遅くなっています"` を含む通知が 1 回
  - `ResumeAfterPauseRestartsMic`: `Active` のまま `OnPause()` → `OnResume()` のあと `FeedFrame` した声が訳される。`OnPause` の時点で区切り途中の声は捨てる
  - `TranslationLogTests`: `FormatLine` にタブ・改行を入れても 1 行、7 列（日時、who、lang、原文、訳、engine）
- [ ] **Step 2:** 失敗を確かめる
- [ ] **Step 3:** 書く（`vrc_translator.App` と同じ世代番号の考え方。録音には `clock()` の時刻を付け、取り出したとき `StaleSeconds` を超えていたら捨てる）
- [ ] **Step 4:** 通ることを確かめる
- [ ] **Step 5:** Commit（`feat(app): own-voice pipeline with local AI`）

---

### Task 8: PC の中継とのやりとり

**Files:**
- Create: `Core/RelayProtocol.cs`、`Core/RelayLink.cs`、`Tests/RelayProtocolTests.cs`

**Interfaces:**
- Produces:
  - `static class RelayProtocol { const string Hello = "/translator/hello"; static byte[] HelloMessage(int session, bool othersOn); }`
  - `class RelayLink : IDisposable { RelayLink(Func<string> pcIp, int relayPort, int listenPort, Action<string, object[]> onOsc); bool Found { get; } void Send(byte[] packet); void Tick(double now); }`（`Tick` が 5 秒ごとに hello を送る。受信は別スレッドで、`onOsc` はそのスレッドから呼ぶ。`Found` は PC から何か届いたら `true`、最後に届いてから 15 秒で `false`）

- [ ] **Step 1:** テスト:
  - `HelloCarriesSessionAndOthersFlag`: `KatOsc.ParseMessage(HelloMessage(123, false))` → `("/translator/hello", [123, false])`
  - `SendFailureDoesNotThrow`: 存在しない宛先（`"203.0.113.1"`、ポート 9）に `Send` しても例外が出ない
  - `EmptyPcIpSendsNothing`: `pcIp` が `""` のとき `Send` は何もしない、hello も送らない
  - `ReceivesToggleFromLoopback`: `listenPort = 0`（空いている番号）で作り、`127.0.0.1` からその番号へ `OscMessage(ParamPrefix + "TranslatorActive", true)` を送る → 1 秒以内に `onOsc` が呼ばれる
- [ ] **Step 2:** 失敗を確かめる
- [ ] **Step 3:** 書く（`UdpClient`。受信のタイムアウト 200 ミリ秒。`Dispose` でスレッドを止める）
- [ ] **Step 4:** 通ることを確かめる
- [ ] **Step 5:** Commit（`feat(app): relay link with hello`）

---

### Task 9: アプリにつなぎ、R9 で自分の声を確かめる

**Files:**
- Create: `App/Translator.App.asmdef`、`App/AndroidMic.cs`、`App/SherpaSenseVoice.cs`、`App/LlmUnityTranslator.cs`、`App/SettingsStore.cs`、`App/TranslatorMain.cs`、`App/MainScreen.cs`、`Assets/Scenes/Main.unity`、`Assets/Fonts/`
- Modify: `Assets/Editor/BuildApk.cs`（`Main.unity` だけをビルドに入れる）
- Delete: `Assets/Translator/Feasibility/`

**Interfaces:**
- Consumes: Task 2〜8 のすべて
- `AndroidMic`: `Microphone.Start(null, true, 1, 16000)` から 480 サンプルずつ `short[]` を出す。権限がなければ求める
- `SherpaSenseVoice : ISpeechRecognizer`: `OfflineRecognizer`（SenseVoice、`UseInverseTextNormalization = 1`、スレッド 2）。結果の `Lang`（`<|ja|>` など）を `LangCodes.Parse` に、`Event` が `Speech` 以外か文が空なら `IsSpeech = false`
- `LlmUnityTranslator : ITextTranslator`: `LLM` と `LLMClient` を 1B と 4B で 1 組ずつ持ち、`CompleteAsync` は `OwnModel` の組の `Completion(prompt)`。`numPredict = 96`、`temperature = 0`、`cachePrompt = true`。キャンセルは `CancelRequest`
- `SettingsStore`: `TranslatorSettings` を `PlayerPrefs` に JSON で保存・読み込み
- `TranslatorMain`: 起動 → モデルがそろっているか確かめる（なければ画面に「モデルがありません: <パス>」）→ 読み込み → `RelayLink`、`OwnVoicePipeline` を作る。`Update` で `display.Tick()` と `relay.Tick()`。翻訳は `Task.Run` のループで `ProcessNextAsync`。`OnApplicationPause` で `OnPause/OnResume`。`Screen.sleepTimeout = SleepTimeout.NeverSleep`
- `MainScreen`（uGUI、TextMeshPro、Noto Sans JP + SC のダイナミックフォント）: 上に状態（中継: つながった / 見つからない、自分: 聞いている / 止まっている、AI: ローカル）、中央は空き（相手の声は計画 3）、下に「相手: 英語 / 中国語」の切り替えと設定ボタン。設定: PC の IP、原文も出す、出し先（KAT / チャットボックス / 両方）、KAT の同期設定。変えたらすぐ保存して反映

- [ ] **Step 1:** 上のつなぎを書き、EditMode テストがすべて通ること、APK がビルドできることを確かめる
- [ ] **Step 2:** R9 にインストールし、モデルを `adb push` する。PC で `start_relay.bat` を起動し、アプリに PC の IP を入れる
- [ ] **Step 3:** 利用者と一緒に確かめる（設計書の受け入れ基準のうち、自分の声の分）:
  - 「翻訳」をオンにして「おはよう」 → 3 秒以内にペットの上に英訳
  - 相手を中国語にして話す → チャットボックスに中国語の訳
  - 英語で話す → かなの日本語訳がペットの上に
  - スマホを機内モード + Wi-Fi（インターネットなし）にしても訳せる
  - アプリを一度裏に回して戻しても、聞き続ける
  - 30 分続けても止まらず、遅くならない
- [ ] **Step 4:** 結果を設計書の末尾に追記する
- [ ] **Step 5:** Commit（`feat(app): own voice translated on-device end to end`）

---

## このあとの計画（今は書かない）

- 計画 2: PC の `translator_pc.exe`（C#。中継の移植、VRChat の音の取り出しと送信）
- 計画 3: 相手の声（受信、SenseVoice、4B、画面、自分の声を先にする順番待ち）
- 計画 4: Gemini への切り替えと API キーの欄、モデルのダウンロード、記録の共有、ライセンス画面、Termux 版の削除
