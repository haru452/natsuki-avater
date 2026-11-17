// Ported from AppTests / ReviewFixTests in Tools/translator/phone/test_app.py, plus the on-device cases.
package com.haru452.vrctranslator.core

import java.io.File
import java.time.LocalDateTime
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

private const val ACTIVE = "/avatar/parameters/TranslatorActive"
private const val LOCAL_AI = "/avatar/parameters/TranslatorLocalAI"

// Speech, then enough silence (over SegmenterSettings.endSilenceS = 1.2 s) to end the sentence.
private val SENTENCE = frames(1.0, 60) + frames(1.0, 6000) + frames(1.5, 60)

class FakeDisplay : Display {
    val calls = ArrayList<String>()
    val shown = ArrayList<TranslationResult>()
    override var targetIsChinese = false
    override fun thinking() { calls.add("thinking") }
    override fun show(r: TranslationResult) { calls.add("show"); shown.add(r) }
    override fun error() { calls.add("error") }
    override fun clear() { calls.add("clear") }
    override fun stop() { calls.add("stop") }
    override fun tick() {}
}

class FakeStt(vararg heard: RecognizedSpeech) : SpeechRecognizer {
    private val queue = ArrayDeque(heard.toList())
    override fun recognize(pcm: ShortArray) = queue.removeFirstOrNull() ?: RecognizedSpeech(Lang.JA, "おはよう", true)
}

class FakeTranslator(vararg outcomes: Any) : TextTranslator {
    private val queue = ArrayDeque(outcomes.toList())
    val calls = ArrayList<Pair<String, String>>()
    var before: (() -> Unit)? = null
    override fun complete(model: String, prompt: String): String {
        calls.add(model to prompt)
        before?.invoke()
        val o = queue.removeFirstOrNull() ?: "Good morning"
        if (o is Exception) throw o
        return o as String
    }
    override fun cancel() {}
}

class OwnVoicePipelineTests {
    private var now = 1000.0
    private val said = ArrayList<String>()
    private val status = ArrayList<String>()
    private val logFile: File = File.createTempFile("translog", ".tsv").also { it.delete(); it.deleteOnExit() }

    private fun make(stt: SpeechRecognizer = FakeStt(), translator: TextTranslator = FakeTranslator(),
                     settings: TranslatorSettings = TranslatorSettings()): Pair<OwnVoicePipeline, FakeDisplay> {
        val disp = FakeDisplay()
        val p = OwnVoicePipeline(settings, stt, translator, disp, object : KanaConverter {
            override fun toKana(japanese: String) = "kana($japanese)"
        }, TranslationLog(logFile), { now }, { LocalDateTime.of(2026, 11, 16, 22, 0, 0) }, { said.add(it) })
        p.onStatus = { status.add(it) }
        return p to disp
    }

    private fun speak(p: OwnVoicePipeline, chunks: List<ShortArray> = SENTENCE) = chunks.forEach(p::feedFrame)
    private fun drain(p: OwnVoicePipeline) { while (p.processNext(0)) Unit }
    private fun logLines() = if (logFile.exists()) logFile.readLines() else emptyList()

    @Test fun onSpeechTranslatesShowsAndLogs() {
        val (p, disp) = make()
        p.onOsc(ACTIVE, listOf(true))
        speak(p)
        drain(p)
        assertEquals(listOf("thinking", "show"), disp.calls)
        assertEquals(TranslationResult(Lang.JA, Lang.EN, "おはよう", "Good morning", kanaOriginal = "kana(おはよう)"), disp.shown[0])
        assertEquals(listOf("2026-11-16 22:00:00\tself\tja\tおはよう\tGood morning\tlocal"), logLines())
    }

    @Test fun nothingHappensWhileOff() {
        val t = FakeTranslator()
        val (p, disp) = make(translator = t)
        speak(p)
        drain(p)
        assertEquals(0, t.calls.size)
        assertTrue(disp.calls.isEmpty())
        assertTrue(logLines().isEmpty())
    }

