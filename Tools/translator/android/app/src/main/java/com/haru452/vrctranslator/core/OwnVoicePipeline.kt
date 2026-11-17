// Port of the App class in Tools/translator/phone/vrc_translator.py, for on-device recognition and translation.
package com.haru452.vrctranslator.core

import java.io.IOException
import java.time.LocalDateTime
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit

interface SpeechRecognizer {
    fun recognize(pcm: ShortArray): RecognizedSpeech
}

data class RecognizedSpeech(val lang: Lang, val text: String, val isSpeech: Boolean)

/**
 * Glue between the avatar's toggle, the microphone, recognition, translation and the display.
 * feedFrame() runs on the microphone thread, processNext() on one worker thread, onOsc() on the network thread.
 */
class OwnVoicePipeline(
    private val settings: TranslatorSettings,
    private val stt: SpeechRecognizer,
    private val translator: TextTranslator,
    private val display: Display,
    private val kana: KanaConverter,
    private val log: TranslationLog,
    private val clock: () -> Double,
    private val now: () -> LocalDateTime,
    private val say: (String) -> Unit,
) {
    companion object {
        const val ACTIVE = KatOsc.PARAM_PREFIX + "TranslatorActive"
        const val LOCAL_AI = KatOsc.PARAM_PREFIX + "TranslatorLocalAI"
        const val AVATAR_CHANGE = "/avatar/change"
        /** A recording older than this when its turn comes is skipped: the moment has passed. */
        const val STALE_SECONDS = 20.0
        const val SLOW_SECONDS = 15.0
    }

    private class Recording(val pcm: ShortArray, val generation: Int, val recordedAt: Double)

    private val lock = Any()
    private val segmenter = Segmenter(settings.segmenter)
    private val queue = LinkedBlockingQueue<Recording>()
    private var generation = 0  // bumped whenever listening stops, so older recordings are dropped
    private var levelMax = 0f
    private var levelSince = Double.NaN

    var active = false
        private set
    /** The avatar's "local AI" toggle; null until VRChat has told us. */
    var localAI: Boolean? = null
        private set
    /** Unknown counts as local, so speech never goes to the internet by surprise. */
    val useLocalAI: Boolean get() = localAI != false
    var onStatus: (String) -> Unit = {}

    // --- toggle -------------------------------------------------------------

    fun onOsc(address: String, args: List<Any>) {
        if (address == AVATAR_CHANGE) {
            setActive(false)  // a new avatar starts with the translator off
            localAI = null
            return
        }
        val value = args.firstOrNull()?.let(::truthy) ?: return
        when (address) {
            ACTIVE -> setActive(value)
            LOCAL_AI -> {
                if (localAI == value) return
                localAI = value
                if (!value) say("Gemini はまだありません。ローカル AI で訳します")
            }
        }
    }

    private fun truthy(v: Any): Boolean? = when (v) {
        is Boolean -> v
        is Int -> v != 0
        is Float -> v != 0f
        is Double -> v != 0.0
        else -> null
    }

    private fun setActive(on: Boolean) {
        synchronized(lock) {
            if (on == active) return
            active = on
            if (!on) {
                generation++
                segmenter.reset()
                display.stop()
            }
        }
        say(if (on) "翻訳: オン（聞いています）" else "翻訳: オフ")
    }

    private fun isCurrent(gen: Int) = synchronized(lock) { active && gen == generation }

    private fun ifCurrent(gen: Int, action: () -> Unit) = synchronized(lock) { if (active && gen == generation) action() }

    // --- audio and translation ------------------------------------------------

    fun feedFrame(frame: ShortArray) {
        var report: String? = null
        val item = synchronized(lock) {
            if (!active) return
            // Diagnostics: the loudest frame of the last 5 s against the voice threshold.
            val t = clock()
            if (levelSince.isNaN()) levelSince = t
            levelMax = maxOf(levelMax, Segmenter.rms(frame))
            if (t - levelSince >= 5.0) {
                report = "音量: 最大 ${levelMax.toInt()} / しきい値 ${segmenter.threshold.toInt()}" +
                    (if (segmenter.recording) "（録音中）" else "")
                levelMax = 0f
                levelSince = t
            }
            segmenter.feed(frame)?.let { Recording(it, generation, t) }
        }
        report?.let(say)
        if (item != null) {
            queue.put(item)
            say("区切り: %.1f 秒".format(item.pcm.size / Segmenter.RATE.toDouble()))
        }
    }

    /** Recognize and translate one queued utterance. False when there was nothing to do. */
    fun processNext(timeoutMs: Long = 500): Boolean {
        val item = queue.poll(timeoutMs, TimeUnit.MILLISECONDS) ?: return false
        if (clock() - item.recordedAt > STALE_SECONDS) {
            say("古い録音を飛ばしました（翻訳が混んでいます）")
            return true
        }
        try {
            process(item)
        } catch (e: Exception) {  // never let one bad utterance stop the translator
            say("予期しないエラー: ${e.javaClass.simpleName}")
            ifCurrent(item.generation) { display.error() }
        }
        return true
    }

    private fun process(item: Recording) {
        val gen = item.generation
        if (!isCurrent(gen)) return  // recorded before the toggle went off
        ifCurrent(gen) {
            display.targetIsChinese = settings.targetLang == Lang.ZH
            display.thinking()
        }

        val t0 = clock()
        val heard = stt.recognize(item.pcm)
        val recognizeSeconds = clock() - t0
        if (!heard.isSpeech || heard.lang !in listOf(Lang.JA, Lang.EN, Lang.ZH) || heard.text.isBlank()) {
            val why = if (!heard.isSpeech) "話し声ではない" else "言語 ${heard.lang}"
            say("出しません（$why）: ${heard.text}")
            ifCurrent(gen) { display.clear() }
            return
        }
        val source = heard.lang
        val target = if (source == Lang.JA) settings.targetLang else Lang.JA
        synchronized(lock) { display.targetIsChinese = target == Lang.ZH }

        val started = clock()
        val raw = try {
            translator.complete(TranslationPrompt.modelFor(source, target, settings.ownModel),
                TranslationPrompt.build(source, target, heard.text))
        } catch (e: Exception) {
            say("翻訳できませんでした: ${e.javaClass.simpleName}")  // the message itself is not shown
            ifCurrent(gen) { display.error() }
            return
        }
        val took = clock() - started
        val model = TranslationPrompt.modelFor(source, target, settings.ownModel)
        say("時間: 認識 %.1f 秒 / 翻訳 %.1f 秒（%s）/ 待ち %.1f 秒".format(recognizeSeconds, took, model, started - item.recordedAt - recognizeSeconds))
        if (took > SLOW_SECONDS) onStatus("遅くなっています（${took.toInt()} 秒）")

        val translation = TranslationPrompt.clean(raw)
        if (translation.isEmpty()) {
            say("出しません（訳が空）: $raw")
            ifCurrent(gen) { display.clear() }
            return
        }
        val result = TranslationResult(
            source, target, heard.text, translation,
            kanaOriginal = if (source == Lang.JA) kana.toKana(heard.text) else "",
            kanaTranslation = if (target == Lang.JA) kana.toKana(translation) else "",
        )
        try {
            log.append(now(), "self", source, heard.text, translation, "local")
        } catch (e: IOException) {
            say("記録できませんでした: ${e.javaClass.simpleName}")
        }
        say("${source.name}: ${heard.text} → $translation")
        ifCurrent(gen) { display.show(result) }
    }
}
