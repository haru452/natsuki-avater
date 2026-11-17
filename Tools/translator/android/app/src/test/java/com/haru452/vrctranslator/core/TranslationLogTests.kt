package com.haru452.vrctranslator.core

import java.io.File
import java.time.LocalDateTime
import org.junit.Assert.assertEquals
import org.junit.Test

class TranslationLogTests {
    @Test fun tabsAndNewlinesStayOnOneLine() {
        val line = TranslationLog.formatLine(LocalDateTime.of(2026, 11, 16, 22, 0, 5), "self", Lang.JA,
            "おは\tよう\nございます", "Good\nmorning", "local")
        assertEquals("2026-11-16 22:00:05\tself\tja\tおは よう ございます\tGood morning\tlocal", line)
        assertEquals(6, line.split("\t").size)
    }

    @Test fun appendsOneLinePerTranslation() {
        val f = File.createTempFile("translog", ".tsv").also { it.delete(); it.deleteOnExit() }
        val log = TranslationLog(File(f.parentFile, f.name.removeSuffix(".tsv") + "/sub/log.tsv"))
        log.append(LocalDateTime.of(2026, 1, 1, 0, 0), "self", Lang.EN, "hi", "やあ", "local")
        log.append(LocalDateTime.of(2026, 1, 1, 0, 1), "self", Lang.EN, "bye", "またね", "local")
        assertEquals(2, log.file.readLines().size)
    }
}
