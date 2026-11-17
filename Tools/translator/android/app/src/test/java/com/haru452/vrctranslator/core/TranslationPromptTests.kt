package com.haru452.vrctranslator.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class TranslationPromptTests {
    private fun prefix(p: String, text: String) = p.substring(0, p.lastIndexOf(text))

    @Test fun buildEndsWithModelTurn() {
        val p = TranslationPrompt.build(Lang.JA, Lang.EN, "こんにちは")
        assertTrue(p.startsWith("<start_of_turn>user\n"))
        assertTrue(p.endsWith("こんにちは<end_of_turn>\n<start_of_turn>model\n"))
    }

    @Test fun buildPrefixIsStablePerDirection() {
        val dirs = listOf(Lang.JA to Lang.EN, Lang.JA to Lang.ZH, Lang.EN to Lang.JA, Lang.ZH to Lang.JA)
        val prefixes = dirs.map { (s, t) ->
            val a = prefix(TranslationPrompt.build(s, t, "aaa"), "aaa")
            assertEquals(a, prefix(TranslationPrompt.build(s, t, "bbb"), "bbb"))
            a
        }
        assertEquals(4, prefixes.toSet().size)
    }

    @Test fun chineseAlwaysUses4B() {
        assertEquals("4B", TranslationPrompt.modelFor(Lang.JA, Lang.ZH, "1B"))
        assertEquals("4B", TranslationPrompt.modelFor(Lang.ZH, Lang.JA, "1B"))
        assertEquals("1B", TranslationPrompt.modelFor(Lang.JA, Lang.EN, "1B"))
        assertEquals("4B", TranslationPrompt.modelFor(Lang.EN, Lang.JA, "4B"))
    }

    @Test fun cleanRemovesWhatIsNotTheTranslation() {
        val cases = listOf(
            "もちろん、もう一度言ってくれる？" to "もう一度言ってくれる？",
            "Sure! Let's take some pictures!" to "Let's take some pictures!",
            "“That avatar is so cute, right?”" to "That avatar is so cute, right?",
            "> Yeah, same time tomorrow!" to "Yeah, same time tomorrow!",
            "何してるの？ (Nani shiteru no?)" to "何してるの？",
            "笑いすぎ！(Wai sugi!) 🤣" to "笑いすぎ！",
            "好可爱啊！ 😊   或者：  厉害了，连你都爱了！ 😉" to "好可爱啊！",
            "もちろん！\nそれ、めっちゃ可愛いね！" to "それ、めっちゃ可愛いね！",
            "Hello\n\nThis means a greeting." to "Hello",
            "  " to "",
        )
        for ((raw, want) in cases) assertEquals(raw, want, TranslationPrompt.clean(raw))
    }

    @Test fun cleanKeepsEnglishParentheses() =
        assertEquals("I'm fine (really).", TranslationPrompt.clean("I'm fine (really)."))

    @Test fun promptsDiffer() = assertNotEquals(
        TranslationPrompt.build(Lang.JA, Lang.EN, "x"), TranslationPrompt.build(Lang.EN, Lang.JA, "x"))
}
