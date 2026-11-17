package com.haru452.vrctranslator.core

import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class RelayLinkTests {
    @Test fun helloCarriesSessionAndOthersFlag() =
        assertEquals("/translator/hello" to listOf<Any>(123, false, true), KatOsc.parseMessage(RelayProtocol.helloMessage(123, false, true)))

    @Test fun sendFailureDoesNotThrow() {
        RelayLink({ "203.0.113.1" }, 9, 0, { _, _ -> }).use { link ->
            link.send(KatOsc.oscMessage("/a", 1))
            link.tick(0.0)
        }
    }

    @Test fun emptyPcIpSendsNothingAndHelloGoesToThePc() {
        DatagramSocket(0, InetAddress.getLoopbackAddress()).use { pc ->
            pc.soTimeout = 300
            var ip = ""
            RelayLink({ ip }, pc.localPort, 0, { _, _ -> }).use { link ->
                link.send(KatOsc.oscMessage("/a", 1))
                link.tick(0.0)
                val buf = ByteArray(256)
                assertFalse("nothing sent without an IP", runCatching { pc.receive(DatagramPacket(buf, buf.size)) }.isSuccess)
                ip = "127.0.0.1"
                link.tick(10.0)
                val p = DatagramPacket(buf, buf.size)
                pc.receive(p)
                assertEquals(RelayProtocol.HELLO, KatOsc.parseMessage(p.data.copyOf(p.length)).first)
                link.tick(12.0)
                assertFalse("hello only every 5 s", runCatching { pc.receive(DatagramPacket(buf, buf.size)) }.isSuccess)
            }
        }
    }

    @Test fun receivesToggleFromLoopback() {
        val got = CountDownLatch(1)
        var seen: Pair<String, List<Any>>? = null
        RelayLink({ "127.0.0.1" }, 9, 0, { a, args -> seen = a to args; got.countDown() }).use { link ->
            assertFalse(link.found)
            DatagramSocket().use { s ->
                val data = KatOsc.oscMessage(OwnVoicePipeline.ACTIVE, true)
                s.send(DatagramPacket(data, data.size, InetAddress.getLoopbackAddress(), link.boundPort))
            }
            assertTrue(got.await(1, TimeUnit.SECONDS))
            assertEquals(OwnVoicePipeline.ACTIVE to listOf<Any>(true), seen)
            assertTrue(link.found)
        }
    }
}
