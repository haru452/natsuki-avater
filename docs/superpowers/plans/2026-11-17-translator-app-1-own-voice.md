# 翻訳アプリ 計画 1：自分の声（ローカル AI）Implementation Plan（Kotlin 版）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** AQUOS R9 の Android アプリで、自分の声をスマホの中だけで訳し（SenseVoice + Gemma）、今の Python の中継を通して KAT / チャットボックスに出す。

**Architecture:** Android に依存しない処理（OSC、KAT の文字、声の区切り、表示、指示文、流れの管理、中継とのやりとり）は `core` パッケージに置き、Python 版のテストを JUnit に移して JVM で確かめる。マイク、SenseVoice、llama.cpp（JNI）、フォアグラウンドサービス、画面は `app` パッケージの薄いつなぎにする。

**Tech Stack:** Kotlin 2.0.21、Android Gradle Plugin 8.7.3、Gradle 8.11.1（wrapper）、JDK 22（`C:/Program Files/Java/jdk-22`）、Android SDK は Unity 付属のもの（platform 35、build-tools 34.0.0）、Jetpack Compose、sherpa-onnx v2.0.1 AAR（static onnxruntime）、llama.cpp（commit 9581154、`native/build_android.sh`）、Kuromoji IPADIC 0.9.0、JUnit 4

**Spec:** `docs/superpowers/specs/2026-11-17-translator-app-design.md`（末尾の「確かめる作業の結果と、作り方の変更」が最新）。表示と OSC の決まりは `2026-11-16-phone-translator-a-design.md` から引き継ぐ

この計画の範囲外（後の計画）: PC の C# `.exe`、相手の声、Gemini、API キーの欄、モデルのダウンロード、ライセンス画面。この計画の間、PC 側は `Tools/translator/pc/pc_osc_relay.py` を使う。

## Global Constraints

- 置き場所: `Tools/translator/android/`。`build/`、`.gradle/`、`local.properties`、`native/.build/`、`*.gguf`、`*.onnx`、`*.aar`（`app/libs/` に置く。`fetch_deps.sh` で取る）は git に入れない
- 1 ファイル 600 行以内（利用者のルール）
- `core` パッケージ（`com.haru452.vrctranslator.core`）は `android.*` を import しない
- ローカル AI のとき、声も文もインターネットへ出さない（アプリは `INTERNET` 権限を持つが、計画 1 では UDP で PC へ送るだけ）
- PC の中継との約束: スマホ → `PC_IP:9002`、PC → スマホ `9001`。hello は `/translator/hello`、引数 `(int session, bool othersOn)`、5 秒ごと。`session` は起動ごとに `1..2^31-1` の乱数
- KAT: 32 文字 × 4 行（128）、同期設定 8、ポインタ 255 で消去。チャットボックスは 144 文字
- 時間の値: ページ 5 秒、送り直し 3 秒、1 段 0.25 秒、`(error)` 3 秒、20 秒以上待った声は捨てる、1 件 15 秒を超えたら「遅くなっています」
- 声の区切り: 始まり 0.15 秒、終わりの無音 0.8 秒、さかのぼり 0.3 秒、最大 60 秒、最短 0.5 秒、`margin` 3.0、`floorMin` 200、フレーム 30 ミリ秒（16 kHz で 480 サンプル）
- 「ローカルAI」の値がまだ届いていないときは、ローカル AI として扱う
- モデルの使い分け: 日本語 ↔ 英語は設定 `ownModel`（初期 1B）、中国語がからむ向きは常に 4B
- SenseVoice の結果で、記号と空白を除いた文字が 1 文字以下なら「話し声ではない」とする

テストの実行: `cd Tools/translator/android && ./gradlew testDebugUnitTest`（合格: `BUILD SUCCESSFUL`）。APK: `./gradlew assembleDebug`（`app/build/outputs/apk/debug/app-debug.apk`）。インストールは Unity 付属の `adb`。

## Review Focus

