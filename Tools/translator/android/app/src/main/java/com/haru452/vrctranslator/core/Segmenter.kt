// Port of the Segmenter in Tools/translator/phone/voice.py.
package com.haru452.vrctranslator.core

import kotlin.math.max
import kotlin.math.sqrt

data class SegmenterSettings(
    var startS: Float = 0.15f,
    var endSilenceS: Float = 1.2f,
    var prerollS: Float = 0.3f,
    var maxS: Float = 60f,
    var minS: Float = 0.5f,
    var margin: Float = 3f,
    var floorMin: Float = 200f,
) {
    companion object {
        /** The PC's microphone (translator_pc.exe): Windows suppresses room noise to ~0, so quieter speech counts. */
        fun forPcMic() = SegmenterSettings(floorMin = 60f)
    }
}

/**
 * Feed 30 ms frames; get back one utterance (PCM) when the speaker stops.
 * A frame is "voice" when it is margin times louder than the room's noise floor (and above floorMin * margin).
 * Recording starts after startS of voice, keeps prerollS from before the start, ends after endSilenceS of
 * quiet (keeping a short tail), and is cut at maxS. Utterances with less than minS of voice are dropped.
 */
class Segmenter(s: SegmenterSettings) {
    companion object {
        const val RATE = 16000
        const val FRAME_MS = 30
        const val FRAME_SAMPLES = RATE * FRAME_MS / 1000
        private const val TAIL_S = 0.2f

        fun rms(frame: ShortArray): Float {
            if (frame.isEmpty()) return 0f
            var sum = 0.0
            for (v in frame) sum += v.toDouble() * v
            return sqrt(sum / frame.size).toFloat()
        }

        // Python's round() rounds halves to even.
        private fun frames(seconds: Float): Int = max(1, Math.rint(seconds * 1000.0 / FRAME_MS).toInt())
    }

    private val startFrames = frames(s.startS)
    private val endFrames = frames(s.endSilenceS)
    private val maxFrames = frames(s.maxS)
    private val minFrames = frames(s.minS)
    private val tailFrames = frames(TAIL_S)
    private val prerollMax = frames(s.prerollS) + startFrames
    private val margin = s.margin
    private val floorMin = s.floorMin
    private val preroll = ArrayDeque<ShortArray>()
    private var buffer = ArrayList<ShortArray>()
    private var voicedRun = 0
    private var voicedTotal = 0
    private var silence = 0

    var noiseFloor = floorMin
        private set
    var recording = false
        private set
    val threshold: Float get() = max(noiseFloor, floorMin) * margin

    /** Forget anything half-recorded (used when the toggle turns off). */
    fun reset() {
        recording = false
        buffer = ArrayList()
        voicedRun = 0
        voicedTotal = 0
        silence = 0
        preroll.clear()
    }

    fun feed(frame: ShortArray): ShortArray? {
        val level = rms(frame)
        val voiced = level > threshold
        // Follow the room: quickly while quiet, very slowly otherwise, so a new steady noise (a fan)
        // eventually stops counting as voice.
        val alpha = if (voiced) 0.002f else 0.05f
        if (!recording) {
            noiseFloor += (level - noiseFloor) * alpha
            preroll.addLast(frame)
            while (preroll.size > prerollMax) preroll.removeFirst()
            voicedRun = if (voiced) voicedRun + 1 else 0
            if (voicedRun >= startFrames) {
                recording = true
                buffer = ArrayList(preroll)
                voicedTotal = voicedRun
                silence = 0
            }
            return null
        }

        buffer.add(frame)
        if (voiced) {
            voicedTotal++
            silence = 0
        } else {
            silence++
        }
        if (buffer.size >= maxFrames) return finish(trim = false)
        if (silence >= endFrames) return finish(trim = true)
        return null
    }

    private fun finish(trim: Boolean): ShortArray? {
        val kept = if (trim) buffer.subList(0, buffer.size - (silence - tailFrames)) else buffer
        val enough = voicedTotal >= minFrames
        val pcm = if (enough) ShortArray(kept.sumOf { it.size }).also { out ->
            var pos = 0
            for (f in kept) { f.copyInto(out, pos); pos += f.size }
        } else null
        reset()
        return pcm
    }
}
