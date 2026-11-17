// Receives the 20 ms audio packets translator_pc.exe sends on UDP 9003 and hands each stream's samples on.
package com.haru452.vrctranslator.app

import com.haru452.vrctranslator.core.AudioPacket
import com.haru452.vrctranslator.core.SequenceFilter
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.net.SocketTimeoutException

class AudioReceiver(
    port: Int = AudioPacket.PORT,
    private val onSamples: (AudioPacket.Kind, ShortArray) -> Unit,
) : AutoCloseable {
    private val socket = DatagramSocket(null).apply {
        reuseAddress = true
        soTimeout = 200
        bind(InetSocketAddress(port))
    }
    private val filters = AudioPacket.Kind.entries.associateWith { SequenceFilter() }
    @Volatile private var closed = false
    @Volatile private var lastPacket = 0L

    /** Whether audio has arrived in the last 2 s. */
    val receiving: Boolean get() = lastPacket != 0L && (System.nanoTime() - lastPacket) / 1e9 < 2.0

    private val thread = Thread({ loop() }, "audio-receive").apply { isDaemon = true; start() }

    private fun loop() {
        val buf = ByteArray(2048)
        while (!closed) {
            val p = DatagramPacket(buf, buf.size)
            try {
                socket.receive(p)
            } catch (e: SocketTimeoutException) {
                continue
            } catch (e: Exception) {
                if (closed) return
                Thread.sleep(500)
                continue
            }
            val packet = AudioPacket.decode(p.data, p.length) ?: continue
            if (!filters.getValue(packet.kind).accept(packet.seq)) continue
            lastPacket = System.nanoTime()
            onSamples(packet.kind, packet.samples)
        }
    }

    override fun close() {
        closed = true
        socket.close()
        thread.join(1000)
    }
}
