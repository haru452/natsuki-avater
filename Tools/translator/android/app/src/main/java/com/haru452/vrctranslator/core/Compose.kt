// Port of compose_kat / compose_chatbox / paginate in Tools/translator/phone/display.py.
// KAT cannot show kanji, so the pet gets kana-only Japanese; the chatbox gets kanji.
package com.haru452.vrctranslator.core

object Compose {
    const val ROW = 32
    const val CHATBOX_LIMIT = 144

    /**
     * Japanese speech: translation, then (kana original) on the next row.
     * English speech: kana translation, then (English original). Chinese speech: kana translation only.
     */
    fun kat(r: TranslationResult, showOriginal: Boolean): String {
        val (main, original) = when (r.source) {
            Lang.JA -> r.translation to r.kanaOriginal
            Lang.ZH -> r.kanaTranslation to ""
            else -> r.kanaTranslation to r.original
        }
        if (!showOriginal || original.isEmpty()) return main
        val pad = Math.floorMod(-KatOsc.cells(main), ROW)
        return main + " ".repeat(pad) + "(" + original + ")"
    }

    fun chatbox(r: TranslationResult, showOriginal: Boolean): String {
        val text = r.translation
        if (showOriginal && r.original.isNotEmpty()) {
            val withOriginal = text + "\n(" + r.original + ")"
            if (length(withOriginal) <= CHATBOX_LIMIT) return withOriginal
        }
        if (length(text) <= CHATBOX_LIMIT) return text
        return KatOsc.chars(text).take(CHATBOX_LIMIT - 1).joinToString("") + "…"
    }

    private fun length(s: String) = s.codePointCount(0, s.length)

    /** Pages of at most `limit` KAT cells, never splitting a character, preferring to break at a space. */
    fun paginate(text: String, limit: Int = KatOsc.TEXT_LIMIT, wordBacktrack: Int = 20): List<String> {
        val pages = ArrayList<String>()
        var page = ""
        var used = 0
        for (ch in KatOsc.chars(text)) {
            val width = KatOsc.charWidth(ch)
            if (used + width > limit) {
                val cut = page.lastIndexOf(' ')
                if (ch != " " && cut > 0 && page.length - cut <= wordBacktrack) {
                    pages.add(page.substring(0, cut))
                    page = page.substring(cut + 1)
                } else {
                    pages.add(page)
                    page = ""
                }
                page = page.trimStart(' ')
                used = KatOsc.cells(page)
                if (ch == " " && page.isEmpty()) continue
            }
            page += ch
            used += width
        }
        if (page.isNotEmpty() || pages.isEmpty()) pages.add(page)
        return pages
    }
}