1. **アプリが裏に回る・画面が消える**: サービスが動き続け、マイクと翻訳が止まらない → Task 9 の実機確認（画面を消して 5 分）
2. **Gemma が訳以外を返す**（「もちろん、」、引用符、ローマ字の括弧、絵文字、`或者` 以降の別案、`>`、2 行目の説明、空）→ Task 6 の `clean` のテスト
3. **かなに直せない文字**（漢字の固有名詞、絵文字）→ KAT に `?` を出さない → Task 5 のテスト
4. **PC の中継がない・IP が空**: 送信の失敗で止まらず、画面に「中継: 見つからない」→ Task 8 のテスト
5. **SenseVoice が対象外の言語・1 文字だけ**: 何も出さず `...` も消す → Task 7 のテスト

---

## ファイル構成

```
Tools/translator/android/
├ settings.gradle.kts, build.gradle.kts, gradle.properties, gradlew(.bat), gradle/wrapper/
├ fetch_deps.sh                    … sherpa-onnx の AAR を app/libs へ取る
├ native/                          … translator_llm.c、CMakeLists.txt、build_android.sh（.so を app/src/main/jniLibs/arm64-v8a へ）
└ app/
   ├ build.gradle.kts
   ├ src/main/AndroidManifest.xml
   ├ src/main/java/com/haru452/vrctranslator/core/
   │   KatOsc.kt, Segmenter.kt, Lang.kt, Compose.kt, KatDisplay.kt, KanaConverter.kt,
   │   TranslationPrompt.kt, TranslatorSettings.kt, TranslationLog.kt, OwnVoicePipeline.kt, RelayLink.kt
   ├ src/main/java/com/haru452/vrctranslator/app/
   │   NativeLlm.kt, SenseVoice.kt, Mic.kt, ModelFiles.kt, SettingsStore.kt, TranslatorService.kt, MainActivity.kt
   └ src/test/java/com/haru452/vrctranslator/core/   … 各ファイルのテスト
```

---

### Task 1: 確かめる作業（済み）

R9 で SenseVoice と Gemma の中国語を確かめた。結果と変更は設計書の末尾に書いた。Unity のプロジェクトは消し、`native/` だけ残した。

- [x] 確かめる、結果を設計書に書く
- [ ] Commit（`docs: feasibility results; switch the app to Kotlin`）

---

### Task 2: Android プロジェクトと KatOsc（`kat_osc.py` の移植）

**Files:**
- Create: `Tools/translator/android/` の Gradle 一式（`gradle wrapper --gradle-version 8.11.1` で wrapper を作る）、`.gitignore`、`fetch_deps.sh`
- Create: `core/KatOsc.kt`、`test/.../core/KatOscTests.kt`

**Interfaces:**
- Produces（`object KatOsc`）:
  - `const val TEXT_LIMIT = 128`、`CLEAR_POINTER = 255`、`PARAM_PREFIX = "/avatar/parameters/"`、`val SYNC_CHOICES = listOf(4, 8, 16)`
  - `fun encodeText(text: String): List<Int>`（表にない文字は `?`）
  - `fun charWidth(ch: String): Int`（1 コードポイント）
  - `fun codeToFloat(code: Int): Float`
  - `fun oscMessage(address: String, vararg values: Any): ByteArray`（`Boolean`、`Int`、`Float`、`String`）
  - `fun parseMessage(data: ByteArray): Pair<String, List<Any>>`（壊れていたら `IllegalArgumentException`）
  - `fun buildMessages(text: String, sync: Int = 8, clear: Boolean = true): List<List<ByteArray>>`

- [ ] **Step 1:** `Tools/translator/test_kat_osc.py` のテストを同じ値で JUnit に移す（`send` のテストは移さない）
- [ ] **Step 2:** `./gradlew testDebugUnitTest` で失敗（コンパイルエラー）を確かめる
- [ ] **Step 3:** `KatOsc.kt` を書く（`java.text.Normalizer` で NFKC / NFD）
- [ ] **Step 4:** 通ることを確かめる
- [ ] **Step 5:** Commit（`feat(android): project and KAT/OSC encoding`）

---

### Task 3: Segmenter（`voice.py` の移植）

**Files:** Create `core/Segmenter.kt`、`test/.../SegmenterTests.kt`

