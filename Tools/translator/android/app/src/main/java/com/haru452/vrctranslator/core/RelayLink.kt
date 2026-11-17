// Talks to the PC relay (Tools/translator/pc/pc_osc_relay.py): sends KAT/chatbox OSC to PC_IP:relayPort and says
// hello every 5 s so the relay knows where the phone is; receives the toggle values VRChat sends on listenPort.
package com.haru452.vrctranslator.core

import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.SocketTimeoutException
import kotlin.random.Random

object RelayProtocol {
    const val HELLO = "/translator/hello"

    /** session changes every launch, so the relay can tell a restarted app from the running one. */
    fun helloMessage(session: Int, othersOn: Boolean): ByteArray = KatOsc.oscMessage(HELLO, session, othersOn)
}

class RelayLink(
    private val pcIp: () -> String,
    private val relayPort: Int,
    listenPort: Int,
    private val onOsc: (String, List<Any>) -> Unit,
) : AutoCloseable {
    companion object {
        const val HELLO_SECONDS = 5.0
        const val FOUND_SECONDS = 15.0
    }

    private val socket = DatagramSocket(null).apply {
        reuseAddress = true
        soTimeout = 200
        bind(InetSocketAddress(listenPort))
    }
    private val session = Random.nextInt(1, Int.MAX_VALUE)
    private var nextHello = Double.NEGATIVE_INFINITY
    @Volatile private var lastReceived = 0L
    @Volatile private var closed = false

    /** Whether the other players' voices should be streamed (plan 3); false for now. */
    @Volatile var othersOn = false

    val boundPort: Int get() = socket.localPort
    val found: Boolean get() = lastReceived != 0L && (System.nanoTime() - lastReceived) / 1e9 < FOUND_SECONDS

    private val receiver = Thread({ receiveLoop() }, "relay-receive").apply { isDaemon = true; start() }

    /** Sends to the PC; does nothing without an IP, and never throws (a Wi-Fi hiccup must not stop anything). */
    fun send(packet: ByteArray) {
        val ip = pcIp().trim()
        if (ip.isEmpty() || closed) return
        try {
            socket.send(DatagramPacket(packet, packet.size, InetAddress.getByName(ip), relayPort))
        } catch (e: Exception) {
            // dropped
        }
    }

    /** Call often with a monotonic time in seconds; says hello every HELLO_SECONDS. */
    fun tick(now: Double) {
        if (pcIp().isBlank() || now < nextHello) return
        nextHello = now + HELLO_SECONDS
        send(RelayProtocol.helloMessage(session, othersOn))
    }

    private fun receiveLoop() {
        val buf = ByteArray(65535)
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
            lastReceived = System.nanoTime()
            try {
                val (address, args) = KatOsc.parseMessage(p.data.copyOf(p.length))
                onOsc(address, args)
            } catch (e: IllegalArgumentException) {
                // not OSC: ignore
            }
        }
    }

    override fun close() {
        closed = true
        socket.close()
        receiver.join(1000)
    }
}
