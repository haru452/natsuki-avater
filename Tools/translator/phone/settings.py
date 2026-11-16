"""Reads settings.ini (configparser, standard library). Missing keys fall back to the defaults below."""
import configparser
import dataclasses
import os

DISPLAY_CHOICES = ("kat", "chatbox", "both")
KAT_SYNC_CHOICES = (4, 8, 16)


@dataclasses.dataclass(frozen=True)
class Settings:
    pc_ip: str = "192.168.1.10"
    relay_port: int = 9002
    listen_port: int = 9001
    model: str = "gemini-flash-latest"
    show_original: bool = True
    display: str = "kat"
    kat_sync: int = 8
    log_file: str = "~/vrc_translation_log.tsv"
    margin: float = 3.0
    floor_min: float = 200.0
    start_s: float = 0.15
    end_silence_s: float = 0.8
    preroll_s: float = 0.3
    max_s: float = 60.0
    min_s: float = 0.5

    def segmenter_args(self):
        return dict(start_s=self.start_s, end_silence_s=self.end_silence_s, preroll_s=self.preroll_s,
                    max_s=self.max_s, min_s=self.min_s, margin=self.margin, floor_min=self.floor_min)


_KEYS = {
    # ini key: (section, field, type)
    "PC_IP": ("translator", "pc_ip", str),
    "RELAY_PORT": ("translator", "relay_port", int),
    "LISTEN_PORT": ("translator", "listen_port", int),
    "MODEL": ("translator", "model", str),
    "SHOW_ORIGINAL": ("translator", "show_original", bool),
    "DISPLAY": ("translator", "display", str),
    "KAT_SYNC": ("translator", "kat_sync", int),
    "LOG_FILE": ("translator", "log_file", str),
    "MARGIN": ("voice", "margin", float),
    "FLOOR_MIN": ("voice", "floor_min", float),
    "START_S": ("voice", "start_s", float),
    "END_SILENCE_S": ("voice", "end_silence_s", float),
    "PREROLL_S": ("voice", "preroll_s", float),
    "MAX_S": ("voice", "max_s", float),
    "MIN_S": ("voice", "min_s", float),
}


def load_settings(path):
    """Settings from an ini file. Raises ValueError (with the key name) on a missing file or a bad value."""
    parser = configparser.ConfigParser(inline_comment_prefixes=(";", "#"))
    parser.optionxform = str.upper
    if not parser.read(path, encoding="utf-8"):
        raise ValueError("設定ファイルが見つかりません: %s" % path)

    values = {}
    for key, (section, field, kind) in _KEYS.items():
        if not parser.has_option(section, key):
            continue
        try:
            if kind is bool:
                values[field] = parser.getboolean(section, key)
            else:
                values[field] = kind(parser.get(section, key).strip())
        except ValueError as error:
            raise ValueError("設定 %s の値が正しくありません: %s" % (key, error)) from error

    settings = Settings(**values)
    if settings.display not in DISPLAY_CHOICES:
        raise ValueError("設定 DISPLAY は %s のどれかにしてください" % " / ".join(DISPLAY_CHOICES))
    if settings.kat_sync not in KAT_SYNC_CHOICES:
        raise ValueError("設定 KAT_SYNC は 4 / 8 / 16 のどれかにしてください（アバター側と同じ値）")
    return dataclasses.replace(settings, log_file=os.path.expanduser(settings.log_file))
