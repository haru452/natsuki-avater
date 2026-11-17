// Ported from Tools/translator/phone/test_voice.py (rms and Segmenter cases, same values).
package com.haru452.vrctranslator.core

import kotlin.math.PI
import kotlin.math.roundToInt
import kotlin.math.sin
import kotlin.math.sqrt
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

internal fun frames(seconds: Double, amplitude: Int, freq: Double = 220.0): List<ShortArray> {
    val n = (seconds * 1000 / 30).roundToInt()
    return (0 until n).map { f ->
        ShortArray(Segmenter.FRAME_SAMPLES) { i ->
            (amplitude * sin(2 * PI * freq * (f * Segmenter.FRAME_SAMPLES + i) / Segmenter.RATE)).toInt().toShort()
        }
    }
}

class SegmenterTests {
    private val quiet = 60      // room noise
    private val speech = 6000   // talking

    private fun feedAll(seg: Segmenter, chunks: List<ShortArray>) = chunks.mapNotNull { seg.feed(it) }
    private fun secondsOf(pcm: ShortArray) = pcm.size.toDouble() / Segmenter.RATE

    @Test fun rmsOfSine() = assertEquals(1000 / sqrt(2.0), Segmenter.rms(frames(0.03, 1000)[0]).toDouble(), 15.0)

    @Test fun rmsOfSilenceAndEmpty() {
        assertEquals(0f, Segmenter.rms(frames(0.03, 0)[0]))
        assertEquals(0f, Segmenter.rms(ShortArray(0)))
    }

    @Test fun oneUtteranceWithPreroll() {
        val seg = Segmenter(SegmenterSettings())
        val got = feedAll(seg, frames(1.0, quiet) + frames(1.0, speech) + frames(1.5, quiet))
        assertEquals(1, got.size)
        assertTrue(secondsOf(got[0]) > 1.25)
        assertTrue(secondsOf(got[0]) < 1.6)
        assertFalse(seg.recording)
    }

    // 0.8 s cut sentences into pieces ("うん、なか。") in real use; 1.2 s keeps a sentence whole.
    @Test fun waitsFor12SecondsOfSilence() {
        val seg = Segmenter(SegmenterSettings())
        assertEquals(0, feedAll(seg, frames(1.0, quiet) + frames(1.0, speech) + frames(1.0, quiet)).size)
        assertTrue("a pause of a second is part of the same sentence", seg.recording)
        assertEquals(1, feedAll(seg, frames(0.3, quiet)).size)
    }

    @Test fun pauseInsideSentenceKeepsOneUtterance() {
        val chunks = frames(1.0, quiet) + frames(1.0, speech) + frames(0.5, quiet) + frames(1.0, speech) + frames(1.5, quiet)
        assertEquals(1, feedAll(Segmenter(SegmenterSettings()), chunks).size)
    }

    @Test fun shortNoiseIsDropped() =
        assertEquals(0, feedAll(Segmenter(SegmenterSettings()), frames(1.0, quiet) + frames(0.3, speech) + frames(1.0, quiet)).size)

    @Test fun twoUtterances() {
        val one = frames(1.0, quiet) + frames(1.0, speech) + frames(1.5, quiet)
        assertEquals(2, feedAll(Segmenter(SegmenterSettings()), one + one).size)
    }

    @Test fun longSpeechIsCutAtMax() {
        val got = feedAll(Segmenter(SegmenterSettings(maxS = 5f)), frames(0.5, quiet) + frames(6.0, speech))
        assertEquals(1, got.size)
        assertEquals(5.0, secondsOf(got[0]), 0.05)
    }

    @Test fun noiseFloorFollowsALouderRoom() {
        val seg = Segmenter(SegmenterSettings())
        feedAll(seg, frames(1.0, quiet))
        val quietFloor = seg.noiseFloor
        feedAll(seg, frames(0.1, 400))  // a fan switches on: too short to count as speech
        feedAll(seg, frames(4.0, 400))
        assertTrue(seg.noiseFloor > quietFloor * 3)
        assertEquals("steady noise is not speech", 0, feedAll(seg, frames(2.0, 400)).size)
    }

    @Test fun resetDiscardsARecording() {
        val seg = Segmenter(SegmenterSettings())
        feedAll(seg, frames(1.0, quiet) + frames(1.0, speech))
        assertTrue(seg.recording)
        seg.reset()
        assertFalse(seg.recording)
        assertEquals(0, feedAll(seg, frames(1.0, quiet)).size)
    }

    // The PC's microphone array suppresses noise to exact zeros, and quiet speech sits around 200-600.
    @Test fun pcMicHearsQuietSpeech() {
        val seg = Segmenter(SegmenterSettings.forPcMic())
        val out = feedAll(seg, frames(1.0, 0) + frames(1.0, 400) + frames(1.5, 0))
        assertEquals(1, out.size)
    }
}
