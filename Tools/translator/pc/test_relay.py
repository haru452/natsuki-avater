import os
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
sys.path.insert(0, HERE)
import kat_osc  # noqa: E402
import pc_osc_relay  # noqa: E402

PHONE = ("192.168.1.20", 50123)
PHONE_IN = ("192.168.1.20", 9001)
VRCHAT = ("127.0.0.1", 9000)
HELLO = kat_osc.osc_message("/translator/hello", True)
ACTIVE_ON = kat_osc.osc_message("/avatar/parameters/TranslatorActive", True)
LOCAL_AI = kat_osc.osc_message("/avatar/parameters/TranslatorLocalAI", False)
CHANGE = kat_osc.osc_message("/avatar/change", "avtr_123")


class RelayTests(unittest.TestCase):
    def setUp(self):
        self.r = pc_osc_relay.Relay()

    def test_vrchat_values_wait_for_the_phone(self):
        self.assertEqual(self.r.from_vrchat(ACTIVE_ON), [], "no phone yet: remember only")
        self.assertEqual(self.r.from_phone(HELLO, PHONE), [(ACTIVE_ON, PHONE_IN)], "hello gets the remembered value")

    def test_after_hello_values_go_straight_to_the_phone(self):
        self.r.from_phone(HELLO, PHONE)
        self.assertEqual(self.r.from_vrchat(ACTIVE_ON), [(ACTIVE_ON, PHONE_IN)])
        self.assertEqual(self.r.from_vrchat(LOCAL_AI), [(LOCAL_AI, PHONE_IN)])

    def test_avatar_change_forgets_old_values(self):
        self.r.from_vrchat(ACTIVE_ON)
        self.r.from_phone(HELLO, PHONE)
        self.assertEqual(self.r.from_vrchat(CHANGE), [(CHANGE, PHONE_IN)], "the phone hears about the change")
        self.assertEqual(self.r.from_phone(HELLO, PHONE), [], "and is not told the old avatar's toggle")

    def test_other_avatar_output_is_dropped(self):
        self.r.from_phone(HELLO, PHONE)
        for address, value in (("/avatar/parameters/GestureLeft", 3), ("/avatar/parameters/VelocityX", 0.5),
                               ("/avatar/parameters/KAT_Pointer", 1)):
            self.assertEqual(self.r.from_vrchat(kat_osc.osc_message(address, value)), [], address)

    def test_phone_messages_go_to_vrchat(self):
        kat = kat_osc.osc_message("/avatar/parameters/KAT_Pointer", 255)
        chat = kat_osc.osc_message("/chatbox/input", "hi", True, False)
        self.assertEqual(self.r.from_phone(kat, PHONE), [(kat, VRCHAT)])
        self.assertEqual(self.r.from_phone(chat, PHONE), [(chat, VRCHAT)])

    def test_phone_that_moves_to_a_new_address_is_followed(self):
        self.r.from_phone(HELLO, PHONE)
        self.r.from_phone(HELLO, ("192.168.1.33", 40000))
        self.assertEqual(self.r.from_vrchat(ACTIVE_ON), [(ACTIVE_ON, ("192.168.1.33", 9001))])

    def test_hello_replays_values_only_for_a_new_phone(self):
        self.r.from_vrchat(ACTIVE_ON)
        self.assertEqual(len(self.r.from_phone(HELLO, PHONE)), 1)
        self.assertEqual(self.r.from_phone(HELLO, PHONE), [], "the same phone again: nothing new")

    def test_restarted_phone_script_gets_the_values_again(self):
        self.r.from_vrchat(ACTIVE_ON)
        self.r.from_phone(kat_osc.osc_message("/translator/hello", 111), PHONE)
        again = self.r.from_phone(kat_osc.osc_message("/translator/hello", 222), PHONE)
        self.assertEqual(again, [(ACTIVE_ON, PHONE_IN)])

    def test_only_text_messages_reach_vrchat(self):
        for address, value in (("/avatar/parameters/GestureLeft", 1), ("/input/Jump", 1),
                               ("/avatar/parameters/TranslatorActive", True)):
            self.assertEqual(self.r.from_phone(kat_osc.osc_message(address, value), PHONE), [], address)

    def test_windows_connection_reset_does_not_stop_receiving(self):
        class Sock:
            def __init__(self):
                self.calls = 0

            def recvfrom(self, n):
                self.calls += 1
                if self.calls == 1:
                    raise ConnectionResetError(10054, "reset")
                return b"x", ("1.2.3.4", 5)

        sock = Sock()
        self.assertIsNone(pc_osc_relay.receive(sock))
        self.assertEqual(pc_osc_relay.receive(sock), (b"x", ("1.2.3.4", 5)))

    def test_broken_packets_are_ignored(self):
        self.assertEqual(self.r.from_vrchat(b"\x00garbage"), [])
        self.assertEqual(self.r.from_phone(b"garbage", PHONE), [])
        self.assertIsNone(self.r.phone)


if __name__ == "__main__":
    unittest.main()
