import datetime
import os
import tempfile
import unittest

import settings
import translog

HERE = os.path.dirname(os.path.abspath(__file__))


def write_ini(text):
    fd, path = tempfile.mkstemp(suffix=".ini")
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        f.write(text)
    return path


class SettingsTests(unittest.TestCase):
    def test_bundled_defaults(self):
        s = settings.load_settings(os.path.join(HERE, "settings.ini"))
        self.assertEqual(s.pc_ip, "192.168.1.10")
        self.assertEqual((s.relay_port, s.listen_port), (9002, 9001))
        self.assertEqual(s.model, "gemini-flash-latest")
        self.assertTrue(s.show_original)
        self.assertEqual(s.display, "kat")
        self.assertEqual(s.kat_sync, 8)
        self.assertEqual(s.log_file, os.path.expanduser("~/vrc_translation_log.tsv"))
        self.assertEqual(s.end_silence_s, 0.8)
        self.assertEqual(s.max_s, 60)

    def test_missing_keys_use_defaults(self):
        s = settings.load_settings(write_ini("[translator]\nPC_IP = 10.0.0.5\n"))
        self.assertEqual(s.pc_ip, "10.0.0.5")
        self.assertEqual(s.kat_sync, 8)

    def test_bad_values_raise(self):
        for bad in ("DISPLAY = screen", "KAT_SYNC = 3", "SHOW_ORIGINAL = maybe", "RELAY_PORT = abc"):
            with self.assertRaises(ValueError, msg=bad):
                settings.load_settings(write_ini("[translator]\n" + bad + "\n"))

    def test_missing_file_raises(self):
        with self.assertRaises(ValueError):
            settings.load_settings(os.path.join(HERE, "no_such_settings.ini"))


class LogTests(unittest.TestCase):
    def test_one_line_per_translation(self):
        fd, path = tempfile.mkstemp(suffix=".tsv")
        os.close(fd)
        when = datetime.datetime(2026, 11, 16, 21, 30, 5)
        translog.append_log(path, when, "ja", "おはよう\nございます", "Good\tmorning", "gemini")
        translog.append_log(path, when, "en", "Hi", "やあ", "gemini")
        with open(path, encoding="utf-8") as f:
            lines = f.read().splitlines()
        self.assertEqual(lines[0], "2026-11-16 21:30:05\tja\tおはよう ございます\tGood morning\tgemini")
        self.assertEqual(len(lines), 2)

    def test_creates_missing_folder(self):
        folder = tempfile.mkdtemp()
        path = os.path.join(folder, "sub", "log.tsv")
        translog.append_log(path, datetime.datetime.now(), "en", "a", "b", "gemini")
        self.assertTrue(os.path.exists(path))


if __name__ == "__main__":
    unittest.main()
