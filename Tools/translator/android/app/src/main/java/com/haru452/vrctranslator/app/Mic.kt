package com.haru452.vrctranslator.app

import android.annotation.SuppressLint
import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder
import com.haru452.vrctranslator.core.Segmenter

/** The phone's microphone as 30 ms frames of 16 kHz mono 16-bit samples. */
class Mic : AutoCloseable {
    private val record: AudioRecord

    init {
        val min = AudioRecord.getMinBufferSize(Segmenter.RATE, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT)
        @SuppressLint("MissingPermission")  // MainActivity asks for RECORD_AUDIO before starting the service
        val r = AudioRecord(MediaRecorder.AudioSource.VOICE_RECOGNITION, Segmenter.RATE,
            AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT, maxOf(min, Segmenter.FRAME_SAMPLES * 2 * 8))
        check(r.state == AudioRecord.STATE_INITIALIZED) { "マイクを開けません" }
        record = r
        record.startRecording()
    }

    /** Blocks until one frame is read; null if the microphone stopped. */
    fun read(): ShortArray? {
        val frame = ShortArray(Segmenter.FRAME_SAMPLES)
        var got = 0
        while (got < frame.size) {
            val n = record.read(frame, got, frame.size - got)
            if (n <= 0) return null
            got += n
        }
        return frame
    }

    override fun close() {
        runCatching { record.stop() }
        record.release()
    }
}
