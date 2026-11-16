"""Translate your own voice and show it above your VRChat pet. Runs in Termux on the phone.

    export GEMINI_API_KEY="..."
    python vrc_translator.py

While the avatar's 翻訳 toggle is on, it listens, splits your speech at pauses, asks Gemini for the
translation and sends it through the PC relay (pc_osc_relay.py) to the KAT text above the pet.
Ctrl+C stops it. See README.md for the setup.
"""
import datetime
import os
import queue
import socket
import sys
import threading
import time

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import kat_osc  # noqa: E402
import display as display_mod  # noqa: E402
import gemini  # noqa: E402
import settings as settings_mod  # noqa: E402
import translog  # noqa: E402
import voice  # noqa: E402

ACTIVE = kat_osc.PARAM_PREFIX + "TranslatorActive"
LOCAL_AI = kat_osc.PARAM_PREFIX + "TranslatorLocalAI"
AVATAR_CHANGE = "/avatar/change"
HELLO = "/translator/hello"
TICK_S = 0.05


def say(text):
    print(datetime.datetime.now().strftime("%H:%M:%S"), text, flush=True)


class App:
    """
    Glue between the toggle, the microphone, the translator and the display.
    Every part is passed in, so the logic is tested without a phone, a network or Gemini.
    """

    def __init__(self, settings, mic_factory, translate, display, now=datetime.datetime.now, engine="gemini"):
        self.settings = settings
        self._mic_factory = mic_factory
        self._translate = translate
        self._display = display
        self._now = now
        self._engine = engine
        self._segmenter = voice.Segmenter(**settings.segmenter_args())
        self._queue = queue.Queue()
        self._lock = threading.RLock()
        self._generation = 0  # bumped whenever listening stops, so older recordings are dropped
        self.active = False
        self.local_ai = False
        self.listen_port = None

    # --- toggle -----------------------------------------------------------

    def on_osc(self, address, args):
        if address == AVATAR_CHANGE:
            self._set_active(False)  # a new avatar starts with the translator off
            return
        if not args or isinstance(args[0], str):
            return
        if address == ACTIVE:
            self._set_active(bool(args[0]))
        elif address == LOCAL_AI:
            self.local_ai = bool(args[0])
            say("AI の切り替え: %s（ローカル AI はまだありません。Gemini で訳します）" % ("ローカル" if self.local_ai else "Gemini"))

    def _set_active(self, on):
        with self._lock:
            if on == self.active:
                return
            self.active = on
            if not on:
                self._generation += 1
                self._segmenter.reset()
                self._display.stop()
        say("翻訳: オン（聞いています）" if on else "翻訳: オフ")

    def _current(self, generation):
        with self._lock:
            return self.active and generation == self._generation

    # --- audio and translation ---------------------------------------------

    def feed_frame(self, frame):
        with self._lock:
            if not self.active:
                return
            pcm = self._segmenter.feed(frame)
            generation = self._generation
        if pcm is not None:
            self._queue.put((voice.to_wav(pcm), generation))

    def process_next(self, block=True, timeout=0.5):
        """Translate one queued utterance. False when there was nothing to do."""
        try:
            wav, generation = self._queue.get(block, timeout)
        except queue.Empty:
            return False
        self._process(wav, generation)
        return True

    def _process(self, wav, generation):
        if not self._current(generation):
            return  # recorded before the toggle went off
        with self._lock:
            self._display.thinking()
        try:
            result = self._translate(wav)
        except gemini.GeminiError as error:
            say("翻訳できませんでした: %s" % error)
            self._if_current(generation, self._display.error)
            return
        except Exception as error:  # never let one bad utterance stop the translator
            say("予期しないエラー: %s: %s" % (type(error).__name__, error))
            self._if_current(generation, self._display.error)
            return

        if result.lang == "none":
            self._if_current(generation, self._display.clear)
            return
        try:
            translog.append_log(self.settings.log_file, self._now(), result.lang, result.original,
                                result.translation, self._engine)
        except OSError as error:
            say("記録できませんでした: %s" % error)
        say("%s: %s → %s" % (result.lang.upper(), result.original, result.translation))
        self._if_current(generation, lambda: self._display.show(result))

    def _if_current(self, generation, action):
        with self._lock:
            if self.active and generation == self._generation:
                action()

    # --- threads -------------------------------------------------------------

    def run(self, stop=None, send_hello=None, hello_seconds=5.0):
        stop = stop or threading.Event()
        listener = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        listener.bind(("0.0.0.0", self.settings.listen_port))
        listener.settimeout(0.2)
        self.listen_port = listener.getsockname()[1]
        threads = [threading.Thread(target=fn, args=(stop,), daemon=True)
                   for fn in (lambda s: self._listen_loop(s, listener), self._mic_loop, self._worker_loop)]
        for t in threads:
            t.start()
        next_hello = 0.0
        try:
            while not stop.is_set():
                with self._lock:
                    self._display.tick()
                if send_hello and time.monotonic() >= next_hello:
                    send_hello()
                    next_hello = time.monotonic() + hello_seconds
                stop.wait(TICK_S)
        except KeyboardInterrupt:
            say("終わります。")
        finally:
            stop.set()
            for t in threads:
                t.join(timeout=3)
            listener.close()

    def _listen_loop(self, stop, sock):
        while not stop.is_set():
            try:
                data, _ = sock.recvfrom(65535)
            except socket.timeout:
                continue
            except OSError:
                if stop.is_set():
                    return
                time.sleep(0.5)
                continue
            try:
                self.on_osc(*kat_osc.parse_message(data))
            except ValueError:
                pass  # not OSC: ignore

    def _mic_loop(self, stop):
        while not stop.is_set():
            if not self.active:
                stop.wait(0.05)
                continue
            try:
                mic = self._mic_factory()
            except voice.MicError as error:
                say(str(error))
                stop.wait(2)
                continue
            try:
                for frame in mic.frames():
                    if stop.is_set() or not self.active:
                        break
                    self.feed_frame(frame)
                else:
                    if not stop.is_set():
                        say("マイクの音が止まりました。2 秒後に開き直します。")
                        stop.wait(2)
            except Exception as error:
                say("マイクのエラー: %s" % error)
                stop.wait(2)
            finally:
                mic.close()

    def _worker_loop(self, stop):
        while not stop.is_set():
            try:
                self.process_next(timeout=0.2)
            except Exception as error:
                say("予期しないエラー: %s" % error)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    try:
        settings = settings_mod.load_settings(os.path.join(here, "settings.ini"))
    except ValueError as error:
        print(error)
        return 1
    key = os.environ.get("GEMINI_API_KEY", "").strip()
    if not key:
        print("GEMINI_API_KEY がありません。次を実行してから、もう一度起動してください:")
        print('  export GEMINI_API_KEY="（Google AI Studio で作ったキー）"')
        return 1

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    relay = (settings.pc_ip, settings.relay_port)

    def send(packet):
        sock.sendto(packet, relay)

    def hello():
        try:
            send(kat_osc.osc_message(HELLO, True))
        except OSError:
            pass  # no Wi-Fi right now; try again later

    disp = display_mod.Display(send, settings.display, settings.kat_sync, settings.show_original)
    app = App(settings, voice.Microphone,
              lambda wav: gemini.translate(wav, settings.model, key), disp)
    say("起動しました。PC: %s:%d / モデル: %s" % (settings.pc_ip, settings.relay_port, settings.model))
    say("アバターのメニューで「翻訳」をオンにすると、聞き始めます。Ctrl+C で終わります。")
    app.run(send_hello=hello)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
