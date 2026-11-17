package com.haru452.vrctranslator.core

import com.atilika.kuromoji.ipadic.Tokenizer

/** Japanese text to something KAT can show: kanji become hiragana readings, everything else stays. */
interface KanaConverter {
    fun toKana(japanese: String): String
}

/** Kuromoji with the bundled IPADIC (Apache 2.0). The first call loads the dictionary (about a second). */
class KuromojiKana : KanaConverter {
    private val tokenizer by lazy { Tokenizer() }

    override fun toKana(japanese: String): String {
        if (japanese.isEmpty()) return ""
        val sb = StringBuilder()
        for (token in tokenizer.tokenize(japanese)) {
            val surface = token.surface
            val reading = token.reading
            if (surface.any(::isKanji) && reading != null && reading != "*") sb.append(toHiragana(reading))
            else sb.append(surface)
        }
        return keepShowable(sb.toString())
    }

    private fun isKanji(c: Char) = Character.UnicodeScript.of(c.code) == Character.UnicodeScript.HAN

    private fun toHiragana(katakana: String) = buildString {
        for (c in katakana) append(if (c in 'ァ'..'ヶ') c - 0x60 else c)
    }

    /** Drops characters KAT would show as '?' (unknown kanji, emoji), but keeps a real '?'. */
    private fun keepShowable(text: String): String {
        val unknown = KatOsc.encodeText("?").single()
        return KatOsc.chars(text).filter { it == "?" || unknown !in KatOsc.encodeText(it) }.joinToString("")
    }
}
