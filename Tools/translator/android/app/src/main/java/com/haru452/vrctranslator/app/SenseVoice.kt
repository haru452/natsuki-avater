package com.haru452.vrctranslator.app

import com.haru452.vrctranslator.core.Lang
import com.haru452.vrctranslator.core.RecognizedSpeech
import com.haru452.vrctranslator.core.SpeechRecognizer
import com.k2fsa.sherpa.onnx.OfflineModelConfig
import com.k2fsa.sherpa.onnx.OfflineRecognizer
import com.k2fsa.sherpa.onnx.OfflineRecognizerConfig
import com.k2fsa.sherpa.onnx.OfflineSenseVoiceModelConfig

/** SenseVoice (sherpa-onnx). language "" detects it automatically; "ja", "en"... fixes it. */
class SenseVoice(modelPath: String, tokensPath: String, threads: Int = 2, language: String = "") : SpeechRecognizer, AutoCloseable {
    private val recognizer = OfflineRecognizer(
        null,
        OfflineRecognizerConfig(
            modelConfig = OfflineModelConfig(
                senseVoice = OfflineSenseVoiceModelConfig(model = modelPath, language = language, useInverseTextNormalization = true),
                tokens = tokensPath,
                numThreads = threads,
                provider = "cpu",
            ),
        ),
    )

    @Synchronized
    override fun recognize(pcm: ShortArray): RecognizedSpeech {
        val samples = FloatArray(pcm.size) { pcm[it] / 32768f }
        val stream = recognizer.createStream()
        try {
            stream.acceptWaveform(samples, 16000)
            recognizer.decode(stream)
            val r = recognizer.getResult(stream)
            return toSpeech(r.lang, r.event, r.text)
        } finally {
            stream.release()
        }
    }

    override fun close() = recognizer.release()
}

/** Silence came back as "그." in testing: one letter (ignoring punctuation) is not speech. */
internal fun toSpeech(lang: String, event: String, text: String): RecognizedSpeech {
    val letters = text.count { Character.isLetterOrDigit(it) }
    val isSpeech = (event.isEmpty() || event.contains("Speech")) && letters > 1
    return RecognizedSpeech(Lang.parse(lang), text.trim(), isSpeech)
}
