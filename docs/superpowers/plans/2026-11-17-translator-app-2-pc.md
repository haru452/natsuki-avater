# 翻訳アプリ 計画 2：PC のプログラム（中継と音）と、PC のマイクで自分の声 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** PC の `translator_pc.exe`（デスクトップのショートカットから起動）が、OSC の中継をし、自分のマイクの音と VRChat の音をスマホへ送る。スマホは自分の声を PC のマイクの音から訳す。

**Architecture:** C#（.NET 8）の 1 つの exe。判断（中継の振り分け、hello、何をいつ送るか、パケットの形、音の変換）は `TranslatorPc.Core` に置いて xUnit で確かめる。録音（NAudio の WASAPI）とソケットは `TranslatorPc` の薄いつなぎ。スマホ側は `AudioReceiver`（UDP 9003）を足し、設定「自分の声のマイク: PC / スマホ」で自分の声の入り口を選ぶ。

**Tech Stack:** .NET 8 SDK（利用者のフォルダに入れる）、NAudio 2.2.1、xUnit。スマホ側は計画 1 と同じ（Kotlin、JUnit）

**Spec:** `docs/superpowers/specs/2026-11-17-translator-app-design.md`（末尾「自分の声は PC のマイクから」が最新）。中継の決まりは `Tools/translator/pc/pc_osc_relay.py` とそのテスト

## Global Constraints

- 置き場所: `Tools/translator/pc-app/`（`TranslatorPc.sln`、`TranslatorPc/`、`TranslatorPc.Core/`、`TranslatorPc.Tests/`）。`bin/`、`obj/`、`publish/` は git に入れない
- 1 ファイル 600 行以内
- ポート: スマホ → PC 9002（OSC）、VRChat → PC 9001、PC → VRChat 127.0.0.1:9000、PC → スマホ 9001（OSC）、PC → スマホ 9003（音）
- hello: `/translator/hello`、引数 `(int session, bool othersOn, bool pcMic)`。PC は hello ごとに `/translator/ack True` を返す
- 音のパケット: `VTA1`（4 バイト）＋種類 1 バイト（0 = 自分のマイク、1 = VRChat の音）＋通し番号 4 バイト（ビッグエンディアン）＋ 320 サンプル（640 バイト、リトルエンディアン 16 ビット）。16 kHz、モノラル
- 自分のマイクの音を送るのは、スマホが見つかっていて、hello の `pcMic` が真で、`TranslatorActive` が真のときだけ。VRChat の音は `othersOn` が真のときだけ。最後の hello から 15 秒で、どちらも止める
- VRChat の音は `VRChat.exe` のプロセス ループバック録音（Windows 10 2004 以降）。VRChat が起動していなければ待ち、起動したら録り始める
- PC の CPU はおよそ 2% 以内
- 自動起動はしない。デスクトップにショートカット「VRC Translator PC」を置く

テストの実行: PC 側 `dotnet test Tools/translator/pc-app`、スマホ側 `cd Tools/translator/android && ./gradlew testDebugUnitTest`。

## Review Focus

1. **マイクの種類が 48 kHz ステレオ、44.1 kHz、32 ビット浮動小数** → どれでも 16 kHz モノラル 16 ビットになる → Task 3 のテスト
2. **VRChat を後から起動・途中で終了** → 落ちずに待ち、また録る → Task 5 の実機確認と、Task 3 の `ProcessWatcher` のテスト
3. **スマホの IP が変わる・スマホのアプリを止める** → 15 秒で送るのをやめる。新しい IP に送り直す → Task 2 のテスト
4. **パケットの抜け・順番の入れ替わり** → スマホは古い番号を捨て、抜けは無音で埋めない（そのまま続ける）→ Task 4 のテスト
5. **翻訳がオフのとき** → 自分のマイクの音は 1 パケットも出ない → Task 2 のテスト

---

### Task 1: .NET の準備と、中継の移植（`pc_osc_relay.py` → C#）

**Files:**
- 準備: `.NET 8 SDK` を `dotnet-install.ps1 -Channel 8.0 -InstallDir $env:USERPROFILE\.dotnet` で入れる（管理者権限なし）
- Create: `Tools/translator/pc-app/` のソリューション一式、`.gitignore`
- Create: `TranslatorPc.Core/Osc.cs`（`Tools/translator/kat_osc.py` の `osc_message` / `parse_message` の移植。KAT の文字表は要らない）、`TranslatorPc.Core/Relay.cs`、`TranslatorPc.Tests/OscTests.cs`、`TranslatorPc.Tests/RelayTests.cs`

