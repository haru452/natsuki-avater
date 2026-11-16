"""Encode text for KAT (KillFrenzy Avatar Text) and send it to VRChat over OSC.

Standard library only, so the same module runs on the PC and in Termux on the phone.
The character table matches KAT v1.3.2's KAT_CharTiles.png (16 x 16 tiles).

The avatar's toggle owns KAT_Visible and clears the text whenever it is switched; the sender
only clears (KAT_Pointer = 255) and writes. Each group sends the pointer before its characters,
because KAT keeps writing the CharSync floats into whichever slot the pointer names.

send() writes once. Synced parameters only carry the latest value, so players who join later
see only the last group; the phone's display keeps rewriting a shown line (clear=False) for them.
"""
import socket
import struct
import time
import unicodedata

TEXT_LIMIT = 128
CLEAR_POINTER = 255
# Must match TranslatorConfig.KatSyncChoices on the avatar side.
SYNC_CHOICES = (4, 8, 16)
PARAM_PREFIX = "/avatar/parameters/"

_ROWS = {
    129: "ふあうえおやゆよわをほへたてい",
    144: "すかんなにらせちとしはきくまのり",
    160: "れけむつさそひこみもねるめろ。ぶ",
    176: "ぷぼぽべぺだでずがぜぢどじばぱぎ",
    192: "ぐげづざぞびぴごぁぃぅぇぉゃゅょ",
    208: "ヌフアウエオヤユヨワヲホヘタテイ",
    224: "スカンナニラセチトシハキクマノリ",
    240: "レケムツサソヒコミモネルメロ゛゜",
}


def _build_table():
    table = {chr(c): c - 32 for c in range(32, 127)}
    table["€"] = 95
    table["ぬ"] = 127
    for start, chars in _ROWS.items():
        for i, ch in enumerate(chars):
            table[ch] = start + i
    return table


_TABLE = _build_table()
_UNKNOWN = _TABLE["?"]

# Characters the tile sheet lacks, replaced with the closest one it has.
_SUBSTITUTES = {
    "っ": "つ", "ゎ": "わ", "ゔ": "う゛",
    "ッ": "ツ", "ャ": "ヤ", "ュ": "ユ", "ョ": "ヨ", "ヮ": "ワ",
    "ァ": "ア", "ィ": "イ", "ゥ": "ウ", "ェ": "エ", "ォ": "オ",
    "ー": "-", "、": ",", "・": ".", "「": "[", "」": "]", "『": "[", "』": "]",
    "〜": "~", "～": "~", "…": "...", "’": "'", "‘": "'", "“": '"', "”": '"',
    "—": "-", "–": "-", "゙": "゛", "゚": "゜", "\n": " ", "\t": " ", "\r": " ",
}


def _codes_for(ch, depth=0):
    if ch in _TABLE:
        return [_TABLE[ch]]
    if depth > 3:
        return [_UNKNOWN]
    if ch in _SUBSTITUTES:
        return [c for part in _SUBSTITUTES[ch] for c in _codes_for(part, depth + 1)]
    for form in ("NFKC", "NFD"):
        normalized = unicodedata.normalize(form, ch)
        if normalized != ch:
            return [c for part in normalized for c in _codes_for(part, depth + 1)]
    return [_UNKNOWN]


def encode_text(text):
    """Text -> KAT character codes, at most TEXT_LIMIT. Unsupported characters become '?'."""
    codes = []
    for ch in text:
        codes.extend(_codes_for(ch))
        if len(codes) >= TEXT_LIMIT:
            break
    return codes[:TEXT_LIMIT]


def code_to_float(code):
    """KAT maps codes 0..127 to 0..1 and 129..255 to -1..-1/127."""
    return (code - 256 if code > 127 else code) / 127.0


def _pad(raw):
    return raw + b"\x00" * (4 - len(raw) % 4)


def osc_message(address, *values):
    """One OSC message. Values may be bool, int, float or str (sent as UTF-8)."""
    tags = b","
    body = b""
    for value in values:
        if isinstance(value, bool):
            tags += b"T" if value else b"F"
        elif isinstance(value, int):
            tags += b"i"
            body += struct.pack(">i", value)
        elif isinstance(value, float):
            tags += b"f"
            body += struct.pack(">f", value)
        elif isinstance(value, str):
            tags += b"s"
            body += _pad(value.encode("utf-8"))
        else:
            raise TypeError(f"unsupported OSC value: {value!r}")
    return _pad(address.encode("ascii")) + _pad(tags) + body


def _read_string(data, pos):
    end = data.find(b"\x00", pos)
    if end < 0:
        raise ValueError("unterminated OSC string")
    return data[pos:end].decode("utf-8"), (end // 4 + 1) * 4


def parse_message(data):
    """(address, [values]) from one OSC message. Raises ValueError on anything malformed."""
    try:
        address, pos = _read_string(data, 0)
        if not address.startswith("/"):
            raise ValueError("OSC address must start with /")
        tags, pos = _read_string(data, pos)
        if not tags.startswith(","):
            raise ValueError("missing OSC type tags")
        values = []
        for tag in tags[1:]:
            if tag in "TF":
                values.append(tag == "T")
            elif tag in "if":
                if pos + 4 > len(data):
                    raise ValueError("truncated OSC argument")
                values.append(struct.unpack(">i" if tag == "i" else ">f", data[pos:pos + 4])[0])
                pos += 4
            elif tag == "s":
                text, pos = _read_string(data, pos)
                values.append(text)
            else:
                raise ValueError(f"unsupported OSC type tag {tag!r}")
        return address, values
    except UnicodeDecodeError as error:
        raise ValueError(str(error)) from error


def build_messages(text, sync=8, clear=True):
    """
    Groups of OSC messages in send order. Wait between groups so each one syncs.
    clear=False rewrites the same slots without wiping first (periodic resends, no flicker).
    """
    if sync not in SYNC_CHOICES:
        raise ValueError(f"sync must be one of {SYNC_CHOICES}, got {sync}")
    codes = encode_text(text)
    groups = [[osc_message(PARAM_PREFIX + "KAT_Pointer", CLEAR_POINTER)]] if clear else []
    for start in range(0, len(codes), sync):
        chunk = codes[start:start + sync]
        chunk += [0] * (sync - len(chunk))
        group = [osc_message(PARAM_PREFIX + "KAT_Pointer", start // sync + 1)]
        group += [
            osc_message(f"{PARAM_PREFIX}KAT_CharSync{i}", code_to_float(code))
            for i, code in enumerate(chunk)
        ]
        groups.append(group)
    return groups


def send(text, host="127.0.0.1", port=9000, sync=8, step_delay=0.25):
    groups = build_messages(text, sync)
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
        for index, group in enumerate(groups):
            for message in group:
                sock.sendto(message, (host, port))
            if step_delay and index < len(groups) - 1:
                # Hold the clear for two steps so remote players do not miss it.
                time.sleep(step_delay * (2 if index == 0 else 1))