    @Test fun turningOffMidTranslationLogsButDoesNotShow() {
        val t = FakeTranslator()
        val (p, disp) = make(translator = t)
        p.onOsc(ACTIVE, listOf(true))
        speak(p)
        t.before = { p.onOsc(ACTIVE, listOf(false)) }
        drain(p)
        assertFalse("show" in disp.calls)
        assertEquals(1, logLines().size)
    }

    @Test fun recordingsQueuedBeforeTurningOffAreDropped() {
        val t = FakeTranslator()
        val (p, _) = make(translator = t)
        p.onOsc(ACTIVE, listOf(true))
        speak(p)
        p.onOsc(ACTIVE, listOf(false))
        drain(p)
        assertEquals(0, t.calls.size)
    }

    @Test fun halfSpokenSentenceIsDiscardedOnOff() {
        val t = FakeTranslator()
        val (p, _) = make(translator = t)
        p.onOsc(ACTIVE, listOf(true))
        speak(p, frames(1.0, 60) + frames(1.0, 6000))
        p.onOsc(ACTIVE, listOf(false))
        p.onOsc(ACTIVE, listOf(true))
        speak(p, frames(1.0, 60))
        drain(p)
        assertEquals(0, t.calls.size)
    }

    @Test fun avatarChangeMeansOff() {
        val t = FakeTranslator()
        val (p, _) = make(translator = t)
        p.onOsc(ACTIVE, listOf(true))
        p.onOsc("/avatar/change", listOf("avtr_x"))
        assertFalse(p.active)
        speak(p)
        drain(p)
        assertEquals(0, t.calls.size)
    }

    @Test fun errorShowsErrorAndKeepsGoing() {
        val (p, disp) = make(translator = FakeTranslator(IllegalStateException("decode failed"), "Good morning"))
        p.onOsc(ACTIVE, listOf(true))
        speak(p)
        speak(p)
        drain(p)
        assertEquals(listOf("thinking", "error", "thinking", "show"), disp.calls)
    }

    @Test fun unexpectedErrorMessageIsNotPrinted() {
        val (p, _) = make(translator = FakeTranslator(RuntimeException("SECRET in here")))
        p.onOsc(ACTIVE, listOf(true))
        speak(p)
        drain(p)
        assertTrue(said.isNotEmpty())
        assertFalse(said.any { "SECRET" in it })
    }

    @Test fun badOscValuesAreIgnored() {
        val (p, _) = make()
        p.onOsc(ACTIVE, emptyList())
        p.onOsc("/unknown", listOf(1))
        p.onOsc(ACTIVE, listOf("yes"))
        assertFalse(p.active)
    }

    @Test fun intOrFloatToggleValuesWork() {
        val (p, _) = make()
        p.onOsc(ACTIVE, listOf(1))
        assertTrue(p.active)
        p.onOsc(ACTIVE, listOf(0.0f))
        assertFalse(p.active)
    }

    @Test fun staleRecordingsAreSkipped() {
        val t = FakeTranslator()
        val (p, _) = make(translator = t)
        p.onOsc(ACTIVE, listOf(true))
        speak(p)
        now += OwnVoicePipeline.STALE_SECONDS + 1
        speak(p)
        drain(p)
        assertEquals("the old one is dropped, the fresh one is translated", 1, t.calls.size)
    }

    @Test fun geminiMessageOnlyWhenSwitchedOff() {
        val (p, _) = make()
        repeat(3) { p.onOsc(LOCAL_AI, listOf(false)) }
        p.onOsc(LOCAL_AI, listOf(true))
        assertEquals(1, said.count { "Gemini" in it })
    }

    @Test fun japaneseToChineseUses4B() {
        val t = FakeTranslator("你好")
        val (p, disp) = make(stt = FakeStt(RecognizedSpeech(Lang.JA, "こんにちは", true)), translator = t,
            settings = TranslatorSettings(targetLang = Lang.ZH))
        p.onOsc(ACTIVE, listOf(true))
        speak(p)
        drain(p)
        assertEquals("4B" to TranslationPrompt.build(Lang.JA, Lang.ZH, "こんにちは"), t.calls.single())
        assertEquals(Lang.ZH, disp.shown.single().target)
        assertTrue(disp.targetIsChinese)
    }

