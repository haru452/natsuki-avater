// Ported from the DisplayTests of Tools/translator/phone/test_display.py, plus Chinese routing.
package com.haru452.vrctranslator.core

import kotlin.math.round
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

private typealias Packet = Pair<String, List<Any>>

class KatDisplayTests {
    private var now = 0.0
    private var packets = ArrayList<Packet>()
    private lateinit var d: KatDisplay

    private fun take(): List<Packet> = packets.also { packets = ArrayList() }
    private fun record(p: ByteArray) { packets.add(KatOsc.parseMessage(p)) }
    private fun new(mode: DisplayMode, showOriginal: Boolean, send: (ByteArray) -> Unit = ::record) =
        KatDisplay(send, mode, 8, showOriginal, { now })

    @Before fun setUp() { d = new(DisplayMode.KAT, false) }

    private fun run(disp: KatDisplay, seconds: Double, step: Double = 0.05) {
        val end = now + seconds
        while (now < end - 1e-9) {
            now = round((now + step) * 10000) / 10000
            disp.tick()
        }
    }

    private fun isClear(p: Packet) = p.first == "/avatar/parameters/KAT_Pointer" && p.second[0] == 255

    /** Rebuild what KAT shows from pointer/char packets (pointer 255 clears). */
    private fun katText(ps: List<Packet>, sync: Int = 8): Map<Int, Float> {
        val cells = HashMap<Int, Float>()
        var pointer = 0
        for ((address, args) in ps) {
            if (address.endsWith("KAT_Pointer")) {
                pointer = args[0] as Int
                if (pointer == 255) cells.clear()
            } else if ("KAT_CharSync" in address && pointer in 1..254) {
                val i = address.substringAfterLast("KAT_CharSync").toInt()
                cells[(pointer - 1) * sync + i] = args[0] as Float
            }
        }
        return cells
    }

    @Test fun thinkingWritesDots() {
        d.thinking()
        run(d, 1.0)
        val shown = katText(take())
        for (i in 0 until 3) assertEquals(KatOsc.codeToFloat(14), shown.getValue(i), 1e-6f)
    }

    @Test fun showWritesAllGroupsThenResendsWithoutClearing() {
        d.show(JA_RESULT)
        run(d, 1.5)
        val first = take()
        assertTrue("starts by clearing", isClear(first[0]))
        assertEquals("'Good morning' padded to 2 groups of 8", 16, katText(first).size)
        run(d, 3.0)
        val again = take()
        assertTrue("rewrites for late joiners", again.isNotEmpty())
        assertFalse("no flicker", again.any(::isClear))
    }

    @Test fun groupsAreSpacedOut() {
        d.show(r("ja", "x", "a".repeat(40), "x"))
        val pointerTimes = ArrayList<Double>()
        repeat(40) {
            now = round((now + 0.05) * 10000) / 10000
            d.tick()
            if (take().any { it.first.endsWith("KAT_Pointer") }) pointerTimes.add(now)
        }
        val gaps = pointerTimes.zipWithNext { a, b -> b - a }
        assertTrue("the clear is held for two steps", gaps[0] >= 0.5 - 1e-6)
        assertTrue(gaps.all { it >= 0.25 - 1e-6 })
    }

    // KAT keeps writing the current characters into the pointer's cells until the pointer changes. Sent together,
    // the new characters landed in the previous cells for a frame ("yeah" became "yeあh"). So the pointer goes
    // first, and the characters only once it has moved.
    @Test fun charactersFollowTheirPointerAfterAPause() {
        d.show(r("ja", "x", "a".repeat(40), "x"))
        var lastPointerAt = Double.NaN
        repeat(80) {
            now = round((now + 0.05) * 10000) / 10000
            d.tick()
            val sent = take()
            val pointer = sent.any { it.first.endsWith("KAT_Pointer") }
            val chars = sent.any { "KAT_CharSync" in it.first }
            assertFalse("pointer and characters in the same moment at $now", pointer && chars)
            if (pointer) lastPointerAt = now
            if (chars) assertTrue("characters ${now - lastPointerAt} s after the pointer", now - lastPointerAt >= 0.1 - 1e-6)
        }
    }

