// Ported from Tools/translator/test_kat_osc.py (same cases, same values). send() is not ported: KatDisplay paces the groups.
package com.haru452.vrctranslator.core

import java.nio.ByteBuffer
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class KatOscTests {
    private fun e(s: String) = KatOsc.encodeText(s)
    private fun b(vararg parts: Any): ByteArray =
        parts.flatMap { p -> if (p is String) p.map { it.code.toByte() } else (p as ByteArray).toList() }.toByteArray()
    private fun bigEndian(f: Float) = ByteBuffer.allocate(4).putFloat(f).array()

    @Test fun asciiCodes() {
        assertEquals(listOf(0), e(" "))
        assertEquals(listOf(33), e("A"))
        assertEquals(listOf(94), e("~"))
    }

    @Test fun kanaCodes() {
        assertEquals(listOf(127), e("ぬ"))
        assertEquals(listOf(129), e("ふ"))
        assertEquals(listOf(133, 154, 136, 131), e("おはよう"))
        assertEquals(listOf(208), e("ヌ"))
        assertEquals(listOf(253), e("ロ"))
    }

    @Test fun unknownCharBecomesQuestion() {
        assertEquals(e("?"), e("漢"))
        assertEquals(e("?"), e("😀"))
    }

    @Test fun missingKanaAreSubstituted() {
        assertEquals(e("つ"), e("っ"))
        assertEquals(e("ツ"), e("ッ"))
        assertEquals(e("-"), e("ー"))
        assertEquals(e(","), e("、"))
        assertEquals(e("ヤ"), e("ャ"))
    }

    @Test fun voicedKatakanaUsesSeparateMark() {
        assertEquals(e("カ") + 254, e("ガ"))
        assertEquals(e("ハ") + 255, e("パ"))
    }

    @Test fun fullwidthBecomesHalfwidth() = assertEquals(e("A1!"), e("Ａ１！"))

    @Test fun neverEmitsUnsendableCode() {
        val codes = e((0x20 until 0x3100).map { it.toChar() }.joinToString(""))
        assertFalse(128 in codes)
        assertTrue(codes.all { it in 0..255 })
    }

    @Test fun truncatesAtLimit() {
        assertEquals(128, e("x".repeat(300)).size)
        assertEquals(128, e("ガ".repeat(100)).size)
    }

    @Test fun newlineAndTabBecomeSpace() = assertEquals(e("a b c"), e("a\nb\tc"))

    @Test fun floatMapping() {
        assertEquals(0f, KatOsc.codeToFloat(0), 1e-6f)
        assertEquals(1f, KatOsc.codeToFloat(127), 1e-6f)
        assertEquals(-1f, KatOsc.codeToFloat(129), 1e-6f)
        assertEquals(-1f / 127f, KatOsc.codeToFloat(255), 1e-6f)
    }

    @Test fun oscMessageLayout() {
        assertArrayEquals(b("/a\u0000\u0000,i\u0000\u0000\u0000\u0000\u0000\u0001"), KatOsc.oscMessage("/a", 1))
        assertArrayEquals(b("/a\u0000\u0000,T\u0000\u0000"), KatOsc.oscMessage("/a", true))
        assertArrayEquals(b("/a\u0000\u0000,F\u0000\u0000"), KatOsc.oscMessage("/a", false))
        assertArrayEquals(b("/a\u0000\u0000,f\u0000\u0000", bigEndian(0.5f)), KatOsc.oscMessage("/a", 0.5f))
    }

    @Test fun addressPaddingIsMultipleOfFour() =
        assertArrayEquals(b("/abc\u0000\u0000\u0000\u0000,T\u0000\u0000"), KatOsc.oscMessage("/abc", true))

    @Test fun roundTripEachType() {
        for (value in listOf<Any>(true, false, 7, -3, 0.5f, "おはよう Hi")) {
            val (address, args) = KatOsc.parseMessage(KatOsc.oscMessage("/a/b", value))
            assertEquals("/a/b", address)
            assertEquals(listOf(value), args)
        }
    }

    @Test fun severalArguments() =
        assertEquals("/chatbox/input" to listOf<Any>("hello", true, false),
            KatOsc.parseMessage(KatOsc.oscMessage("/chatbox/input", "hello", true, false)))

    @Test fun brokenPacketsRaise() {
        val cases = listOf(
            b(""), b("/a"), b("/a\u0000\u0000"), b("/a\u0000\u0000,i\u0000\u0000\u0000\u0001"), b("/a\u0000\u0000,x\u0000\u0000"),
            byteArrayOf(0xff.toByte(), 0xfe.toByte(), 0, 0, ','.code.toByte(), 'i'.code.toByte(), 0, 0),
        )
        for (data in cases) assertThrows(IllegalArgumentException::class.java) { KatOsc.parseMessage(data) }
    }

    @Test fun rewriteWithoutClear() {
        val groups = KatOsc.buildMessages("Good morning", 8, clear = false)
        assertEquals(2, groups.size)
        assertArrayEquals(KatOsc.oscMessage("/avatar/parameters/KAT_Pointer", 1), groups[0][0])
    }

    @Test fun syncChoicesMatchThePrefab() {
        assertEquals(listOf(4, 8, 16), KatOsc.SYNC_CHOICES)
        assertThrows(IllegalArgumentException::class.java) { KatOsc.buildMessages("a", 2) }
    }

    @Test fun groups() {
        val groups = KatOsc.buildMessages("Good morning", 8)
        assertEquals(3, groups.size)
        assertEquals(1, groups[0].size)
        assertArrayEquals(KatOsc.oscMessage("/avatar/parameters/KAT_Pointer", 255), groups[0][0])
        assertEquals(9, groups[1].size)
        assertArrayEquals(KatOsc.oscMessage("/avatar/parameters/KAT_Pointer", 1), groups[1][0])
        assertArrayEquals(KatOsc.oscMessage("/avatar/parameters/KAT_CharSync0", 39 / 127f), groups[1][1])
        assertArrayEquals(KatOsc.oscMessage("/avatar/parameters/KAT_Pointer", 2), groups[2][0])
    }

    @Test fun lastGroupPaddedWithSpaces() {
        val groups = KatOsc.buildMessages("Hi", 8)
        assertEquals(9, groups[1].size)
        assertArrayEquals(KatOsc.oscMessage("/avatar/parameters/KAT_CharSync7", 0f), groups[1][8])
    }

    @Test fun longTextUsesAllPointers() {
        assertEquals(1 + 16, KatOsc.buildMessages("x".repeat(300), 8).size)
        assertEquals(1 + 32, KatOsc.buildMessages("x".repeat(300), 4).size)
    }

    @Test fun emptyTextOnlyClears() = assertEquals(1, KatOsc.buildMessages("", 8).size)

    @Test fun rejectsBadSync() {
        assertThrows(IllegalArgumentException::class.java) { KatOsc.buildMessages("a", 3) }
    }
}
