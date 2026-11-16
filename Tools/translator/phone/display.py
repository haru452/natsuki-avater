"""What goes above the pet (KAT) and into the chatbox, and when.

KAT shows 32 cells x 4 rows and has no line break, so a second line is started by padding with spaces.
KAT cannot show kanji, so the pet gets the kana-only Japanese (Result.kana); the chatbox gets kanji.
Writing is spread over time (one group per step) and driven by tick(), called often by the main loop.
"""
import time

import kat_osc

ROW = 32
CHATBOX_LIMIT = 144
POINTER = kat_osc.PARAM_PREFIX + "KAT_Pointer"


def _cells(text):
    return sum(kat_osc.char_width(ch) for ch in text)


def compose_kat(result, show_original):
    """Japanese speech: English, then (kana original). English speech: kana Japanese, then (English original)."""
    if result.lang == "ja":
        main, original = result.translation, result.kana
    else:
        main, original = result.kana, result.original
    if not show_original or not original:
        return main
    pad = -_cells(main) % ROW
    return main + " " * pad + "(" + original + ")"


def compose_chatbox(result, show_original):
    text = result.translation
    if show_original and result.original:
        with_original = text + "\n(" + result.original + ")"
        if len(with_original) <= CHATBOX_LIMIT:
            return with_original
    if len(text) <= CHATBOX_LIMIT:
        return text
    return text[: CHATBOX_LIMIT - 1] + "…"


def paginate(text, limit=kat_osc.TEXT_LIMIT, word_backtrack=20):
    """Pages of at most `limit` KAT cells, never splitting a character, preferring to break at a space."""
    pages = []
    page, used = "", 0
    for ch in text:
        width = kat_osc.char_width(ch)
        if used + width > limit:
            cut = page.rfind(" ")
            if ch != " " and cut > 0 and len(page) - cut <= word_backtrack:
                pages.append(page[:cut])
                page = page[cut + 1:]
            else:
                pages.append(page)
                page = ""
            page = page.lstrip(" ")
            used = _cells(page)
            if ch == " " and not page:
                continue
        page += ch
        used += width
    if page or not pages:
        pages.append(page)
    return pages


class Display:
    """
    thinking() / show(result) / error() / stop(), then call tick() every ~50 ms.
    A shown page is rewritten (without clearing) every resend_seconds so late joiners get all of it;
    long text turns to the next page page_seconds after a page is fully written.
    """

    def __init__(self, send, mode="kat", sync=8, show_original=True, clock=time.monotonic,
                 page_seconds=5.0, resend_seconds=3.0, step_seconds=0.25, error_seconds=3.0):
        self._send = send
        self._kat = mode in ("kat", "both")
        self._chatbox = mode in ("chatbox", "both")
        self._sync = sync
        self._show_original = show_original
        self._clock = clock
        self._page_s = page_seconds
        self._resend_s = resend_seconds
        self._step_s = step_seconds
        self._error_s = error_seconds
        self.stop()

    # --- commands -------------------------------------------------------

    def thinking(self):
        if self._kat:
            self._reset()
            self._write("...", clear=True)
        if self._chatbox:
            self._safe_send(kat_osc.osc_message("/chatbox/typing", True))

    def show(self, result):
        if self._kat:
            self._reset()
            self._pages = paginate(compose_kat(result, self._show_original))
            self._write(self._pages[0], clear=True)
        if self._chatbox:
            self._safe_send(kat_osc.osc_message("/chatbox/input", compose_chatbox(result, self._show_original), True, False))
            self._safe_send(kat_osc.osc_message("/chatbox/typing", False))

    def error(self):
        if self._kat:
            self._reset()
            self._write("(error)", clear=True)
            self._error_until = self._clock() + self._error_s
        if self._chatbox:
            self._safe_send(kat_osc.osc_message("/chatbox/typing", False))

    def clear(self):
        """Wipe the text (e.g. the "..." when the audio turned out to hold no speech)."""
        if self._kat:
            self._reset()
            self._queue = [[kat_osc.osc_message(POINTER, kat_osc.CLEAR_POINTER)]]
            self._next = self._clock()
        if self._chatbox:
            self._safe_send(kat_osc.osc_message("/chatbox/typing", False))

    def stop(self):
        """Forget everything; the avatar's toggle clears the text itself."""
        self._reset()

    # --- timing ---------------------------------------------------------

    def tick(self):
        now = self._clock()
        if self._queue:
            if now >= self._next:
                group = self._queue.pop(0)
                for packet in group:
                    self._safe_send(packet)
                is_clear = len(group) == 1 and kat_osc.parse_message(group[0]) == (POINTER, [kat_osc.CLEAR_POINTER])
                self._next = now + self._step_s * (2 if is_clear else 1)  # hold the clear so nobody misses it
                if not self._queue:
                    self._written_at = now
            return
        if self._error_until is not None:
            if now >= self._error_until:
                self._error_until = None
                self._safe_send(kat_osc.osc_message(POINTER, kat_osc.CLEAR_POINTER))
            return
        if not self._pages or self._written_at is None:
            return
        if self._page < len(self._pages) - 1:
            if now - self._written_at >= self._page_s:
                self._page += 1
                self._write(self._pages[self._page], clear=True)
        elif now - self._written_at >= self._resend_s:
            self._write(self._pages[self._page], clear=False)

    # --- helpers --------------------------------------------------------

    def _reset(self):
        self._queue = []
        self._next = 0.0
        self._pages = []
        self._page = 0
        self._written_at = None
        self._error_until = None

    def _write(self, text, clear):
        self._queue = kat_osc.build_messages(text, self._sync, clear=clear)
        self._next = self._clock()
        self._written_at = None

    def _safe_send(self, packet):
        try:
            self._send(packet)
        except Exception:  # a dropped Wi-Fi must never stop the translator
            pass
