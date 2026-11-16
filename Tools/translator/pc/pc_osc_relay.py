"""PC-side relay between VRChat and the phone translator. Standard library only.

    python pc_osc_relay.py          (or double-click start_relay.bat)

- Phone -> PC port 9002 -> VRChat 127.0.0.1:9000 (the translated text). Also works around a firewall
  that blocks the phone from reaching VRChat's port directly.
- VRChat -> PC port 9001 -> phone port 9001, but only the translator's own values (the toggle and the
  AI switch) and avatar changes. Everything else VRChat sends (faces, gestures...) stays on the PC.
- The phone says /translator/hello every few seconds, so the relay learns where it is; no phone IP to type.
"""
import os
import selectors
import socket
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import kat_osc  # noqa: E402

HELLO = "/translator/hello"
AVATAR_CHANGE = "/avatar/change"
FORWARD_TO_PHONE = {
    kat_osc.PARAM_PREFIX + "TranslatorActive",
    kat_osc.PARAM_PREFIX + "TranslatorLocalAI",
    AVATAR_CHANGE,
}
# The phone may only write text: the KAT values and the chatbox. Nothing that moves or changes the avatar.
FROM_PHONE_PREFIXES = (kat_osc.PARAM_PREFIX + "KAT_", "/chatbox/")


class Relay:
    """Decides where each packet goes. No sockets here, so it can be tested on its own."""

    def __init__(self, vrchat=("127.0.0.1", 9000), phone_port=9001):
        self.vrchat = vrchat
        self.phone_port = phone_port
        self.phone = None
        self._session = None
        self._latest = {}  # address -> last packet, for a phone that (re)joins

    def from_phone(self, data, addr):
        try:
            address, args = kat_osc.parse_message(data)
        except ValueError:
            return []
        if address == HELLO:
            phone = (addr[0], self.phone_port)
            session = args[0] if args else None  # changes each time the phone script starts
            if phone == self.phone and session == self._session:
                return []  # already up to date; values go straight to it as they change
            self.phone = phone
            self._session = session
            return [(packet, self.phone) for packet in self._latest.values()]
        if not address.startswith(FROM_PHONE_PREFIXES):
            return []
        return [(data, self.vrchat)]

    def from_vrchat(self, data):
        try:
            address, _ = kat_osc.parse_message(data)
        except ValueError:
            return []
        if address not in FORWARD_TO_PHONE:
            return []
        if address == AVATAR_CHANGE:
            self._latest.clear()  # a new avatar starts with the translator off
        else:
            self._latest[address] = data
        return [(data, self.phone)] if self.phone else []


def describe(data):
    try:
        address, args = kat_osc.parse_message(data)
    except ValueError:
        return "?"
    return "%s %s" % (address.rsplit("/", 1)[-1], args[0] if args else "")


def receive(sock):
    """One packet, or None when Windows reports that an earlier send went nowhere (WinError 10054)."""
    try:
        return sock.recvfrom(65535)
    except ConnectionResetError:
        return None


def main(phone_listen=("0.0.0.0", 9002), vrchat_listen=("127.0.0.1", 9001)):
    from_phone = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    from_vrchat = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        from_phone.bind(phone_listen)
        from_vrchat.bind(vrchat_listen)
    except OSError as error:
        print("ポートを開けません: %s" % error)
        print("ほかのアプリ（OSC を使うツールや、もう1つの中継スクリプト）が 9001 か 9002 番を使っていないか確かめてください。")
        input("Enter で終わります")
        return 1

    relay = Relay()
    sel = selectors.DefaultSelector()
    sel.register(from_phone, selectors.EVENT_READ, "phone")
    sel.register(from_vrchat, selectors.EVENT_READ, "vrchat")
    print("中継を始めました。スマホの翻訳スクリプトを起動してください（Ctrl+C で終わります）。")
    known_phone = None
    to_vrchat = 0
    last_report = time.monotonic()
    try:
        while True:
            for key, _ in sel.select(timeout=1.0):
                packet_in = receive(key.fileobj)
                if packet_in is None:
                    continue
                data, addr = packet_in
                if key.data == "phone":
                    outgoing = relay.from_phone(data, addr)
                    if relay.phone != known_phone:
                        known_phone = relay.phone
                        print("スマホを見つけました: %s" % known_phone[0])
                    to_vrchat += sum(1 for _, dest in outgoing if dest == relay.vrchat)
                else:
                    outgoing = relay.from_vrchat(data)
                    for packet, _ in outgoing:
                        print("VRChat → スマホ: %s" % describe(packet))
                for packet, dest in outgoing:
                    try:
                        from_phone.sendto(packet, dest)
                    except OSError:
                        pass  # Wi-Fi hiccup: drop it and carry on
            if to_vrchat and time.monotonic() - last_report > 10:
                print("スマホ → VRChat: %d 件" % to_vrchat)
                to_vrchat = 0
                last_report = time.monotonic()
    except KeyboardInterrupt:
        print("終わります。")
    finally:
        from_phone.close()
        from_vrchat.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
