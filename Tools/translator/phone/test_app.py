import datetime
import math
import os
import socket
import struct
import sys
import tempfile
import threading
import time
import unittest

sys.path.insert(0, __file__.rsplit("phone", 1)[0])
import kat_osc  # noqa: E402
import settings as settings_mod  # noqa: E402
import vrc_translator  # noqa: E402
import voice  # noqa: E402
from gemini import GeminiError, Result  # noqa: E402

ACTIVE = "/avatar/parameters/TranslatorActive"
SAMPLES = voice.FRAME_BYTES // 2


def frames(seconds, amplitude):
    out = []
    for f in range(round(seconds * 1000 / voice.FRAME_MS)):
        s = [int(amplitude * math.sin(2 * math.pi * 220 * (f * SAMPLES + i) / voice.RATE)) for i in range(SAMPLES)]
        out.append(struct.pack("<%dh" % SAMPLES, *s))
    return out


SENTENCE = frames(1, 60) + frames(1, 6000) + frames(1, 60)
JA = Result("ja", "おはよう", "Good morning", "おはよう")


class FakeDisplay:
    def __init__(self):
        self.calls = []

    def __getattr__(self, name):
        if name in ("thinking", "show", "error", "clear", "stop"):
            return lambda *a: self.calls.append((name,) + a)
        if name == "tick":
            return lambda: None
        raise AttributeError(name)


class FakeTranslate:
    def __init__(self, *outcomes):
        self.outcomes = list(outcomes)
        self.calls = 0
        self.before = None  # run just before answering (e.g. toggle off mid-translation)

    def __call__(self, wav):
        self.calls += 1
        if self.before:
            self.before()
        outcome = self.outcomes.pop(0)
        if isinstance(outcome, Exception):
            raise outcome
        return outcome


class Clock:
    def __init__(self):
        self.now = 1000.0

    def __call__(self):
        return self.now


def make_app(translate, log_path=None, clock=None):
    fd, path = tempfile.mkstemp(suffix=".tsv")
    os.close(fd)
    os.remove(path)
    s = settings_mod.Settings(log_file=log_path or path, listen_port=0)
    disp = FakeDisplay()
    app = vrc_translator.App(s, mic_factory=None, translate=translate, display=disp,
                             now=lambda: datetime.datetime(2026, 11, 16, 22, 0, 0), clock=clock or Clock())
    return app, disp, s.log_file


def log_lines(path):
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8") as f:
        return f.read().splitlines()


def speak(app, chunks=SENTENCE):
    for frame in chunks:
        app.feed_frame(frame)


def drain(app):
    while app.process_next(block=False):
        pass


class AppTests(unittest.TestCase):
    def test_on_speech_translates_shows_and_logs(self):
        app, disp, log = make_app(FakeTranslate(JA))
        app.on_osc(ACTIVE, [True])
        speak(app)
        drain(app)
        self.assertEqual(disp.calls, [("thinking",), ("show", JA)])
        self.assertEqual(log_lines(log), ["2026-11-16 22:00:00\tja\tおはよう\tGood morning\tgemini"])

    def test_nothing_happens_while_off(self):
        t = FakeTranslate(JA)
        app, disp, log = make_app(t)
        speak(app)
        drain(app)
        self.assertEqual((t.calls, disp.calls, log_lines(log)), (0, [], []))

    def test_turning_off_mid_translation_logs_but_does_not_show(self):
        t = FakeTranslate(JA)
        app, disp, log = make_app(t)
        app.on_osc(ACTIVE, [True])
        speak(app)
        t.before = lambda: app.on_osc(ACTIVE, [False])
        drain(app)
        self.assertNotIn(("show", JA), disp.calls)
        self.assertEqual(len(log_lines(log)), 1)

    def test_recordings_queued_before_turning_off_are_dropped(self):
        t = FakeTranslate(JA, JA)
        app, disp, log = make_app(t)
        app.on_osc(ACTIVE, [True])
        speak(app)
        app.on_osc(ACTIVE, [False])
        drain(app)
        self.assertEqual(t.calls, 0)

    def test_half_spoken_sentence_is_discarded_on_off(self):
        t = FakeTranslate(JA)
        app, disp, log = make_app(t)
        app.on_osc(ACTIVE, [True])
        speak(app, frames(1, 60) + frames(1, 6000))
        app.on_osc(ACTIVE, [False])
        app.on_osc(ACTIVE, [True])
        speak(app, frames(1, 60))
        drain(app)
        self.assertEqual(t.calls, 0)

    def test_avatar_change_means_off(self):
        t = FakeTranslate(JA)
        app, disp, log = make_app(t)
        app.on_osc(ACTIVE, [True])
        app.on_osc("/avatar/change", ["avtr_x"])
        self.assertFalse(app.active)
        speak(app)
        drain(app)
        self.assertEqual(t.calls, 0)

    def test_error_shows_error_and_keeps_going(self):
        app, disp, log = make_app(FakeTranslate(GeminiError("down"), JA))
        app.on_osc(ACTIVE, [True])
        speak(app)
        speak(app)
        drain(app)
        self.assertEqual(disp.calls, [("thinking",), ("error",), ("thinking",), ("show", JA)])

    def test_unexpected_exception_does_not_stop_the_worker(self):
        app, disp, log = make_app(FakeTranslate(RuntimeError("bug"), JA))
        app.on_osc(ACTIVE, [True])
        speak(app)
        speak(app)
        drain(app)
        self.assertIn(("show", JA), disp.calls)

    def test_no_speech_clears_without_logging(self):
        app, disp, log = make_app(FakeTranslate(Result("none", "", "", "")))
        app.on_osc(ACTIVE, [True])
        speak(app)
        drain(app)
        self.assertEqual(disp.calls, [("thinking",), ("clear",)])
        self.assertEqual(log_lines(log), [])

    def test_bad_osc_values_are_ignored(self):
        app, disp, log = make_app(FakeTranslate())
        for address, args in ((ACTIVE, []), ("/unknown", [1]), (ACTIVE, ["yes"])):
            app.on_osc(address, args)
        self.assertFalse(app.active)

    def test_int_or_float_toggle_values_work(self):
        app, disp, log = make_app(FakeTranslate())
        app.on_osc(ACTIVE, [1])
        self.assertTrue(app.active)
        app.on_osc(ACTIVE, [0.0])
        self.assertFalse(app.active)


