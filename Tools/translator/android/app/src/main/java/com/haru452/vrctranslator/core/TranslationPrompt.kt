package com.haru452.vrctranslator.core

/** The on-device model behind the translation ("1B" or "4B"). */
interface TextTranslator {
    fun complete(model: String, prompt: String): String
    fun cancel()
}

/** Gemma 3 prompts for one direction, and cleanup of what the model says around the translation. */
object TranslationPrompt {
    private fun name(lang: Lang) = when (lang) {
        Lang.JA -> "Japanese"
        Lang.EN -> "English"
        Lang.ZH -> "Simplified Chinese"
        else -> "English"
    }

    private val EXAMPLES = mapOf(
        (Lang.JA to Lang.EN) to listOf("そのアバターめっちゃかわいいね" to "Your avatar is so cute!", "明日も同じ時間にインするよ" to "I'll be on at the same time tomorrow."),
        (Lang.JA to Lang.ZH) to listOf("そのアバターめっちゃかわいいね" to "你的虚拟形象好可爱啊！", "明日も同じ時間にインするよ" to "明天我同一时间上线哦。"),
        (Lang.EN to Lang.JA) to listOf("Sorry, I didn't catch that." to "ごめん、聞き取れなかった。", "No worries, take your time." to "大丈夫、ゆっくりでいいよ。"),
        (Lang.ZH to Lang.JA) to listOf("没关系，慢慢来。" to "大丈夫、ゆっくりでいいよ。", "这里有点卡，我们换个世界吧。" to "ここちょっと重いね、別のワールドに行こう。"),
    )

    /** Same text before `text` for the same direction, so the runtime can reuse the evaluated prefix. */
    fun build(source: Lang, target: Lang, text: String): String {
        val examples = EXAMPLES[source to target].orEmpty().joinToString("\n") { (a, b) -> "$a => $b" }
        val instruction = "Translate the ${name(source)} line into natural, casual ${name(target)}, like friends chatting in VRChat. " +
            "\"Avatar\" (アバター) means a player's character in VRChat. " +
            "Reply with the translation only, on one line: no preamble, quotes, readings, romanization, emoji or alternatives.\n" +
            "Examples:\n$examples\n\nLine:\n"
        return "<start_of_turn>user\n$instruction$text<end_of_turn>\n<start_of_turn>model\n"
    }

    /** Chinese is too weak in 1B (it answered in English), so anything Chinese uses 4B. */
    fun modelFor(source: Lang, target: Lang, ownModel: String): String =
        if (source == Lang.ZH || target == Lang.ZH) "4B" else ownModel

    private val PREAMBLE = Regex("^(もちろん|Sure|Of course)[、,!！。.:：\\s]*", RegexOption.IGNORE_CASE)
    private val ALTERNATIVE = Regex("(或者|または|\\bOr:).*$")
    private val EMOJI = Regex("[\\p{So}\\p{Cs}\\uFE0F\\u200D]")
    private val ASCII_PARENS = Regex("\\s*[(（][\\x20-\\x7E]*[)）]")
    private val CJK = Regex("[\\p{IsHan}\\p{IsHiragana}\\p{IsKatakana}]")
    private val QUOTES = "\"“”「」『』'‘’"

    fun clean(raw: String): String {
        val lines = raw.lines().map { tidy(it) }.filter { it.isNotEmpty() }
        if (lines.isEmpty()) return ""
        // The first line that still says something once a preamble ("Sure!") is taken off.
        val chosen = lines.map { PREAMBLE.replace(it, "").trim() }.firstOrNull { it.isNotEmpty() } ?: lines.first()
        return tidy(chosen)
    }

    private fun tidy(line: String): String {
        var s = line.trim().removePrefix(">").trim()
        s = ALTERNATIVE.replace(s, "")
        s = EMOJI.replace(s, "")
        if (CJK.containsMatchIn(s)) s = ASCII_PARENS.replace(s, "")
        s = s.trim().trim { it in QUOTES }.trim()
        return s
    }
}
