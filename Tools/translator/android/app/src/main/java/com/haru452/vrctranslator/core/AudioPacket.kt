// Audio from translator_pc.exe (see TranslatorPc.Core/AudioGate.cs):
// "VTA1", kind (0 = your PC mic, 1 = VRChat's sound), sequence (4 bytes, big-endian),
// 320 samples of 16 kHz mono 16-bit little-endian (20 ms).
package com.haru452.vrctranslator.core

import java.nio.ByteBuffer
import java.nio.ByteOrder

class AudioPacket(val kind: Kind, val seq: Long, val samples: ShortArray) {
    enum class Kind { OWN_MIC, VRCHAT }

    companion object {
        const val PORT = 9003
        const val SAMPLES = 320
        const val LENGTH = 9 + SAMPLES * 2
        private val MAGIC = "VTA1".toByteArray(Charsets.US_ASCII)

        fun decode(data: ByteArray, length: Int = data.size): AudioPacket? {
            if (length != LENGTH) return null
            for (i in MAGIC.indices) if (data[i] != MAGIC[i]) return null
            val kind = when (data[4].toInt()) {
                0 -> Kind.OWN_MIC
                1 -> Kind.VRCHAT
                else -> return null
            }
            val seq = ByteBuffer.wrap(data, 5, 4).int.toLong() and 0xFFFFFFFFL
            val samples = ShortArray(SAMPLES)
            ByteBuffer.wrap(data, 9, SAMPLES * 2).order(ByteOrder.LITTLE_ENDIAN).asShortBuffer().get(samples)
            return AudioPacket(kind, seq, samples)
        }
    }
}

/** Turns the PC's 20 ms packets into the 30 ms frames the Segmenter wants. */
class Rechunker(private val frameSamples: Int = Segmenter.FRAME_SAMPLES) {
    private var buffer = ShortArray(0)

    fun push(samples: ShortArray): List<ShortArray> {
        buffer += samples
        val out = ArrayList<ShortArray>()
        var pos = 0
        while (buffer.size - pos >= frameSamples) {
            out.add(buffer.copyOfRange(pos, pos + frameSamples))
            pos += frameSamples
        }
        buffer = buffer.copyOfRange(pos, buffer.size)
        return out
    }

    fun reset() { buffer = ShortArray(0) }
}

/**
 * Drops packets that arrive late or twice (UDP can reorder). A big jump backwards means the PC program
 * restarted and counts from 0 again, so that is followed instead of dropped.
 */
class SequenceFilter(private val restartGap: Long = 1000) {
    private var last = -1L

    fun accept(seq: Long): Boolean {
        if (last >= 0 && seq <= last && last - seq < restartGap) return false
        last = seq
        return true
    }
}
