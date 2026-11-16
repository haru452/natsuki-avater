import struct
import unittest

import kat_osc


class CharCodeTests(unittest.TestCase):
    def test_ascii_codes(self):
        self.assertEqual(kat_osc.encode_text(" "), [0])
        self.assertEqual(kat_osc.encode_text("A"), [33])
        self.assertEqual(kat_osc.encode_text("~"), [94])

    def test_kana_codes(self):
        self.assertEqual(kat_osc.encode_text("ぬ"), [127])
        self.assertEqual(kat_osc.encode_text("ふ"), [129])
        self.assertEqual(kat_osc.encode_text("おはよう"), [133, 154, 136, 131])
        self.assertEqual(kat_osc.encode_text("ヌ"), [208])
        self.assertEqual(kat_osc.encode_text("ロ"), [253])

    def test_unknown_char_becomes_question(self):
        q = kat_osc.encode_text("?")
        self.assertEqual(kat_osc.encode_text("漢"), q)
        self.assertEqual(kat_osc.encode_text("😀"), q)

    def test_missing_kana_are_substituted(self):
        self.assertEqual(kat_osc.encode_text("っ"), kat_osc.encode_text("つ"))
        self.assertEqual(kat_osc.encode_text("ッ"), kat_osc.encode_text("ツ"))
        self.assertEqual(kat_osc.encode_text("ー"), kat_osc.encode_text("-"))
        self.assertEqual(kat_osc.encode_text("、"), kat_osc.encode_text(","))
        self.assertEqual(kat_osc.encode_text("ャ"), kat_osc.encode_text("ヤ"))

    def test_voiced_katakana_uses_separate_mark(self):
        self.assertEqual(kat_osc.encode_text("ガ"), kat_osc.encode_text("カ") + [254])
        self.assertEqual(kat_osc.encode_text("パ"), kat_osc.encode_text("ハ") + [255])

    def test_fullwidth_becomes_halfwidth(self):
        self.assertEqual(kat_osc.encode_text("Ａ１！"), kat_osc.encode_text("A1!"))

    def test_never_emits_unsendable_code(self):
        codes = kat_osc.encode_text("".join(chr(c) for c in range(0x20, 0x3100)))
        self.assertNotIn(128, codes)
        self.assertTrue(all(0 <= c <= 255 for c in codes))

    def test_truncates_at_limit(self):
        self.assertEqual(len(kat_osc.encode_text("x" * 300)), 128)
        self.assertEqual(len(kat_osc.encode_text("ガ" * 100)), 128)

    def test_newline_and_tab_become_space(self):
        self.assertEqual(kat_osc.encode_text("a\nb\tc"), kat_osc.encode_text("a b c"))


class FloatTests(unittest.TestCase):
    def test_float_mapping(self):
        self.assertAlmostEqual(kat_osc.code_to_float(0), 0.0)
        self.assertAlmostEqual(kat_osc.code_to_float(127), 1.0)
        self.assertAlmostEqual(kat_osc.code_to_float(129), -1.0)
        self.assertAlmostEqual(kat_osc.code_to_float(255), -1 / 127)


class OscTests(unittest.TestCase):
    def test_osc_message_layout(self):
        self.assertEqual(kat_osc.osc_message("/a", 1), b"/a\x00\x00,i\x00\x00\x00\x00\x00\x01")
        self.assertEqual(kat_osc.osc_message("/a", True), b"/a\x00\x00,T\x00\x00")
        self.assertEqual(kat_osc.osc_message("/a", False), b"/a\x00\x00,F\x00\x00")
        self.assertEqual(kat_osc.osc_message("/a", 0.5), b"/a\x00\x00,f\x00\x00" + struct.pack(">f", 0.5))

    def test_address_padding_is_multiple_of_four(self):
        self.assertEqual(kat_osc.osc_message("/abc", True), b"/abc\x00\x00\x00\x00,T\x00\x00")


class GroupTests(unittest.TestCase):
    def test_groups(self):
        groups = kat_osc.build_messages("Good morning", sync=8)
        self.assertEqual(len(groups), 3)
        # The toggle owns KAT_Visible, so the sender only clears and writes.
        self.assertEqual(groups[0], [kat_osc.osc_message("/avatar/parameters/KAT_Pointer", 255)])
        self.assertEqual(len(groups[1]), 9)
        # Pointer first: KAT writes the floats into whatever slot the pointer names.
        self.assertEqual(groups[1][0], kat_osc.osc_message("/avatar/parameters/KAT_Pointer", 1))
        self.assertEqual(groups[1][1], kat_osc.osc_message("/avatar/parameters/KAT_CharSync0", 39 / 127))  # G
        self.assertEqual(groups[2][0], kat_osc.osc_message("/avatar/parameters/KAT_Pointer", 2))

    def test_last_group_padded_with_spaces(self):
        groups = kat_osc.build_messages("Hi", sync=8)
        self.assertEqual(len(groups[1]), 9)
        self.assertEqual(groups[1][8], kat_osc.osc_message("/avatar/parameters/KAT_CharSync7", 0.0))

    def test_long_text_uses_all_pointers(self):
        self.assertEqual(len(kat_osc.build_messages("x" * 300, sync=8)), 1 + 16)
        self.assertEqual(len(kat_osc.build_messages("x" * 300, sync=4)), 1 + 32)

    def test_empty_text_only_clears(self):
        self.assertEqual(len(kat_osc.build_messages("", sync=8)), 1)

    def test_rejects_bad_sync(self):
        with self.assertRaises(ValueError):
            kat_osc.build_messages("a", sync=3)


class SendTests(unittest.TestCase):
    def test_clear_is_held_longer_than_one_step(self):
        from unittest import mock

        with mock.patch.object(kat_osc.time, "sleep") as sleep, mock.patch.object(kat_osc.socket, "socket"):
            kat_osc.send("Hi", step_delay=0.25)
        self.assertGreaterEqual(sleep.call_args_list[0].args[0], 0.5)

    def test_send_delivers_every_message_in_order(self):
        import socket

        receiver = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        receiver.bind(("127.0.0.1", 0))
        receiver.settimeout(2)
        try:
            kat_osc.send("Hi", port=receiver.getsockname()[1], step_delay=0)
            expected = [m for g in kat_osc.build_messages("Hi") for m in g]
            got = [receiver.recv(1024) for _ in expected]
        finally:
            receiver.close()
        self.assertEqual(got, expected)


if __name__ == "__main__":
    unittest.main()
