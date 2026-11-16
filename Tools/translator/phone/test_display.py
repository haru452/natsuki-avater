import sys
import unittest

sys.path.insert(0, __file__.rsplit("phone", 1)[0])
import kat_osc  # noqa: E402
import display  # noqa: E402
from gemini import Result  # noqa: E402

JA = Result("ja", "おはようございます", "Good morning", "おはようございます")
EN = Result("en", "Good morning", "おはよう", "おはよう")


def cells(text):
    return sum(kat_osc.char_width(ch) for ch in text)


class ComposeTests(unittest.TestCase):
    def test_japanese_speech_shows_english_then_kana_original(self):
        text = display.compose_kat(JA, show_original=True)
        self.assertTrue(text.startswith("Good morning"))
        self.assertEqual(text.index("("), 32, "original starts on the next row")
        self.assertTrue(text.endswith("(おはようございます)"))

    def test_english_speech_shows_kana_then_english_original(self):
        text = display.compose_kat(EN, show_original=True)
        self.assertTrue(text.startswith("おはよう"))
        self.assertEqual(text.rstrip().endswith("(Good morning)"), True)
        self.assertEqual(cells(text[: text.index("(")]), 32)

    def test_without_original(self):
        self.assertEqual(display.compose_kat(JA, show_original=False), "Good morning")

    def test_row_padding_counts_voiced_katakana_as_two_cells(self):
        r = Result("en", "Game", "ゲーム", "ゲーム")
        text = display.compose_kat(r, show_original=True)
        self.assertEqual(cells(text[: text.index("(")]), 32)

    def test_main_line_exactly_one_row_needs_no_padding(self):
        r = Result("ja", "x", "a" * 32, "x")
        self.assertEqual(display.compose_kat(r, True), "a" * 32 + "(x)")

    def test_chatbox_uses_kanji_and_fits_144(self):
        r = Result("en", "Good morning", "おはよう、元気？", "おはよう、げんき？")
        self.assertEqual(display.compose_chatbox(r, True), "おはよう、元気？\n(Good morning)")
        long = Result("ja", "あ" * 100, "b" * 100, "あ" * 100)
        self.assertEqual(display.compose_chatbox(long, True), "b" * 100, "drops the original first")
        longer = Result("ja", "x", "c" * 200, "x")
        out = display.compose_chatbox(longer, True)
        self.assertEqual(len(out), 144)
        self.assertTrue(out.endswith("…"))


class PaginateTests(unittest.TestCase):
    def test_short_text_is_one_page(self):
        self.assertEqual(display.paginate("hello"), ["hello"])

    def test_long_text_breaks_between_words(self):
        text = " ".join(["word"] * 40)  # 199 characters
        pages = display.paginate(text)
        self.assertEqual(len(pages), 2)
        self.assertTrue(all(cells(p) <= 128 for p in pages))
        self.assertFalse(pages[0].endswith("wor"), "no word split")
        self.assertEqual(" ".join(p.strip() for p in pages).split(), text.split())

    def test_never_splits_a_voiced_katakana(self):
        text = "ア" * 127 + "ガ" + "イ"
        pages = display.paginate(text)
        self.assertEqual(pages[0], "ア" * 127)
        self.assertTrue(pages[1].startswith("ガ"))


class FakeClock:
    def __init__(self):
        self.now = 0.0

    def __call__(self):
        return self.now


class Recorder:
    def __init__(self):
        self.packets = []

    def __call__(self, packet):
        self.packets.append(kat_osc.parse_message(packet))

    def take(self):
        out, self.packets = self.packets, []
        return out


def run(d, clock, seconds, step=0.05):
    end = clock.now + seconds
    while clock.now < end - 1e-9:
        clock.now = round(clock.now + step, 4)
        d.tick()


def kat_text(packets, sync=8):
    """Rebuild what KAT shows from pointer/char packets (pointer 255 clears)."""
    cells_ = {}
    pointer = 0
    pending = {}
    for address, args in packets:
        if address.endswith("KAT_Pointer"):
            pointer = args[0]
            if pointer == 255:
                cells_.clear()
        elif "KAT_CharSync" in address and 0 < pointer < 255:
            i = int(address.rsplit("KAT_CharSync", 1)[1])
            cells_[(pointer - 1) * sync + i] = args[0]
    return cells_


