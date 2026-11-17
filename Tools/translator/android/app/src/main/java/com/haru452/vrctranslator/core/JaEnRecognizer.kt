package com.haru452.vrctranslator.core

/**
 * The wearer speaks only Japanese or English. SenseVoice's automatic language guess sometimes calls short
 * Japanese Chinese, Korean or a single English word ("好嘞。", "也希요。", "The."), which garbles the text.
 * Keep Japanese and real English sentences (two or more words, no kana/kanji); hear anything else again
 * with the recognizer fixed to Japanese. Recognition takes about 0.1 s, so asking twice is cheap.
 */
class JaEnRecognizer(private val auto: SpeechRecognizer, private val japanese: SpeechRecognizer) : SpeechRecognizer {
    override fun recognize(pcm: ShortArray): RecognizedSpeech {
        val first = auto.recognize(pcm)
        return if (first.lang == Lang.JA || isEnglishSentence(first)) first else japanese.recognize(pcm)
    }

    private fun isEnglishSentence(r: RecognizedSpeech): Boolean {
        if (r.lang != Lang.EN) return false
        if (r.text.any { Character.UnicodeScript.of(it.code) in NON_LATIN }) return false
        return r.text.split(Regex("[^A-Za-z']+")).count { it.isNotEmpty() } >= 2
    }

    private companion object {
        val NON_LATIN = setOf(Character.UnicodeScript.HAN, Character.UnicodeScript.HIRAGANA,
            Character.UnicodeScript.KATAKANA, Character.UnicodeScript.HANGUL)
    }
}