    @Test fun englishSpeechGetsKanaTranslation() {
        val (p, disp) = make(stt = FakeStt(RecognizedSpeech(Lang.EN, "thank you", true)), translator = FakeTranslator("ありがとう"))
        p.onOsc(ACTIVE, listOf(true))
        speak(p)
        drain(p)
        assertEquals(TranslationResult(Lang.EN, Lang.JA, "thank you", "ありがとう", kanaTranslation = "kana(ありがとう)"), disp.shown.single())
    }

    @Test fun cleanedTranslationIsShown() {
        val (p, disp) = make(translator = FakeTranslator("Sure! Good morning 😊"))
        p.onOsc(ACTIVE, listOf(true))
        speak(p)
        drain(p)
        assertEquals("Good morning", disp.shown.single().translation)
    }

    @Test fun unsupportedLanguageClearsDots() {
        val t = FakeTranslator()
        val (p, disp) = make(stt = FakeStt(RecognizedSpeech(Lang.OTHER, "안녕하세요", true)), translator = t)
        p.onOsc(ACTIVE, listOf(true))
        speak(p)
        drain(p)
        assertEquals(listOf("thinking", "clear"), disp.calls)
        assertEquals(0, t.calls.size)
        assertTrue(logLines().isEmpty())
    }

    @Test fun nonSpeechIsDropped() {
        val t = FakeTranslator()
        val (p, disp) = make(stt = FakeStt(RecognizedSpeech(Lang.JA, "", false)), translator = t)
        p.onOsc(ACTIVE, listOf(true))
        speak(p)
        drain(p)
        assertEquals(listOf("thinking", "clear"), disp.calls)
        assertEquals(0, t.calls.size)
    }

    @Test fun everyDroppedUtteranceIsExplained() {
        val (p, _) = make(stt = FakeStt(RecognizedSpeech(Lang.JA, "え", false), RecognizedSpeech(Lang.OTHER, "안녕", true)),
            translator = FakeTranslator())
        p.onOsc(ACTIVE, listOf(true))
        speak(p)
        speak(p)
        drain(p)
        assertEquals(2, said.count { it.startsWith("区切り") })
        assertTrue(said.any { it.startsWith("出しません") && "え" in it })
        assertTrue(said.any { it.startsWith("出しません") && "안녕" in it })
    }

    @Test fun micLevelIsReportedEveryFiveSeconds() {
        val (p, _) = make()
        p.onOsc(ACTIVE, listOf(true))
        for (f in frames(6.0, 300)) { p.feedFrame(f); now += 0.03 }
        val levels = said.filter { it.startsWith("音量") }
        assertEquals(1, levels.size)
        assertTrue(levels[0], "最大 213" in levels[0] && "しきい値" in levels[0])
    }

    @Test fun stageTimesAreReported() {
        val (p, _) = make()
        p.onOsc(ACTIVE, listOf(true))
        speak(p)
        drain(p)
        assertTrue(said.any { it.startsWith("時間: 認識") && "翻訳" in it && "1B" in it })
    }

    @Test fun localAIUnknownMeansLocal() {
        val (p, _) = make()
        assertTrue(p.useLocalAI)
        p.onOsc(LOCAL_AI, listOf(false))
        assertFalse(p.useLocalAI)
        p.onOsc("/avatar/change", listOf("avtr_y"))
        assertTrue("a new avatar's value is unknown again", p.useLocalAI)
    }

    @Test fun slowTranslationIsReported() {
        val t = FakeTranslator()
        t.before = { now += 16 }
        val (p, _) = make(translator = t)
        p.onOsc(ACTIVE, listOf(true))
        speak(p)
        drain(p)
        assertEquals(1, status.count { "遅くなっています" in it })
    }
}
