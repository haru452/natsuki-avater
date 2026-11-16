import io
import math
import struct
import sys
import unittest
import wave

sys.path.insert(0, __file__.rsplit("phone", 1)[0])
import voice  # noqa: E402

SAMPLES = voice.RATE * voice.FRAME_MS // 1000


def frames(seconds, amplitude, freq=220.0):
    """30 ms frames of a sine wave; amplitude 0 gives digital silence."""
    out = []
    n = round(seconds * 1000 / voice.FRAME_MS)
    for f in range(n):
        samples = [
            int(amplitude * math.sin(2 * math.pi * freq * (f * SAMPLES + i) / voice.RATE))
            for i in range(SAMPLES)
        ]
        out.append(struct.pack("<%dh" % SAMPLES, *samples))
    return out


def feed_all(seg, chunks):
    return [pcm for pcm in (seg.feed(c) for c in chunks) if pcm is not None]


def seconds_of(pcm):
    return len(pcm) / 2 / voice.RATE


QUIET = 60      # room noise
SPEECH = 6000   # talking


class RmsTests(unittest.TestCase):
    def test_rms_of_sine(self):
        self.assertAlmostEqual(voice.rms(frames(0.03, 1000)[0]), 1000 / math.sqrt(2), delta=15)

    def test_rms_of_silence_and_empty(self):
        self.assertEqual(voice.rms(frames(0.03, 0)[0]), 0.0)
        self.assertEqual(voice.rms(b""), 0.0)


class SegmenterTests(unittest.TestCase):
    def test_one_utterance_with_preroll(self):
        seg = voice.Segmenter()
        got = feed_all(seg, frames(1, QUIET) + frames(1, SPEECH) + frames(1, QUIET))
        self.assertEqual(len(got), 1)
        # 0.3 s before the start + 1 s of speech + a short tail, trailing silence trimmed
        self.assertGreater(seconds_of(got[0]), 1.25)
        self.assertLess(seconds_of(got[0]), 1.6)
        self.assertFalse(seg.recording)

    def test_waits_for_08_seconds_of_silence(self):
        seg = voice.Segmenter()
        got = feed_all(seg, frames(1, QUIET) + frames(1, SPEECH) + frames(0.6, QUIET))
        self.assertEqual(got, [])
        self.assertTrue(seg.recording, "a short pause is part of the same sentence")
        got = feed_all(seg, frames(0.3, QUIET))
        self.assertEqual(len(got), 1)

    def test_pause_inside_sentence_keeps_one_utterance(self):
        seg = voice.Segmenter()
        chunks = frames(1, QUIET) + frames(1, SPEECH) + frames(0.5, QUIET) + frames(1, SPEECH) + frames(1, QUIET)
        self.assertEqual(len(feed_all(seg, chunks)), 1)

    def test_short_noise_is_dropped(self):
        seg = voice.Segmenter()
        self.assertEqual(feed_all(seg, frames(1, QUIET) + frames(0.3, SPEECH) + frames(1, QUIET)), [])

    def test_two_utterances(self):
        seg = voice.Segmenter()
        chunks = (frames(1, QUIET) + frames(1, SPEECH) + frames(1, QUIET)) * 2
        self.assertEqual(len(feed_all(seg, chunks)), 2)

    def test_long_speech_is_cut_at_max(self):
        seg = voice.Segmenter(max_s=5)
        got = feed_all(seg, frames(0.5, QUIET) + frames(6, SPEECH))
        self.assertEqual(len(got), 1)
        self.assertAlmostEqual(seconds_of(got[0]), 5, delta=0.05)

    def test_noise_floor_follows_a_louder_room(self):
        seg = voice.Segmenter()
        feed_all(seg, frames(1, QUIET))
        quiet_floor = seg.noise_floor
        feed_all(seg, frames(0.1, 400))  # a fan switches on: too short to count as speech
        feed_all(seg, frames(4, 400))
        self.assertGreater(seg.noise_floor, quiet_floor * 3)
        self.assertEqual(feed_all(seg, frames(2, 400)), [], "steady noise is not speech")

    def test_reset_discards_a_recording(self):
        seg = voice.Segmenter()
        feed_all(seg, frames(1, QUIET) + frames(1, SPEECH))
        self.assertTrue(seg.recording)
        seg.reset()
        self.assertFalse(seg.recording)
        self.assertEqual(feed_all(seg, frames(1, QUIET)), [])


class WavTests(unittest.TestCase):
    def test_wav_round_trip(self):
        pcm = b"".join(frames(0.3, 1000))
        with wave.open(io.BytesIO(voice.to_wav(pcm))) as w:
            self.assertEqual((w.getnchannels(), w.getsampwidth(), w.getframerate()), (1, 2, voice.RATE))
            self.assertEqual(w.readframes(w.getnframes()), pcm)


class MicrophoneTests(unittest.TestCase):
    def test_reads_whole_frames_from_the_command(self):
        script = "import sys; sys.stdout.buffer.write(bytes(%d)); sys.stdout.flush()" % (voice.FRAME_BYTES * 3 + 10)
        mic = voice.Microphone([sys.executable, "-c", script])
        try:
            got = list(mic.frames())
        finally:
            mic.close()
        self.assertEqual(len(got), 3)
        self.assertTrue(all(len(f) == voice.FRAME_BYTES for f in got))

    def test_missing_command_raises_mic_error(self):
        with self.assertRaises(voice.MicError):
            voice.Microphone(["definitely-not-a-real-command-xyz"])


if __name__ == "__main__":
    unittest.main()
