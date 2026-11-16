"""Send one line of text to the KAT display on your avatar.

    python kat_send_test.py "Good morning"
"""
import argparse

import kat_osc


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("text")
    parser.add_argument("--host", default="127.0.0.1", help="PC running VRChat")
    parser.add_argument("--port", type=int, default=9000)
    parser.add_argument("--sync", type=int, default=8, choices=kat_osc.SYNC_CHOICES,
                        help="must match the prefab's KAT sync count")
    args = parser.parse_args()
    kat_osc.send(args.text, args.host, args.port, args.sync)
    print(f"sent {len(kat_osc.encode_text(args.text))} characters to {args.host}:{args.port}")


if __name__ == "__main__":
    main()
