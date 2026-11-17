package com.haru452.vrctranslator.core

import java.nio.ByteBuffer
import java.nio.ByteOrder
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** Builds a packet the way the PC does (TranslatorPc.Core/AudioGate.cs AudioPacket.Encode). */
internal fun pcPacket(kind: Int, seq: Long, samples: ShortArray, magic: String = "VTA1"): ByteArray {
    val b = ByteBuffer.allocate(9 + samples.size * 2)
    b.put(magic.toByteArray(Charsets.US_ASCII)).put(kind.toByte()).putInt(seq.toInt())
    b.order(ByteOrder.LITTLE_ENDIAN)
    samples.forEach { b.putShort(it) }
    return b.array()
}

class AudioPacketTests {
    @Test fun decodesWhatThePcSends() {
        val samples = ShortArray(320) { (it * 7 - 1000).toShort() }
        val p = AudioPacket.decode(pcPacket(1, 0xFFFFFFF0L, samples))!!
        assertEquals(AudioPacket.Kind.VRCHAT, p.kind)
        assertEquals(0xFFFFFFF0L, p.seq)
        assertArrayEquals(samples, p.samples)
    }

    @Test fun rejectsOtherPackets() {
        assertNull(AudioPacket.decode(pcPacket(0, 1, ShortArray(320), magic = "VTA2")))
        assertNull(AudioPacket.decode(pcPacket(0, 1, ShortArray(300))))
        assertNull(AudioPacket.decode(pcPacket(9, 1, ShortArray(320))))
        assertNull(AudioPacket.decode(ByteArray(3)))
    }

    @Test fun rechunks20msInto30msFrames() {
        val r = Rechunker(480)
        val out = (0 until 3).flatMap { i -> r.push(ShortArray(320) { (i * 320 + it).toShort() }) }
        assertEquals(2, out.size)
        assertEquals(480, out[1].size)
        assertEquals(480.toShort(), out[1][0])
    }

    @Test fun lateAndRepeatedPacketsAreDropped() {
        val f = SequenceFilter()
        assertTrue(f.accept(5))
        assertTrue(f.accept(7))
        assertFalse("late", f.accept(6))
        assertFalse("repeated", f.accept(7))
        assertTrue(f.accept(8))
    }

    @Test fun aRestartedPcIsFollowed() {
        val f = SequenceFilter()
        assertTrue(f.accept(100_000))
        assertTrue("far behind means the PC program restarted", f.accept(3))
    }
}
