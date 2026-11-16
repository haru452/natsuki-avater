"""Check that Termux can hear the microphone: shows a loudness bar and when a sentence is detected.

    python mic_check.py              (Ctrl+C to stop)
    python mic_check.py --seconds 20

Talk, then stay quiet for a second. "● 録音" appears while you talk and
"✔ 1 回分" when a sentence ends. Try it with the screen on and with the screen off.
"""
import argparse
import time

import voice

BAR_WIDTH = 40
FULL_SCALE = 8000.0


def main():
    parser = argparse.ArgumentParser(description="マイクの確認")
    parser.add_argument("--seconds", type=float, default=0, help="この秒数で終わる（0 なら Ctrl+C まで）")
    args = parser.parse_args()

    try:
        mic = voice.Microphone()
    except voice.MicError as error:
        print(error)
        print("pulseaudio が動いているか確かめてください（README の手順 3）。")
        return 1

    seg = voice.Segmenter()
    started = time.monotonic()
    count = 0
    frames = 0
    try:
        for frame in mic.frames():
            frames += 1
            pcm = seg.feed(frame)
            if pcm is not None:
                count += 1
                print("\n✔ %d 回分（%.1f 秒）" % (count, len(pcm) / 2 / voice.RATE))
            if frames % 3 == 0:  # about 10 updates per second
                level = voice.rms(frame)
                filled = min(BAR_WIDTH, int(level / FULL_SCALE * BAR_WIDTH))
                mark = "● 録音" if seg.recording else "      "
                print("\r%5d |%-*s| 雑音 %4d しきい値 %4d %s" % (
                    level, BAR_WIDTH, "#" * filled, seg.noise_floor, seg.threshold, mark), end="", flush=True)
            if args.seconds and time.monotonic() - started > args.seconds:
                break
        else:
            print("\nマイクの音が止まりました。pulseaudio を起動し直してください。")
    except KeyboardInterrupt:
        pass
    finally:
        mic.close()
    seconds = frames * voice.FRAME_MS / 1000
    print("\n%.0f 秒聞いて、%d 回分の声を見つけました。" % (seconds, count))
    if frames == 0:
        print("音が 1 つも届いていません。マイクの許可と pulseaudio を確かめてください。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