**Interfaces:**
- Produces: `data class SegmenterSettings(var startS: Float = 0.15f, var endSilenceS: Float = 0.8f, var prerollS: Float = 0.3f, var maxS: Float = 60f, var minS: Float = 0.5f, var margin: Float = 3f, var floorMin: Float = 200f)`、`class Segmenter(s: SegmenterSettings) { companion object { const val RATE = 16000; const val FRAME_SAMPLES = 480; fun rms(frame: ShortArray): Float }; val threshold: Float; val noiseFloor: Float; val recording: Boolean; fun reset(); fun feed(frame: ShortArray): ShortArray? }`

- [ ] **Step 1:** `phone/test_voice.py` の `rms` と Segmenter のテストを同じ値で移す
- [ ] **Step 2:** 失敗を確かめる
- [ ] **Step 3:** `voice.py` と同じ動き（`TAIL_S = 0.2`、`round` は偶数丸め）で書く
- [ ] **Step 4:** 通ることを確かめる
- [ ] **Step 5:** Commit（`feat(android): voice segmenter`）

---

### Task 4: 表示（`display.py` の移植 + 中国語の振り分け）

**Files:** Create `core/Lang.kt`、`core/Compose.kt`、`core/KatDisplay.kt`、`test/.../ComposeTests.kt`、`test/.../KatDisplayTests.kt`

**Interfaces:**
- Produces:
  - `enum class Lang { NONE, JA, EN, ZH, OTHER; companion object { fun parse(code: String): Lang } }`（`"ja"|"en"|"zh"`、`<|ja|>` の形も読む。空は `NONE`、ほかは `OTHER`）
  - `data class TranslationResult(val source: Lang, val target: Lang, val original: String, val translation: String, val kanaOriginal: String = "", val kanaTranslation: String = "")`
  - `enum class DisplayMode { KAT, CHATBOX, BOTH }`
  - `object Compose { const val ROW = 32; const val CHATBOX_LIMIT = 144; fun kat(r: TranslationResult, showOriginal: Boolean): String; fun chatbox(r: TranslationResult, showOriginal: Boolean): String; fun paginate(text: String, limit: Int = 128, wordBacktrack: Int = 20): List<String> }`
  - `class KatDisplay(send: (ByteArray) -> Unit, mode: DisplayMode, sync: Int, showOriginal: Boolean, clock: () -> Double, pageSeconds: Double = 5.0, resendSeconds: Double = 3.0, stepSeconds: Double = 0.25, errorSeconds: Double = 3.0) { var targetIsChinese: Boolean; fun thinking(); fun show(r: TranslationResult); fun error(); fun clear(); fun stop(); fun tick() }`

`Compose.kat` の並び:

| source → target | 1 行目から | 原文（`showOriginal`） |
|---|---|---|
| JA → EN | `translation` | `(` + `kanaOriginal` + `)` |
| EN → JA | `kanaTranslation` | `(` + `original` + `)` |
| ZH → JA | `kanaTranslation` | 出さない |

かなの部分が空なら、その部分を出さない。`target == ZH` の結果は、`mode` にかかわらずチャットボックスだけ。`targetIsChinese` のとき、`thinking()` は KAT に `...` を書かない。

- [ ] **Step 1:** `phone/test_display.py` の全テストを同じ値で移す（Python の `Result(lang, original, translation, kana)` は、`ja` なら `JA→EN` で `kanaOriginal = kana`、`en` なら `EN→JA` で `kanaTranslation = kana`）。加えて `zhTargetGoesToChatboxEvenInKatMode`（`showOriginal = true`、`JA→ZH`、`"你好\n(こんにちは)"` が `/chatbox/input` に、`KAT_` は 0 件）、`zhSpeechShowsKanaWithoutOriginal`、`emptyKanaOriginalDropsTheBracket`
- [ ] **Step 2:** 失敗を確かめる
- [ ] **Step 3:** `display.py` と同じ動き（`MAX_BACKLOG = 3`、表示中の答えは切らない）で書く
- [ ] **Step 4:** 通ることを確かめる
- [ ] **Step 5:** Commit（`feat(android): KAT display with Chinese routed to chatbox`）

---

### Task 5: かなへの変換（Kuromoji）

**Files:** Create `core/KanaConverter.kt`、`test/.../KanaConverterTests.kt`。依存: `com.atilika.kuromoji:kuromoji-ipadic:0.9.0`

