package com.haru452.vrctranslator.core

enum class Lang {
    NONE, JA, EN, ZH, OTHER;

    companion object {
        /** "ja", "en", "zh" or SenseVoice's "<|ja|>" form. Empty is NONE; anything else (ko, yue...) is OTHER. */
        fun parse(code: String): Lang = when (code.trim().removePrefix("<|").removeSuffix("|>").lowercase()) {
            "" -> NONE
            "ja" -> JA
            "en" -> EN
            "zh" -> ZH
            else -> OTHER
        }
    }
}

/** One translation. kanaOriginal / kanaTranslation are the Japanese side written in kana only (for KAT). */
data class TranslationResult(
    val source: Lang,
    val target: Lang,
    val original: String,
    val translation: String,
    val kanaOriginal: String = "",
    val kanaTranslation: String = "",
)

enum class DisplayMode { KAT, CHATBOX, BOTH }