class DisplayTests(unittest.TestCase):
    def setUp(self):
        self.clock = FakeClock()
        self.rec = Recorder()
        self.d = display.Display(self.rec, mode="kat", sync=8, show_original=False, clock=self.clock)

    def test_thinking_writes_dots(self):
        self.d.thinking()
        run(self.d, self.clock, 1)
        shown = kat_text(self.rec.take())
        for i in range(3):  # '.' is code 14; OSC floats are 32-bit
            self.assertAlmostEqual(shown[i], kat_osc.code_to_float(14), places=6)

    def test_show_writes_all_groups_then_resends_without_clearing(self):
        self.d.show(JA)
        run(self.d, self.clock, 1.5)
        first = self.rec.take()
        self.assertEqual(first[0], ("/avatar/parameters/KAT_Pointer", [255]), "starts by clearing")
        self.assertEqual(len(kat_text(first)), 16, "'Good morning' padded to 2 groups of 8")
        run(self.d, self.clock, 3)
        again = self.rec.take()
        self.assertTrue(again, "rewrites for late joiners")
        self.assertNotIn(("/avatar/parameters/KAT_Pointer", [255]), again, "no flicker")

    def test_groups_are_spaced_out(self):
        self.d.show(Result("ja", "x", "a" * 40, "x"))
        times = []
        for _ in range(40):
            self.clock.now = round(self.clock.now + 0.05, 4)
            before = len(self.rec.packets)
            self.d.tick()
            if len(self.rec.packets) > before:
                times.append(self.clock.now)
        gaps = [b - a for a, b in zip(times, times[1:])]
        self.assertGreaterEqual(gaps[0], 0.5 - 1e-6, "the clear is held for two steps")
        self.assertTrue(all(g >= 0.25 - 1e-6 for g in gaps))

    def test_long_text_turns_pages_after_it_is_written(self):
        long = Result("ja", "x", " ".join(["word"] * 40), "x")
        self.d.show(long)
        run(self.d, self.clock, 4.5)  # page 1 (clear + 16 groups) is fully written at 4.3 s
        run(self.d, self.clock, 4.7)  # up to 9.2 s: still within 5 s of being written
        self.assertNotIn(("/avatar/parameters/KAT_Pointer", [255]), self.rec.take()[1:], "page 1 stays 5 s")
        run(self.d, self.clock, 1.5)
        self.assertIn(("/avatar/parameters/KAT_Pointer", [255]), self.rec.take(), "then page 2")

    def test_error_shows_then_clears(self):
        self.d.error()
        run(self.d, self.clock, 1)
        self.assertTrue(kat_text(self.rec.take()))
        run(self.d, self.clock, 3)
        self.assertEqual(self.rec.take()[-1], ("/avatar/parameters/KAT_Pointer", [255]))
        run(self.d, self.clock, 5)
        self.assertEqual(self.rec.take(), [], "nothing more after the error is gone")

    def test_clear_wipes_the_dots(self):
        self.d.thinking()
        run(self.d, self.clock, 1)
        self.rec.take()
        self.d.clear()
        run(self.d, self.clock, 1)
        self.assertEqual(self.rec.take(), [("/avatar/parameters/KAT_Pointer", [255])])
        run(self.d, self.clock, 5)
        self.assertEqual(self.rec.take(), [])

    def test_stop_sends_nothing_more(self):
        self.d.show(JA)
        run(self.d, self.clock, 0.3)
        self.d.stop()
        self.rec.take()
        run(self.d, self.clock, 10)
        self.assertEqual(self.rec.take(), [])

    def test_send_failures_do_not_escape(self):
        def broken(packet):
            raise OSError("network is unreachable")
        d = display.Display(broken, mode="both", sync=8, show_original=True, clock=self.clock)
        d.thinking()
        d.show(JA)
        run(d, self.clock, 10)  # no exception

    def test_chatbox_mode(self):
        d = display.Display(self.rec, mode="chatbox", sync=8, show_original=True, clock=self.clock)
        d.thinking()
        self.assertEqual(self.rec.take(), [("/chatbox/typing", [True])])
        d.show(EN)
        run(d, self.clock, 5)
        got = self.rec.take()
        self.assertIn(("/chatbox/input", ["おはよう\n(Good morning)", True, False]), got)
        self.assertIn(("/chatbox/typing", [False]), got)
        self.assertFalse(any("KAT" in a for a, _ in got), "chatbox only")


if __name__ == "__main__":
    unittest.main()