**Interfaces:**
- `static class Osc { byte[] Message(string address, params object[] values); (string Address, object[] Args) Parse(byte[] data); }`（壊れていたら `FormatException`）
- `record Phone(IPAddress Address, int Session, bool OthersOn, bool PcMic, DateTime LastHello);`
- `class Relay { Relay(IPEndPoint vrchat, int phonePort = 9001); Phone? Phone { get; } bool TranslatorActive { get; } List<(byte[] Packet, IPEndPoint To)> FromPhone(byte[] data, IPEndPoint from, DateTime now); List<(byte[] Packet, IPEndPoint To)> FromVrchat(byte[] data); bool PhoneAlive(DateTime now); }`
  - `FromPhone` の hello は ack を必ず返し、新しいスマホ（IP か session が変わった）には覚えている値を送り直す。KAT と `/chatbox/` だけを VRChat へ
  - `PhoneAlive`: 最後の hello から 15 秒以内

- [ ] **Step 1:** `Tools/translator/pc/test_relay.py` の全テストと `test_kat_osc.py` の OSC の部分を xUnit に移す（同じ値）。加えて `HelloCarriesFlags`（`(5, true, false)` → `Phone.OthersOn == true`、`PcMic == false`）、`PhoneIsForgottenAfter15Seconds`、`TranslatorActiveFollowsVrchat`（`/avatar/parameters/TranslatorActive True` で真、`/avatar/change` で偽）
- [ ] **Step 2:** `dotnet test` で失敗を確かめる
- [ ] **Step 3:** 書く
- [ ] **Step 4:** 通ることを確かめる
- [ ] **Step 5:** Commit（`feat(pc): .NET relay with hello flags and ack`）

---

### Task 2: 何をいつ送るか（`AudioGate`）と、音のパケット

**Files:** Create `TranslatorPc.Core/AudioPacket.cs`、`TranslatorPc.Core/AudioGate.cs`、`TranslatorPc.Tests/AudioPacketTests.cs`、`TranslatorPc.Tests/AudioGateTests.cs`

**Interfaces:**
- `enum StreamKind : byte { OwnMic = 0, Vrchat = 1 }`
- `static class AudioPacket { const int Samples = 320; byte[] Encode(StreamKind kind, uint seq, short[] samples); }`
- `class AudioGate { AudioGate(Relay relay); IPEndPoint? Destination(StreamKind kind, DateTime now); }`（送ってよいときだけスマホの `IP:9003`、ほかは `null`）

- [ ] **Step 1:** テスト: `EncodeLayout`（`VTA1`、種類、番号のビッグエンディアン、320 サンプルがリトルエンディアンで 649 バイト）、`EncodeRejectsWrongLength`。`AudioGate`: スマホなし → どちらも `null`、`pcMic` 偽 → 自分のマイク `null`、翻訳オフ → 自分のマイク `null`、`othersOn` 偽 → VRChat の音 `null`、最後の hello から 16 秒 → どちらも `null`、全部そろえば `IP:9003`
- [ ] **Step 2:** 失敗を確かめる
- [ ] **Step 3:** 書く
- [ ] **Step 4:** 通ることを確かめる
- [ ] **Step 5:** Commit（`feat(pc): audio packet and send gate`）

---

### Task 3: 音を 16 kHz モノラル 16 ビットにする部分と、VRChat の見張り

**Files:** Create `TranslatorPc.Core/AudioConverter.cs`、`TranslatorPc.Core/Framer.cs`、`TranslatorPc.Core/ProcessWatcher.cs` と各テスト

**Interfaces:**
- `class AudioConverter { AudioConverter(int sampleRate, int channels, bool isFloat, int bitsPerSample); short[] Convert(byte[] buffer, int count); }`（チャンネルを平均してモノラル、線形補間で 16 kHz、16 ビットに）
- `class Framer { Framer(int frameSamples = 320); IEnumerable<short[]> Push(short[] samples); }`（端数は次へ持ち越す）
- `class ProcessWatcher { ProcessWatcher(Func<int?> findPid); int? Poll(); }`（`findPid` が返す PID が変わったら新しい値、同じなら `null` ではなく同じ値、なくなったら `null`）