**Interfaces:** `interface KanaConverter { fun toKana(japanese: String): String }`、`class KuromojiKana : KanaConverter`

漢字を含む語は読み（カタカナ）をひらがなにする。かな・英数字・記号はそのまま。読みのない語は、`KatOsc` の表で `?` にならない文字だけ残す。

- [ ] **Step 1:** テスト: `"今日は寒いね"` → `"きょうはさむいね"`、`"マイクの調子が悪い"` → `"マイクのちょうしがわるい"`、`"VRChatで会おう!"` → `"VRChatであおう!"`、`""` → `""`、`"東京タワーに行った😊"` の結果を `KatOsc.encodeText` しても `?`（code 31）が 1 つもない
- [ ] **Step 2:** 失敗を確かめる
- [ ] **Step 3:** 書く。Kuromoji のライセンスを `Tools/translator/android/LICENSES.md` に書く
- [ ] **Step 4:** 通ることを確かめる
- [ ] **Step 5:** Commit（`feat(android): kana conversion with Kuromoji`）

---

### Task 6: Gemma の指示文と後片付け

**Files:** Create `core/TranslationPrompt.kt`、`test/.../TranslationPromptTests.kt`

**Interfaces:**
- `interface TextTranslator { fun complete(model: String, prompt: String): String; fun cancel() }`（`model` は `"1B"` / `"4B"`。Task 9 で JNI につなぐ）
- `object TranslationPrompt { fun build(source: Lang, target: Lang, text: String): String; fun clean(raw: String): String; fun modelFor(source: Lang, target: Lang, ownModel: String): String }`
  - `build`: `<start_of_turn>user\n{指示}\n\n{text}<end_of_turn>\n<start_of_turn>model\n`。指示は向きごとに固定の文と例 2 つ。「訳だけを 1 行で。前置き・引用符・読み仮名・絵文字・別案をつけない。avatar（アバター）は VRChat で使う自分の姿のこと」
  - `modelFor`: 中国語がからむ向きは `"4B"`、ほかは `ownModel`

- [ ] **Step 1:** テスト:
  - `buildEndsWithModelTurn`、`buildPrefixIsStablePerDirection`（`(JA,EN)`、`(JA,ZH)`、`(EN,JA)`、`(ZH,JA)` の 4 つで違い、同じ向きなら `text` の手前まで同じ）
  - `modelFor(JA, ZH, "1B") == "4B"`、`modelFor(ZH, JA, "1B") == "4B"`、`modelFor(JA, EN, "1B") == "1B"`、`modelFor(EN, JA, "4B") == "4B"`
  - `clean` の入力 → 出力:
    - `"もちろん、もう一度言ってくれる？"` → `"もう一度言ってくれる？"`
    - `"Sure! Let's take some pictures!"` → `"Let's take some pictures!"`
    - `"“That avatar is so cute, right?”"` → `"That avatar is so cute, right?"`
    - `"> Yeah, same time tomorrow!"` → `"Yeah, same time tomorrow!"`
    - `"何してるの？ (Nani shiteru no?)"` → `"何してるの？"`
    - `"笑いすぎ！(Wai sugi!) 🤣"` → `"笑いすぎ！"`
    - `"好可爱啊！ 😊   或者：  厉害了，连你都爱了！ 😉"` → `"好可爱啊！"`
    - `"もちろん！\nそれ、めっちゃ可愛いね！"` → `"それ、めっちゃ可愛いね！"`
    - `"Hello\n\nThis means a greeting."` → `"Hello"`
    - `"  "` → `""`
- [ ] **Step 2:** 失敗を確かめる
- [ ] **Step 3:** 書く。前置き: `もちろん` `はい` `了解` `Sure` `Of course` `OK` `Okay`（後ろの `、,!！。` と空白も）。別案の印: `或者` `または` `Or:` 以降を捨てる。絵文字（`\p{So}` と `\p{Cs}`）を取る。括弧の中が ASCII だけで本文が日本語か中国語なら、その括弧を取る
- [ ] **Step 4:** 通ることを確かめる
- [ ] **Step 5:** Commit（`feat(android): Gemma prompt and output cleanup`）

---

