"""Microphone input and splitting it into utterances by loudness. Standard library only."""
import array
import collections
import io
import math
import subprocess
import sys
import wave

RATE = 16000
FRAME_MS = 30
FRAME_BYTES = RATE * FRAME_MS // 1000 * 2  # 16-bit mono

# PulseAudio in Termux reads the phone's microphone through module-sles-source.
PAREC_CMD = ["parec", "--raw", "--format=s16le", "--rate=%d" % RATE, "--channels=1", "--latency-msec=%d" % FRAME_MS]


class MicError(Exception):
    pass


def rms(frame):
    """Loudness of one frame of 16-bit little-endian samples."""
    if len(frame) < 2:
        return 0.0
    samples = array.array("h", frame[: len(frame) // 2 * 2])
    if sys.byteorder != "little":
        samples.byteswap()
    return math.sqrt(sum(s * s for s in samples) / len(samples))


def to_wav(pcm):
    buf = io.BytesIO()
    with wave.open(buf, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm)
    return buf.getvalue()


def _frames(seconds):
    return max(1, round(seconds * 1000 / FRAME_MS))


class Segmenter:
    """
    Feed 30 ms frames; get back one utterance (raw PCM) when the speaker stops.

    A frame is "voice" when it is `margin` times louder than the room's noise floor (and above
    floor_min * margin). Recording starts after start_s of voice, keeps preroll_s from before the
    start, ends after end_silence_s of quiet (keeping a short tail), and is cut at max_s.
    Utterances with less than min_s of voice (a cough, a knock) are dropped.
    """

    TAIL_S = 0.2

    def __init__(self, start_s=0.15, end_silence_s=0.8, preroll_s=0.3, max_s=60, min_s=0.5,
                 margin=3.0, floor_min=200.0):
        self.start_frames = _frames(start_s)
        self.end_frames = _frames(end_silence_s)
        self.max_frames = _frames(max_s)
        self.min_frames = _frames(min_s)
        self.tail_frames = _frames(self.TAIL_S)
        self.margin = margin
        self.floor_min = floor_min
        self.noise_floor = floor_min
        self._preroll = collections.deque(maxlen=_frames(preroll_s) + self.start_frames)
        self.reset()

    @property
    def threshold(self):
        return max(self.noise_floor, self.floor_min) * self.margin

    def reset(self):
        """Forget anything half-recorded (used when the toggle turns off)."""
        self.recording = False
        self._buffer = []
        self._voiced_run = 0
        self._voiced_total = 0
        self._silence = 0
        self._preroll.clear()

    def feed(self, frame):
        level = rms(frame)
        voiced = level > self.threshold
        # Follow the room: quickly while quiet, very slowly otherwise so a new steady
        # noise (a fan) eventually stops counting as voice.
        alpha = 0.05 if not voiced else 0.002
        if not self.recording:
            self.noise_floor += (level - self.noise_floor) * alpha

        if not self.recording:
            self._preroll.append(frame)
            self._voiced_run = self._voiced_run + 1 if voiced else 0
            if self._voiced_run >= self.start_frames:
                self.recording = True
                self._buffer = list(self._preroll)
                self._voiced_total = self._voiced_run
                self._silence = 0
            return None

        self._buffer.append(frame)
        if voiced:
            self._voiced_total += 1
            self._silence = 0
        else:
            self._silence += 1

        if len(self._buffer) >= self.max_frames:
            return self._finish(trim=False)
        if self._silence >= self.end_frames:
            return self._finish(trim=True)
        return None

    def _finish(self, trim):
        frames = self._buffer
        if trim:
            frames = frames[: len(frames) - (self._silence - self.tail_frames)]
        enough = self._voiced_total >= self.min_frames
        self.reset()
        return b"".join(frames) if enough else None


class Microphone:
    """Runs the capture command and yields whole 30 ms frames until it stops."""

    def __init__(self, command=None):
        try:
            self._proc = subprocess.Popen(command or PAREC_CMD, stdout=subprocess.PIPE,
                                          stderr=subprocess.DEVNULL)
        except OSError as error:
            raise MicError("マイクを開けません（%s）: %s" % ((command or PAREC_CMD)[0], error)) from error

    def frames(self):
        out = self._proc.stdout
        while True:
            frame = out.read(FRAME_BYTES)
            if len(frame) < FRAME_BYTES:
                return
            yield frame

    def close(self):
        if self._proc.poll() is None:
            self._proc.terminate()
            try:
                self._proc.wait(timeout=2)
            except subprocess.TimeoutExpired:
                self._proc.kill()
        if self._proc.stdout:
            self._proc.stdout.close()