- [ ] **Step 1:** テスト: 48 kHz ステレオ浮動小数の 1 kHz 正弦波 0.1 秒 → 1600 サンプル ±1、周波数（ゼロ交差の数）1 kHz ±3%、振幅 ±5%。44.1 kHz モノラル 16 ビット → 1600 ±2 サンプル。左右が逆相のステレオ → ほぼ無音。`Framer` に 500、300、500 サンプル入れると 320 × 4 が出て残り 20。`ProcessWatcher` が PID の出現・変化・消滅を返す
- [ ] **Step 2:** 失敗を確かめる
- [ ] **Step 3:** 書く
- [ ] **Step 4:** 通ることを確かめる
- [ ] **Step 5:** Commit（`feat(pc): audio conversion, framing, VRChat watcher`）

---

### Task 4: スマホ側: PC からの音を受け取って自分の声に使う

**Files:**
- Create: `core/AudioPacket.kt`（`decode(data: ByteArray): Pair<StreamKind, Pair<Long, ShortArray>>?`、`VTA1` 以外や長さ違いは `null`）、`core/Rechunker.kt`（20 ミリ秒 320 サンプル → 30 ミリ秒 480 サンプル）、`app/AudioReceiver.kt`（UDP 9003、種類ごとに番号の古いものを捨てる）
- Modify: `core/RelayLink.kt`（hello に `pcMic`）、`core/TranslatorSettings.kt`（`var micSource: String = "PC"`）、`app/SettingsStore.kt`、`app/TranslatorService.kt`（`micSource == "PC"` なら `AudioReceiver` の自分のマイクを `feedFrame` へ。スマホのマイクは開かない）、`app/MainActivity.kt`（「自分の声のマイク: PC / スマホ」）
- Test: `AudioPacketTests.kt`、`RechunkerTests.kt`、`RelayLinkTests.kt`（hello の 3 つ目の引数）

- [ ] **Step 1:** テスト: C# と同じ形のパケットを Kotlin で作って `decode` で戻せる。`VTA2` や 600 バイトは `null`。`Rechunker` に 320 × 3 を入れると 480 × 2。hello が `(123, false, true)`。番号が 5、7、6 の順に来たら 6 は捨てる（`SequenceFilter` として `core` に置く）
- [ ] **Step 2:** 失敗を確かめる
- [ ] **Step 3:** 書く
- [ ] **Step 4:** 通ることを確かめる。`assembleDebug` も通る
- [ ] **Step 5:** Commit（`feat(android): own voice from the PC microphone`）

---

### Task 5: PC の exe、ショートカット、実機で確かめる

**Files:** Create `TranslatorPc/Program.cs`（つなぎ）、`TranslatorPc/MicSource.cs`（`WasapiCapture` 既定の録音デバイス）、`TranslatorPc/VrchatSource.cs`（`WasapiCapture.CreateForProcessCaptureAsync(pid, true)`）、`Tools/translator/pc-app/publish.ps1`（`dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` で `publish/translator_pc.exe`、デスクトップにショートカット「VRC Translator PC」を作る）、`README.md`

- `Program`: 9001 と 9002 を開く（使われていたら理由を出して Enter で終わる）。画面に、この PC の IP、スマホが見つかったか、翻訳のオン・オフ、自分のマイク・VRChat の音を送っているか（毎秒の送信数）を 1 行ずつ出す。Ctrl+C で終わる
- Python の中継（`Tools/translator/pc/`）は README で「古い版」と書き、まだ消さない（計画 4 で消す）

- [ ] **Step 1:** つなぎを書く。`dotnet test` がすべて通り、`publish.ps1` で exe とショートカットができることを確かめる
- [ ] **Step 2:** 利用者と確かめる:
  - ショートカットから起動すると、IP と「スマホを待っています」が出る
  - スマホのアプリ（マイク: PC）を開始すると「スマホが見つかった」、アプリも「中継: つながった」
  - 翻訳をオンにして PC のマイクで話す → ペットの上に訳が出る（10 文中 9 文以上）
  - 翻訳をオフにすると、自分のマイクの送信数が 0 になる
  - PC で YouTube を流しても、VRChat の音の送信には入らない（スマホで「相手の声 ON」のとき、計画 3 の前なので送信数だけ見る）
  - タスクマネージャーで CPU 2% 以内
  - 1 文の時間（アプリの記録の「時間: 認識 / 翻訳 / 待ち」）を見て、遅いときの原因を設計書に書く
- [ ] **Step 3:** 結果を設計書の末尾に追記する
- [ ] **Step 4:** Commit（`feat(pc): translator_pc.exe with mic and VRChat audio; desktop shortcut`）
