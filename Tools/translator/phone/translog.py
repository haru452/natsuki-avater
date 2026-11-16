"""Review log: one tab-separated line per translation. Never holds the API key or the audio."""
import os


def _clean(text):
    return " ".join(str(text).replace("\t", " ").split())


def append_log(path, when, lang, original, translation, engine):
    folder = os.path.dirname(path)
    if folder:
        os.makedirs(folder, exist_ok=True)
    fields = [when.strftime("%Y-%m-%d %H:%M:%S"), lang, original, translation, engine]
    with open(path, "a", encoding="utf-8", newline="\n") as f:
        f.write("\t".join(_clean(x) for x in fields) + "\n")