### Task 7: 自分の声の流れ（`vrc_translator.py` の App の移植）と設定、記録

**Files:** Create `core/TranslatorSettings.kt`、`core/TranslationLog.kt`、`core/OwnVoicePipeline.kt`、`test/.../OwnVoicePipelineTests.kt`、`test/.../TranslationLogTests.kt`

**Interfaces:**
- `interface SpeechRecognizer { fun recognize(pcm: ShortArray): RecognizedSpeech }`、`data class RecognizedSpeech(val lang: Lang, val text: String, val isSpeech: Boolean)`
- `data class TranslatorSettings(var pcIp: String = "", var relayPort: Int = 9002, var listenPort: Int = 9001, var showOriginal: Boolean = true, var display: DisplayMode = DisplayMode.KAT, var katSync: Int = 8, var targetLang: Lang = Lang.EN, var ownModel: String = "1B", var otherModel: String = "4B", var segmenter: SegmenterSettings = SegmenterSettings())`
- `class TranslationLog(file: java.io.File) { fun append(whenTime: java.time.LocalDateTime, who: String, lang: Lang, original: String, translation: String, engine: String); companion object { fun formatLine(...): String } }`
- `class OwnVoicePipeline(settings, stt: SpeechRecognizer, translator: TextTranslator, display: KatDisplay, kana: KanaConverter, log: TranslationLog, clock: () -> Double, now: () -> LocalDateTime, say: (String) -> Unit) { companion object { const val STALE_SECONDS = 20.0; const val SLOW_SECONDS = 15.0 }; val active: Boolean; val localAI: Boolean?; val useLocalAI: Boolean; fun onOsc(address: String, args: List<Any>); fun feedFrame(frame: ShortArray); fun processNext(timeoutMs: Long = 500): Boolean; var onStatus: (String) -> Unit }`
  - `useLocalAI = localAI != false`。計画 1 には Gemini がないので、`false` でもローカルで訳し、`say("Gemini はまだありません。ローカル AI で訳します")` を変わったときだけ出す
  - 訳す向き: `JA` → `settings.targetLang`、`EN`/`ZH` → `JA`、`OTHER`/`NONE`/`!isSpeech` → `display.clear()`、記録しない。`display.targetIsChinese` は `thinking()` の前に `settings.targetLang == ZH && source == JA` にする（`source` が分かる前は `settings.targetLang == ZH`）

- [ ] **Step 1:** `phone/test_app.py` の `AppTests` と `ReviewFixTests` を同じ筋書きで移す（偽の認識、翻訳、時計）。加えて: `japaneseToChineseUses4B`（`targetLang = ZH`、認識 `(JA, "こんにちは")` → `complete("4B", build(JA, ZH, ...))`、`show` の `target == ZH`）、`englishSpeechGetsKanaTranslation`、`unsupportedLanguageClearsDots`、`nonSpeechIsDropped`、`localAIUnknownMeansLocal`（起動直後と `/avatar/change` のあと）、`slowTranslationIsReported`（偽の翻訳が時計を 16 秒進める → `onStatus` に「遅くなっています」が 1 回）、`TranslationLogTests`（タブ・改行を入れても 1 行 6 列: 日時、who、lang、原文、訳、engine）
- [ ] **Step 2:** 失敗を確かめる
- [ ] **Step 3:** 書く（世代番号、録音に時刻を付けて `STALE_SECONDS` を超えたら捨てる、`LinkedBlockingQueue`）
- [ ] **Step 4:** 通ることを確かめる
- [ ] **Step 5:** Commit（`feat(android): own-voice pipeline with local AI`）

---

### Task 8: PC の中継とのやりとり

**Files:** Create `core/RelayLink.kt`、`test/.../RelayLinkTests.kt`

**Interfaces:** `object RelayProtocol { const val HELLO = "/translator/hello"; fun helloMessage(session: Int, othersOn: Boolean): ByteArray }`、`class RelayLink(pcIp: () -> String, relayPort: Int, listenPort: Int, onOsc: (String, List<Any>) -> Unit) : AutoCloseable { val found: Boolean; val boundPort: Int; fun send(packet: ByteArray); fun tick(now: Double) }`（`tick` が 5 秒ごとに hello。受信は別スレッド、`found` は最後に届いてから 15 秒で `false`）

