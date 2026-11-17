package com.haru452.vrctranslator.app

import com.haru452.vrctranslator.core.Lang
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SenseVoiceRulesTests {
    @Test fun speechWithWordsIsSpeech() {
        val s = toSpeech("<|ja|>", "<|Speech|>", " おはよう。")
        assertTrue(s.isSpeech)
        assertEquals(Lang.JA, s.lang)
        assertEquals("おはよう。", s.text)
    }

    @Test fun oneLetterFromSilenceIsNotSpeech() = assertFalse(toSpeech("<|ko|>", "<|Speech|>", "그.").isSpeech)

    @Test fun musicIsNotSpeech() = assertFalse(toSpeech("<|en|>", "<|BGM|>", "The music plays on.").isSpeech)
}
