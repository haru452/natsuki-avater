"""One Gemini call: transcribe, detect the language and translate (Japanese <-> English). Standard library only."""
import base64
import dataclasses
import json
import socket
import time
import urllib.error
import urllib.request

ENDPOINT = "https://generativelanguage.googleapis.com/v1beta/models/%s:generateContent"
TIMEOUT_S = 20
RATE_LIMIT_WAITS = (2, 4, 8)   # seconds before each retry after HTTP 429
LANGS = ("ja", "en", "none")

PROMPT = """You help a Japanese learner of English talk in VRChat.
Listen to the audio. It is one person speaking Japanese or English.
- lang: "ja" if Japanese, "en" if English, "none" if there is no clear speech (noise, music, silence).
- original: what they said, transcribed as spoken.
- translation: the meaning in the other language (Japanese -> English, English -> Japanese),
  casual and natural like friends chatting, ideally 120 characters or fewer.
- kana: the Japanese side (original if lang is "ja", translation if lang is "en") rewritten using ONLY
  hiragana and katakana, no kanji. Keep English words as they are. Use 、 and 。 normally.
If lang is "none", return empty strings for the other fields."""

SCHEMA = {
    "type": "OBJECT",
    "properties": {
        "lang": {"type": "STRING", "enum": list(LANGS)},
        "original": {"type": "STRING"},
        "translation": {"type": "STRING"},
        "kana": {"type": "STRING"},
    },
    "required": ["lang", "original", "translation", "kana"],
}


class GeminiError(Exception):
    pass


@dataclasses.dataclass(frozen=True)
class Result:
    lang: str
    original: str
    translation: str
    kana: str


def build_request(wav, model, api_key):
    body = {
        "contents": [{"parts": [
            {"text": PROMPT},
            {"inline_data": {"mime_type": "audio/wav", "data": base64.b64encode(wav).decode("ascii")}},
        ]}],
        "generationConfig": {
            "responseMimeType": "application/json",
            "responseSchema": SCHEMA,
            "temperature": 0.3,
        },
    }
    # The key goes in a header, never in the URL, so it cannot leak into logs or error messages.
    return urllib.request.Request(
        ENDPOINT % model,
        data=json.dumps(body).encode("utf-8"),
        headers={"Content-Type": "application/json", "x-goog-api-key": api_key},
        method="POST",
    )


def parse_response(body):
    try:
        data = json.loads(body)
        parts = data["candidates"][0]["content"]["parts"]
        text = "".join(p.get("text", "") for p in parts if not p.get("thought"))
        obj = json.loads(text)
        result = Result(*(str(obj[k]) for k in ("lang", "original", "translation", "kana")))
    except (ValueError, KeyError, IndexError, TypeError) as error:
        raise GeminiError("Gemini の返事を読めませんでした: %s" % type(error).__name__) from None
    if result.lang not in LANGS:
        raise GeminiError("Gemini が知らない言語を返しました: %s" % result.lang)
    return result


def translate(wav, model, api_key, opener=urllib.request.urlopen, sleep=time.sleep):
    """
    Result for one utterance. Retries: HTTP 429 waits 2, 4, 8 s; a network error or 5xx is retried once.
    Raises GeminiError (whose message never contains the key) when it gives up.
    """
    rate_limited = 0
    other_failures = 0
    while True:
        try:
            with opener(build_request(wav, model, api_key), timeout=TIMEOUT_S) as response:
                return parse_response(response.read())
        except urllib.error.HTTPError as error:
            if error.code == 429 and rate_limited < len(RATE_LIMIT_WAITS):
                sleep(RATE_LIMIT_WAITS[rate_limited])
                rate_limited += 1
                continue
            if error.code >= 500 and other_failures == 0:
                other_failures += 1
                continue
            reason = {400: "リクエストが正しくありません", 403: "API キーが使えません",
                      404: "モデルが見つかりません", 429: "回数の上限です"}.get(error.code, "")
            raise GeminiError("Gemini がエラーを返しました（HTTP %d %s）" % (error.code, reason)) from None
        except (urllib.error.URLError, socket.timeout, TimeoutError, ConnectionError) as error:
            if other_failures == 0:
                other_failures += 1
                continue
            raise GeminiError("Gemini につながりません: %s" % type(error).__name__) from None
