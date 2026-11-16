import base64
import io
import json
import socket
import unittest
import urllib.error

import gemini

WAV = b"RIFF....WAVEfake"


def reply(obj, wrap=True):
    """A generateContent response body whose text part holds obj as JSON."""
    text = json.dumps(obj, ensure_ascii=False)
    body = {"candidates": [{"content": {"parts": [{"text": text}]}}]} if wrap else obj
    return json.dumps(body).encode("utf-8")


class FakeResponse(io.BytesIO):
    def __enter__(self):
        return self

    def __exit__(self, *a):
        return False


def http_error(code):
    return urllib.error.HTTPError("https://x", code, "err", {}, io.BytesIO(b"{}"))


class Opener:
    """Plays back a list of outcomes: bytes (a body) or an exception."""

    def __init__(self, *outcomes):
        self.outcomes = list(outcomes)
        self.requests = []

    def __call__(self, request, timeout=None):
        self.requests.append((request, timeout))
        outcome = self.outcomes.pop(0)
        if isinstance(outcome, Exception):
            raise outcome
        return FakeResponse(outcome)


GOOD = {"lang": "ja", "original": "おはよう", "translation": "Good morning", "kana": "おはよう"}


class RequestTests(unittest.TestCase):
    def test_key_in_header_not_url(self):
        req = gemini.build_request(WAV, "gemini-flash-latest", "SECRET")
        self.assertNotIn("SECRET", req.full_url)
        self.assertEqual(req.get_header("X-goog-api-key"), "SECRET")
        self.assertTrue(req.full_url.endswith("/models/gemini-flash-latest:generateContent"))
        self.assertEqual(req.get_method(), "POST")

    def test_body_carries_audio_and_schema(self):
        req = gemini.build_request(WAV, "m", "k")
        body = json.loads(req.data)
        parts = body["contents"][0]["parts"]
        audio = [p for p in parts if "inline_data" in p][0]["inline_data"]
        self.assertEqual(audio["mime_type"], "audio/wav")
        self.assertEqual(base64.b64decode(audio["data"]), WAV)
        cfg = body["generationConfig"]
        self.assertEqual(cfg["responseMimeType"], "application/json")
        self.assertEqual(set(cfg["responseSchema"]["properties"]), {"lang", "original", "translation", "kana"})
        self.assertEqual(cfg["responseSchema"]["properties"]["lang"]["enum"], ["ja", "en", "none"])


class ParseTests(unittest.TestCase):
    def test_reads_result(self):
        self.assertEqual(gemini.parse_response(reply(GOOD)), gemini.Result("ja", "おはよう", "Good morning", "おはよう"))

    def test_no_speech(self):
        r = gemini.parse_response(reply({"lang": "none", "original": "", "translation": "", "kana": ""}))
        self.assertEqual(r.lang, "none")

    def test_skips_thought_parts(self):
        body = {"candidates": [{"content": {"parts": [
            {"text": "thinking...", "thought": True},
            {"text": json.dumps(GOOD)}]}}]}
        self.assertEqual(gemini.parse_response(json.dumps(body).encode()).translation, "Good morning")

    def test_broken_replies_raise(self):
        for body in (b"not json", reply({"lang": "ja"}), json.dumps({"candidates": []}).encode(),
                     reply({"lang": "fr", "original": "", "translation": "", "kana": ""}),
                     json.dumps({"candidates": [{"content": {"parts": [{"text": "{oops"}]}}]}).encode()):
            with self.assertRaises(gemini.GeminiError, msg=body):
                gemini.parse_response(body)


class TranslateTests(unittest.TestCase):
    def setUp(self):
        self.slept = []

    def translate(self, opener):
        return gemini.translate(WAV, "m", "k", opener=opener, sleep=self.slept.append)

    def test_success_uses_timeout(self):
        opener = Opener(reply(GOOD))
        self.assertEqual(self.translate(opener).translation, "Good morning")
        self.assertEqual(opener.requests[0][1], 20)

    def test_rate_limit_backs_off(self):
        opener = Opener(http_error(429), http_error(429), reply(GOOD))
        self.assertEqual(self.translate(opener).lang, "ja")
        self.assertEqual(self.slept, [2, 4])

    def test_rate_limit_gives_up_after_three_retries(self):
        opener = Opener(*[http_error(429)] * 4)
        with self.assertRaises(gemini.GeminiError):
            self.translate(opener)
        self.assertEqual(self.slept, [2, 4, 8])

    def test_server_error_retries_once(self):
        opener = Opener(http_error(500), http_error(503))
        with self.assertRaises(gemini.GeminiError):
            self.translate(opener)
        self.assertEqual(len(opener.requests), 2)

    def test_network_error_retries_once(self):
        opener = Opener(urllib.error.URLError("down"), reply(GOOD))
        self.assertEqual(self.translate(opener).lang, "ja")
        opener = Opener(socket.timeout("slow"), socket.timeout("slow"))
        with self.assertRaises(gemini.GeminiError):
            self.translate(opener)

    def test_bad_request_is_not_retried(self):
        opener = Opener(http_error(400))
        with self.assertRaises(gemini.GeminiError):
            self.translate(opener)
        self.assertEqual(len(opener.requests), 1)

    def test_error_message_never_contains_the_key(self):
        opener = Opener(http_error(403))
        with self.assertRaises(gemini.GeminiError) as ctx:
            gemini.translate(WAV, "m", "SECRET-KEY", opener=opener, sleep=self.slept.append)
        self.assertNotIn("SECRET-KEY", str(ctx.exception))


if __name__ == "__main__":
    unittest.main()