class ReviewFixTests(unittest.TestCase):
    def test_stale_recordings_are_skipped(self):
        clock = Clock()
        t = FakeTranslate(JA, JA)
        app, disp, log = make_app(t, clock=clock)
        app.on_osc(ACTIVE, [True])
        speak(app)
        clock.now += vrc_translator.STALE_S + 1  # e.g. Gemini was slow for a long time
        speak(app)
        drain(app)
        self.assertEqual(t.calls, 1, "the old one is dropped, the fresh one is translated")

    def test_unexpected_error_message_is_not_printed(self):
        printed = []
        app, disp, log = make_app(FakeTranslate(RuntimeError("SECRET-KEY in here")))
        original = vrc_translator.say
        vrc_translator.say = printed.append
        try:
            app.on_osc(ACTIVE, [True])
            speak(app)
            drain(app)
        finally:
            vrc_translator.say = original
        self.assertTrue(printed)
        self.assertFalse(any("SECRET-KEY" in p for p in printed))

    def test_local_ai_message_only_when_it_changes(self):
        printed = []
        app, disp, log = make_app(FakeTranslate())
        original = vrc_translator.say
        vrc_translator.say = printed.append
        try:
            for _ in range(3):
                app.on_osc("/avatar/parameters/TranslatorLocalAI", [False])
            app.on_osc("/avatar/parameters/TranslatorLocalAI", [True])
        finally:
            vrc_translator.say = original
        self.assertEqual(len(printed), 1)


class RunTests(unittest.TestCase):
    def test_run_listens_for_the_toggle_and_records(self):
        mic_frames = SENTENCE * 3
        opened = []

        class Mic:
            def __init__(self):
                opened.append(self)

            def frames(self):
                for f in mic_frames:
                    time.sleep(0.001)
                    yield f
                while True:
                    time.sleep(0.01)
                    yield frames(0.03, 60)[0]

            def close(self):
                pass

        t = FakeTranslate(JA, JA, JA, JA)
        app, disp, log = make_app(t)
        app._mic_factory = Mic
        hellos = []
        stop = threading.Event()
        thread = threading.Thread(target=app.run, kwargs=dict(stop=stop, send_hello=lambda: hellos.append(1),
                                                               hello_seconds=0.1))
        thread.start()
        try:
            for _ in range(100):
                if app.listen_port:
                    break
                time.sleep(0.02)
            sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            sock.sendto(kat_osc.osc_message(ACTIVE, True), ("127.0.0.1", app.listen_port))
            for _ in range(300):
                if t.calls >= 1:
                    break
                time.sleep(0.02)
        finally:
            stop.set()
            thread.join(timeout=5)
        self.assertFalse(thread.is_alive(), "run() returns when asked to stop")
        self.assertTrue(opened, "the mic opens once the toggle is on")
        self.assertGreaterEqual(t.calls, 1)
        self.assertTrue(hellos, "says hello to the relay")


if __name__ == "__main__":
    unittest.main()
