package com.haru452.vrctranslator.core

import java.io.File
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter

data class TranslatorSettings(
    var pcIp: String = "",
    var relayPort: Int = 9002,
    var listenPort: Int = 9001,
    var showOriginal: Boolean = true,
    var display: DisplayMode = DisplayMode.KAT,
    var katSync: Int = 8,
    /** What your Japanese is translated into: EN or ZH (the switch on the main screen). */
    var targetLang: Lang = Lang.EN,
    var ownModel: String = "1B",
    var otherModel: String = "4B",
    var segmenter: SegmenterSettings = SegmenterSettings(),
)

/** Review log: one tab-separated line per translation. Never holds audio or keys. */
class TranslationLog(val file: File) {
    companion object {
        private val TIME = DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm:ss")
        private fun clean(text: String) = text.split(Regex("\\s+")).filter { it.isNotEmpty() }.joinToString(" ")

        fun formatLine(whenTime: LocalDateTime, who: String, lang: Lang, original: String, translation: String, engine: String) =
            listOf(whenTime.format(TIME), who, lang.name.lowercase(), original, translation, engine).joinToString("\t") { clean(it) }
    }

    @Synchronized
    fun append(whenTime: LocalDateTime, who: String, lang: Lang, original: String, translation: String, engine: String) {
        file.parentFile?.mkdirs()
        file.appendText(formatLine(whenTime, who, lang, original, translation, engine) + "\n", Charsets.UTF_8)
    }
}
