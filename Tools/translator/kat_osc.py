"""Encode text for KAT (KillFrenzy Avatar Text) and send it to VRChat over OSC.

Standard library only, so the same module runs on the PC and in Termux on the phone.
The character table matches KAT v1.3.2's KAT_CharTiles.png (16 x 16 tiles).

The avatar's toggle owns KAT_Visible and clears the text whenever it is switched; the sender
only clears (KAT_Pointer = 255) and writes. Each group sends the pointer before its characters,
because KAT keeps writing the CharSync floats into whichever slot the pointer names.

send() writes once. Synced parameters only carry the latest value, so players who join later
see only the last group. The phone script should keep cycling the pointer over all groups
while a line is shown (TODO for the phone-side tool).
"""
import socket
import struct
import time
import unicodedata

TEXT_LIMIT = 128
CLEAR_POINTER = 255
SYNC_CHOICES = (1, 2, 4, 8, 16)
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


def osc_message(address, value):
    def pad(raw):
        return raw + b"\x00" * (4 - len(raw) % 4)

    head = pad(address.encode("ascii"))
    if isinstance(value, bool):
        return head + pad(b",T" if value else b",F")
    if isinstance(value, int):
        return head + pad(b",i") + struct.pack(">i", value)
    return head + pad(b",f") + struct.pack(">f", value)


def build_messages(text, sync=8):
    """Groups of OSC messages in send order. Wait between groups so each one syncs."""
    if sync not in SYNC_CHOICES:
        raise ValueError(f"sync must be one of {SYNC_CHOICES}, got {sync}")
    codes = encode_text(text)
    groups = [[osc_message(PARAM_PREFIX + "KAT_Pointer", CLEAR_POINTER)]]
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