- [ ] **Step 1:** テスト: `helloCarriesSessionAndOthersFlag`、`sendFailureDoesNotThrow`（`"203.0.113.1"`）、`emptyPcIpSendsNothing`、`receivesToggleFromLoopback`（`listenPort = 0`、`127.0.0.1` から `boundPort` へ送ると 1 秒以内に `onOsc`）
- [ ] **Step 2:** 失敗を確かめる
- [ ] **Step 3:** 書く（`DatagramSocket`、受信タイムアウト 200 ミリ秒、`close` でスレッドを止める）
- [ ] **Step 4:** 通ることを確かめる
- [ ] **Step 5:** Commit（`feat(android): relay link with hello`）

---

### Task 9: アプリにつなぎ、R9 で自分の声を確かめる

**Files:** Create `app/NativeLlm.kt`、`app/SenseVoice.kt`、`app/Mic.kt`、`app/ModelFiles.kt`、`app/SettingsStore.kt`、`app/TranslatorService.kt`、`app/MainActivity.kt`、`AndroidManifest.xml`。Modify `native/translator_llm.c`（JNI の入り口を足す、`use_mmap` の代わりに `load_mode` の指定はしない: モデルを内部ストレージに置くので mmap のままでよい）

- `NativeLlm : TextTranslator`: JNI `Java_com_haru452_vrctranslator_app_NativeLlm_load/complete/cancel/free`。1B と 4B を起動時に読み込む（スレッド 6、コンテキスト 1024、最大 96 トークン）
- `SenseVoice : SpeechRecognizer`: sherpa-onnx の `OfflineRecognizer`（スレッド 2）。`lang` を `Lang.parse`、`event` が `<|Speech|>` 以外、または記号と空白を除いて 1 文字以下なら `isSpeech = false`
- `Mic`: `AudioRecord`（`VOICE_RECOGNITION`、16 kHz、モノラル、16 ビット）から 480 サンプルずつ
- `ModelFiles`: `filesDir/models` にそろっているか確かめる。なければ共有ストレージ（`getExternalFilesDir(null)/models`）からコピーする
- `TranslatorService`: フォアグラウンドサービス（`foregroundServiceType="microphone"`、通知「翻訳: 聞いています」）、`PARTIAL_WAKE_LOCK`。`RelayLink`、`OwnVoicePipeline`、翻訳スレッド、50 ミリ秒ごとの `display.tick()` と `relay.tick()`
- `MainActivity`（Compose）: 状態（中継: つながった / 見つからない、自分: 聞いている / 止まっている、AI: ローカル、モデル: 準備中 / できた）、「相手: 英語 / 中国語」、設定（PC の IP、原文も出す、出し先、KAT の同期設定）。開始 / 停止ボタン。権限（マイク、通知）を求める

- [ ] **Step 1:** つなぎを書く。`testDebugUnitTest` がすべて通り、`assembleDebug` が通ることを確かめる
- [ ] **Step 2:** R9 にインストールし、モデルを `adb push` する。PC で `start_relay.bat` を起動し、アプリに PC の IP を入れる
- [ ] **Step 3:** 利用者と一緒に確かめる:
  - 「翻訳」をオンにして「おはよう」 → 3 秒以内にペットの上に英訳
  - 相手を中国語にして話す → チャットボックスに中国語の訳
  - 英語で話す → かなの日本語訳がペットの上に
  - 機内モード + Wi-Fi（インターネットなし）でも訳せる
  - 画面を消して 5 分話しても、訳が出続け、遅くならない
  - 30 分続けても止まらない
- [ ] **Step 4:** 結果を設計書の末尾に追記する
- [ ] **Step 5:** Commit（`feat(android): own voice translated on-device end to end`）

---

## このあとの計画（今は書かない）

- 計画 2: PC の `translator_pc.exe`（C#。中継の移植、VRChat の音の取り出しと送信）
- 計画 3: 相手の声（受信、SenseVoice、4B、画面、自分の声を先にする順番待ち）
- 計画 4: Gemini への切り替えと API キーの欄、モデルのダウンロード、記録の共有、ライセンス画面、Termux 版の削除