    @Test fun longTextTurnsPagesAfterItIsWritten() {
        d.show(r("ja", "x", List(40) { "word" }.joinToString(" "), "x"))
        run(d, 4.5)  // page 1 (clear + 16 groups) is fully written at 4.3 s
        run(d, 4.7)  // up to 9.2 s: still within 5 s of being written
        assertFalse("page 1 stays 5 s", take().drop(1).any(::isClear))
        run(d, 1.5)
        assertTrue("then page 2", take().any(::isClear))
    }

    @Test fun errorShowsThenClears() {
        d.error()
        run(d, 1.0)
        assertTrue(katText(take()).isNotEmpty())
        run(d, 3.0)
        assertTrue(isClear(take().last()))
        run(d, 5.0)
        assertTrue("nothing more after the error is gone", take().isEmpty())
    }

    @Test fun backToBackResultsEachGetShown() {
        d.show(r("ja", "x", "first answer", "x"))
        run(d, 0.3)  // only partly written
        d.thinking()  // the next sentence is being translated
        d.show(r("ja", "y", "second answer", "y"))
        run(d, 0.8)
        assertEquals("the first answer is finished, not replaced by '...'", 16, katText(packets).size)
        run(d, 4.0)
        assertFalse("and stays a while", take().drop(1).any(::isClear))
        run(d, 3.0)
        assertTrue("then the second answer replaces it", take().any(::isClear))
    }

    @Test fun errorAndClearDoNotCutAShownResult() {
        d.show(JA_RESULT)
        run(d, 0.3)
        d.error()
        d.clear()
        run(d, 1.5)
        assertEquals("the answer is still written in full", 16, katText(take()).size)
    }

    @Test fun clearWipesTheDots() {
        d.thinking()
        run(d, 1.0)
        take()
        d.clear()
        run(d, 1.0)
        val got = take()
        assertEquals(1, got.size)
        assertTrue(isClear(got[0]))
        run(d, 5.0)
        assertTrue(take().isEmpty())
    }

    @Test fun stopSendsNothingMore() {
        d.show(JA_RESULT)
        run(d, 0.3)
        d.stop()
        take()
        run(d, 10.0)
        assertTrue(take().isEmpty())
    }

    @Test fun sendFailuresDoNotEscape() {
        val broken = new(DisplayMode.BOTH, true) { throw java.io.IOException("network is unreachable") }
        broken.thinking()
        broken.show(JA_RESULT)
        run(broken, 10.0)  // no exception
    }

    @Test fun chatboxMode() {
        val c = new(DisplayMode.CHATBOX, true)
        c.thinking()
        assertEquals(listOf<Packet>("/chatbox/typing" to listOf(true)), take())
        c.show(EN_RESULT)
        run(c, 5.0)
        val got = take()
        assertTrue(("/chatbox/input" to listOf<Any>("おはよう\n(Good morning)", true, false)) in got)
        assertTrue(("/chatbox/typing" to listOf<Any>(false)) in got)
        assertFalse("chatbox only", got.any { "KAT" in it.first })
    }

    @Test fun queuedAndDroppedAnswersAreReported() {
        val events = ArrayList<String>()
        val k = KatDisplay(::record, DisplayMode.KAT, 8, false, { now }, onEvent = { events.add(it) })
        k.show(r("ja", "a", "first", "a"))
        repeat(4) { k.show(r("ja", "b", "next $it", "b")) }
        assertEquals(4, events.count { it.startsWith("表示を待ちます") })
        assertEquals(1, events.count { it.startsWith("表示を飛ばしました") && "next 0" in it })
    }

    @Test fun zhTargetGoesToChatboxEvenInKatMode() {
        val k = new(DisplayMode.KAT, true)
        k.targetIsChinese = true
        k.thinking()
        k.show(TranslationResult(Lang.JA, Lang.ZH, "こんにちは", "你好"))
        run(k, 5.0)
        val got = take()
        assertTrue(("/chatbox/input" to listOf<Any>("你好\n(こんにちは)", true, false)) in got)
        assertFalse("KAT is not used when the other person speaks Chinese", got.any { "KAT_" in it.first })
    }
}
