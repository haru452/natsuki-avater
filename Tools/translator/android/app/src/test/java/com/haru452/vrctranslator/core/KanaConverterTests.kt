package com.haru452.vrctranslator.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test

class KanaConverterTests {
    companion object {
        private val kana = KuromojiKana()  // loading the dictionary takes a moment; share it
    }

    @Test fun kanjiBecomeHiragana() = assertEquals("きょうはさむいね", kana.toKana("今日は寒いね"))

    @Test fun katakanaStays() = assertEquals("マイクのちょうしがわるい", kana.toKana("マイクの調子が悪い"))

    @Test fun latinAndSymbolsStay() = assertEquals("VRChatであおう!", kana.toKana("VRChatで会おう!"))

    @Test fun emptyStaysEmpty() = assertEquals("", kana.toKana(""))

    @Test fun nothingBecomesQuestionMarkOnKat() {
        val unknown = KatOsc.encodeText("?").single()
        val out = kana.toKana("東京タワーに行った😊")
        assertFalse(out, unknown in KatOsc.encodeText(out))
    }
}
