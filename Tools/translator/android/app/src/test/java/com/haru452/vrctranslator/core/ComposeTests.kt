// Ported from the ComposeTests and PaginateTests of Tools/translator/phone/test_display.py, plus Chinese cases.
package com.haru452.vrctranslator.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** Python's Result(lang, original, translation, kana): kana is the Japanese side. */
internal fun r(lang: String, original: String, translation: String, kana: String) =
    if (lang == "ja") TranslationResult(Lang.JA, Lang.EN, original, translation, kanaOriginal = kana)
    else TranslationResult(Lang.parse(lang), Lang.JA, original, translation, kanaTranslation = kana)

internal val JA_RESULT = r("ja", "おはようございます", "Good morning", "おはようございます")
internal val EN_RESULT = r("en", "Good morning", "おはよう", "おはよう")

class ComposeTests {
    @Test fun japaneseSpeechShowsEnglishThenKanaOriginal() {
        val text = Compose.kat(JA_RESULT, true)
        assertTrue(text.startsWith("Good morning"))
        assertEquals("original starts on the next row", 32, text.indexOf('('))
        assertTrue(text.endsWith("(おはようございます)"))
    }

    @Test fun englishSpeechShowsKanaThenEnglishOriginal() {
        val text = Compose.kat(EN_RESULT, true)
        assertTrue(text.startsWith("おはよう"))
        assertTrue(text.trimEnd().endsWith("(Good morning)"))
        assertEquals(32, KatOsc.cells(text.substring(0, text.indexOf('('))))
    }

    @Test fun withoutOriginal() = assertEquals("Good morning", Compose.kat(JA_RESULT, false))

    @Test fun rowPaddingCountsVoicedKatakanaAsTwoCells() {
        val text = Compose.kat(r("en", "Game", "ゲーム", "ゲーム"), true)
        assertEquals(32, KatOsc.cells(text.substring(0, text.indexOf('('))))
    }

    @Test fun mainLineExactlyOneRowNeedsNoPadding() =
        assertEquals("a".repeat(32) + "(x)", Compose.kat(r("ja", "x", "a".repeat(32), "x"), true))

    @Test fun chatboxUsesKanjiAndFits144() {
        assertEquals("おはよう、元気？\n(Good morning)", Compose.chatbox(r("en", "Good morning", "おはよう、元気？", "おはよう、げんき？"), true))
        assertEquals("drops the original first", "b".repeat(100),
            Compose.chatbox(r("ja", "あ".repeat(100), "b".repeat(100), "あ".repeat(100)), true))
        val out = Compose.chatbox(r("ja", "x", "c".repeat(200), "x"), true)
        assertEquals(144, out.length)
        assertTrue(out.endsWith("…"))
    }

    @Test fun zhSpeechShowsKanaWithoutOriginal() {
        val res = TranslationResult(Lang.ZH, Lang.JA, "谢谢", "ありがとう", kanaTranslation = "ありがとう")
        assertEquals("ありがとう", Compose.kat(res, true))
    }

    @Test fun emptyKanaOriginalDropsTheBracket() {
        val res = TranslationResult(Lang.JA, Lang.EN, "東京", "Tokyo", kanaOriginal = "")
        assertEquals("Tokyo", Compose.kat(res, true))
    }

    @Test fun langParsesSenseVoiceTags() {
        assertEquals(Lang.JA, Lang.parse("<|ja|>"))
        assertEquals(Lang.ZH, Lang.parse("zh"))
        assertEquals(Lang.OTHER, Lang.parse("<|ko|>"))
        assertEquals(Lang.NONE, Lang.parse(""))
    }

    @Test fun shortTextIsOnePage() = assertEquals(listOf("hello"), Compose.paginate("hello"))

    @Test fun longTextBreaksBetweenWords() {
        val text = List(40) { "word" }.joinToString(" ")  // 199 characters
        val pages = Compose.paginate(text)
        assertEquals(2, pages.size)
        assertTrue(pages.all { KatOsc.cells(it) <= 128 })
        assertFalse("no word split", pages[0].endsWith("wor"))
        assertEquals(text.split(" "), pages.joinToString(" ") { it.trim() }.split(" "))
    }

    @Test fun neverSplitsAVoicedKatakana() {
        val pages = Compose.paginate("ア".repeat(127) + "ガ" + "イ")
        assertEquals("ア".repeat(127), pages[0])
        assertTrue(pages[1].startsWith("ガ"))
    }
}
