package com.haru452.vrctranslator.core

import org.junit.Assert.assertEquals
import org.junit.Test

class OwnLanguageTests {
    private class Fake(val answer: RecognizedSpeech) : SpeechRecognizer {
        var calls = 0
        override fun recognize(pcm: ShortArray): RecognizedSpeech { calls++; return answer }
    }

    private val pcm = ShortArray(16000)
    private val japanese = Fake(RecognizedSpeech(Lang.JA, "音声入力の質が良くない。", true))

    private fun heard(lang: Lang, text: String) = JaEnRecognizer(Fake(RecognizedSpeech(lang, text, true)), japanese).recognize(pcm)

    @Test fun japaneseIsKept() {
        assertEquals("できてないというか。", heard(Lang.JA, "できてないというか。").text)
        assertEquals(0, japanese.calls)
    }

    @Test fun englishSentenceIsKept() = assertEquals(Lang.EN, heard(Lang.EN, "Where are you from?").lang)

    // Seen on the device: short Japanese came back as Chinese, Korean or one English word.
    @Test fun otherLanguagesAreHeardAgainAsJapanese() {
        for ((lang, text) in listOf(Lang.ZH to "好嘞。", Lang.OTHER to "也希요。", Lang.EN to "The.", Lang.EN to "嗯なか。"))
            assertEquals("$lang $text", "音声入力の質が良くない。", heard(lang, text).text)
        assertEquals(4, japanese.calls)
    }
}
