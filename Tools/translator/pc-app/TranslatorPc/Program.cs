// translator_pc.exe: VRC Translator on the PC alone.
// PC microphone (only while the avatar's ペット toggle is on; its 翻訳 toggle turns translating on and off) → SenseVoice → DeepL → the pet's KAT text / chatbox.
// VRChat → us on 9001 (the toggle), us → VRChat on 9000.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TranslatorPc.Core;

namespace TranslatorPc;

static class Program
{
    const int FromVrchatPort = 9001;
    static readonly IPEndPoint Vrchat = new(IPAddress.Loopback, 9000);
    static readonly string DataDir = Path.GetDirectoryName(Settings.DefaultPath)!;

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        Console.Title = "VRC Translator PC";
        var settings = Settings.Load(Settings.DefaultPath);
        var screen = new Screen();
        if (args.Length >= 2 && args[0] == "--test-command") return TestCommand(args.Skip(1), settings);
        if (args.Length >= 2 && args[0] == "--test-chat") return TestChat(args.Skip(1), settings);

        // Next to the exe (AppContext.BaseDirectory is the single-file extraction folder).
        string exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        string modelDir = Path.Combine(exeDir, "models", "sense-voice");
        if (!File.Exists(Path.Combine(modelDir, "model.int8.onnx")))
            return Fail($"音声認識のモデルがありません: {modelDir}\nREADME の手順でモデルを置いてください。");

        Console.WriteLine("音声認識と辞書を読み込んでいます…");
        using var auto = new SenseVoice(modelDir);
        using var senseVoiceJa = new SenseVoice(modelDir, language: "ja");
        // Japanese is heard again by ReazonSpeech when it is there (much better at casual Japanese).
        string reazonDir = Path.Combine(exeDir, "models", "reazon");
        using var reazon = ReazonSpeech.IsIn(reazonDir)
            ? new ReazonSpeech(reazonDir, hotwords: settings.PetNames, hotwordsFile: Path.Combine(DataDir, "hotwords.txt"))
            : null;
        using var kana = new MeCabKana();
        kana.ToKana("準備"); // load the dictionary now, not on the first sentence
        var recorder = new SampleRecorder(reazon != null
            ? new JaEnRecognizer(auto, reazon, refineJapanese: true)
            : new JaEnRecognizer(auto, senseVoiceJa));
        var deepl = new DeepLClient(() => settings.DeepLKey);

        if (args.Length == 2 && args[0] == "--test-wav") return TestWav(args[1], recorder, deepl, kana, settings);
        if (args.Length >= 1 && args[0] == "--compare")
        {
            var list = new List<(string, IRecognizer)> { ("SenseVoice", auto), ("SenseVoice-ja", senseVoiceJa) };
            if (reazon != null) list.Add(("ReazonSpeech", reazon));
            return Compare.Run(args.Length == 2 ? args[1] : SampleRecorder.Dir, list);
        }

        UdpClient fromVrchat;
        try
        {
            fromVrchat = new UdpClient(new IPEndPoint(IPAddress.Any, FromVrchatPort));
        }
        catch (SocketException e)
        {
            return Fail($"ポート {FromVrchatPort} が使われています（{e.Message}）。\nこのプログラムがもう開いていないか、古い中継が動いていないか確かめてください。");
        }
        using var toVrchat = new UdpClient();
        void Send(byte[] p) { try { toVrchat.Send(p, p.Length, Vrchat); } catch (SocketException) { } }

        var clock = Stopwatch.StartNew();
        double Now() => clock.Elapsed.TotalSeconds;
        var display = new KatDisplay(Send, settings.Display, settings.KatSync, settings.ShowOriginal, Now, onEvent: screen.Say,
            holdSeconds: settings.KatHoldSeconds);
        var log = new TranslationLog(Path.Combine(DataDir, "translation_log.tsv"));
        var petCall = new PetCall(Send, Now);
        var chat = new PetChat(() => settings.GeminiKey, (url, body) => GeminiHttp.Call(url, body, settings.GeminiKey));
        var pipeline = new OwnVoice(settings, recorder, deepl, display, kana, log.Append, Now, screen.Say, petCall, chat);
        // Other players' voices, translated into Japanese on the phone's page, only while the page is open.
        var feed = new OthersFeed(() => DateTime.Now);
        var others = new OthersVoice(auto, deepl, feed, Now, screen.Say, () => settings.TranslateOthers);
        string phoneUrl = PhoneServer.Url() ?? "（ネットワークが見つかりません）";
        PhoneServer? phoneServer = null;
        try { phoneServer = new PhoneServer(feed); }
        catch (SocketException e) { phoneUrl = $"（ポート {OthersFeed.Port} が使えません: {e.Message}）"; }

        var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        Background("vrchat-osc", () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    var (address, oscArgs) = Osc.Parse(fromVrchat.Receive(ref from));
                    pipeline.OnOsc(address, oscArgs);
                }
                catch (FormatException) { }
                catch (SocketException) { if (stop.IsCancellationRequested) return; }
                catch (ObjectDisposedException) { return; }
            }
        });
        Background("translate", () => { while (!stop.IsCancellationRequested) pipeline.ProcessNext(300); });
        Background("others", () => { while (!stop.IsCancellationRequested) others.ProcessNext(300); });

        using var mic = new MicSource(new FrameSink(pipeline.FeedFrame));
        using var vrchatSound = new VrchatSource(new FrameSink(others.FeedFrame));
        var cpu = new CpuMeter();
        // Her name flag and the subtitle's timing run on their own: the screen's thread stops while text in the console
        // window is selected (Windows holds the output), and she must not keep smiling then.
        Background("pet", () =>
        {
            while (!stop.IsCancellationRequested)
            {
                lock (pipeline.DisplayLock) display.Tick();
                petCall.Tick();
                stop.Token.WaitHandle.WaitOne(50);
            }
        });
        double nextScreen = 0;
        while (!stop.IsCancellationRequested)
        {
            if (Now() >= nextScreen)
            {
                nextScreen = Now() + 1;
                mic.Update(pipeline.Active); // the microphone is open only while the pet is out (ペット)
                bool phoneOpen = feed.HasViewer;
                others.Listening = phoneOpen;
                vrchatSound.Update(phoneOpen); // VRChat's sound is heard only while the phone's page is open
                screen.Show(new[]
                {
                    "VRC Translator PC（K: DeepL のキー / G: Gemini のキー / L: 相手の言語 / R: 声の保存 / Ctrl+C: 終わる）",
                    "",
                    "ペット:      " + (pipeline.Active ? "オン（聞いています）" : "オフ（アバターの「ペット」で切り替え）"),
                    "翻訳:        " + (pipeline.Translating ? "オン" : "オフ（名前を呼ぶと反応だけします）"),
                    "会話:        " + (!settings.PetChat ? "オフ"
                        : settings.GeminiKey.Trim().Length == 0 ? "Gemini のキーがありません（G キーで入れてください）"
                        : "名前を呼ぶと、ほまれが英語で返事します（Gemini" + (chat.Model != null ? ": " + chat.Model : "") + "）"),
                    "マイク:      " + mic.State,
                    "DeepL キー:  " + Settings.MaskKey(settings.DeepLKey),
                    "相手の言語:  " + (settings.TargetLang == Lang.ZH ? "中国語（チャットに出ます）" : "英語"),
                    "音声認識:    " + (reazon != null ? "SenseVoice + 日本語は ReazonSpeech" : "SenseVoice")
                        + (reazon?.Hotwords.Count > 0 ? "（聞き取りやすくする名前: " + string.Join("・", reazon.Hotwords) + "）" : ""),
                    "声の保存:    " + (recorder.On ? $"オン（{recorder.Saved} 文、{SampleRecorder.Dir}）" : "オフ"),
                    "相手の声:    " + (phoneOpen
                        ? (settings.TranslateOthers ? "スマホに表示中（日本語以外を DeepL で訳しています）" : "スマホに表示中（話したそのまま）")
                        : "スマホで開くと出します: " + phoneUrl),
                    "VRChat の音: " + vrchatSound.State,
                    $"CPU:         {cpu.Percent():0.0} %",
                });
                HandleKeys(settings, screen, recorder);
            }
            stop.Token.WaitHandle.WaitOne(50);
        }
        fromVrchat.Close();
        phoneServer?.Dispose();
        return 0;
    }

    static void HandleKeys(Settings settings, Screen screen, SampleRecorder recorder)
    {
        while (!Console.IsInputRedirected && Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true).Key;
            if (key == ConsoleKey.R) recorder.On = !recorder.On; // saves each sentence's sound (to compare recognizers)
            else if (key == ConsoleKey.L)
            {
                settings.TargetLang = settings.TargetLang == Lang.ZH ? Lang.EN : Lang.ZH;
                settings.Save(Settings.DefaultPath);
            }
            else if (key == ConsoleKey.G)
            {
                screen.Pause();
                Console.WriteLine("Gemini の API キーを貼り付けて Enter（空のまま Enter でやめる）:");
                var entered = Console.ReadLine()?.Trim() ?? "";
                if (entered.Length > 0)
                {
                    settings.GeminiKey = entered;
                    settings.Save(Settings.DefaultPath);
                }
                screen.Resume();
            }
            else if (key == ConsoleKey.K)
            {
                screen.Pause();
                Console.WriteLine("DeepL の API キーを貼り付けて Enter（空のまま Enter でやめる）:");
                var entered = Console.ReadLine()?.Trim() ?? "";
                if (entered.Length > 0)
                {
                    settings.DeepLKey = entered;
                    settings.Save(Settings.DefaultPath);
                }
                screen.Resume();
            }
        }
    }

    /// <summary>Recognize and translate one WAV file (16 kHz mono 16-bit) and print the result: a check without VRChat.</summary>
    /// <summary>--test-command "誉、座って" …: the kana reading, and whether her name and a command are heard (no microphone).</summary>
    static int TestCommand(IEnumerable<string> sentences, Settings settings)
    {
        using var kana = new MeCabKana();
        foreach (var text in sentences)
        {
            var reading = kana.ToKana(text);
            bool name = PetCall.Heard(text, reading, settings);
            var command = PetCommands.Find(text, reading, settings.PetCommandWords, settings.PetCallNames);
            Console.WriteLine($"{text} → かな: {reading} / 名前: {(name ? "あり" : "なし")} / 命令: "
                              + (command is PetCommand c ? PetCommands.Describe(c) : "なし"));
        }
        return 0;
    }

    /// <summary>--test-chat "ちゃん、元気？" …: her answers through Gemini (needs the key; no microphone or VRChat).</summary>
    static int TestChat(IEnumerable<string> sentences, Settings settings)
    {
        var chat = new PetChat(() => settings.GeminiKey, (url, body) => GeminiHttp.Call(url, body, settings.GeminiKey));
        foreach (var text in sentences)
        {
            try { Console.WriteLine($"{text} → ほまれ: {chat.Reply(text)}（{chat.Model}）"); }
            catch (Exception e) when (e is PetChatException or HttpRequestException or TaskCanceledException)
            {
                Console.WriteLine($"{text} → 返事なし: {e.Message}");
            }
        }
        return 0;
    }

    static int TestWav(string path, IRecognizer stt, ITranslator deepl, IKana kana, Settings settings)
    {
        var pcm = Wav.Read(path);
        var sw = Stopwatch.StartNew();
        var heard = stt.Recognize(pcm);
        Console.WriteLine($"認識 {sw.Elapsed.TotalSeconds:0.00} 秒: [{heard.Lang}] {heard.Text}");
        if (heard.Lang == Lang.JA) Console.WriteLine("かな: " + kana.ToKana(heard.Text));
        // What the pet would make of it (her name in any recognizer's text, the command in the chosen one).
        var reading = heard.Lang == Lang.JA ? kana.ToKana(heard.Text) : "";
        var also = string.Join(" ", heard.AllTexts.Skip(1));
        bool name = PetCall.Heard(heard.Text, reading, settings) || PetCall.Heard(also, "", settings);
        var command = PetCommands.Find(heard.Text, reading, settings.PetCommandWords, settings.PetCallNames);
        Console.WriteLine($"ペット: 名前 {(name ? "あり" : "なし")} / 命令 {(command is PetCommand c ? PetCommands.Describe(c) : "なし")}"
                          + (also.Length > 0 ? $"（もう一方の認識: {also}）" : ""));
        if (heard.Lang != Lang.JA)
        {
            Console.WriteLine("そのまま出します（日本語には訳しません）: " + heard.Text);
            return 0;
        }
        try
        {
            sw.Restart();
            Console.WriteLine($"DeepL {sw.Elapsed.TotalSeconds:0.00} 秒: " + deepl.Translate(heard.Text, heard.Lang, settings.TargetLang));
        }
        catch (Exception e) when (e is DeepLException or HttpRequestException or TaskCanceledException)
        {
            Console.WriteLine("DeepL: " + e.Message);
        }
        return 0;
    }

    static void Background(string name, Action body) => new Thread(() => body()) { IsBackground = true, Name = name }.Start();

    static int Fail(string message)
    {
        Console.WriteLine(message);
        Console.WriteLine("Enter で終わります。");
        if (!Console.IsInputRedirected) Console.ReadLine();
        return 1;
    }
}

/// <summary>Gemini over HTTPS. The key goes only in the x-goog-api-key header.</summary>
static class GeminiHttp
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static (int Status, string Json) Call(string url, string? body, string key)
    {
        using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, url);
        if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.Add("x-goog-api-key", key.Trim());
        using var response = Http.Send(request);
        using var reader = new StreamReader(response.Content.ReadAsStream(), Encoding.UTF8);
        return ((int)response.StatusCode, reader.ReadToEnd());
    }
}

/// <summary>This process's CPU use since the last call, as a share of the whole machine.</summary>
sealed class CpuMeter
{
    readonly Process self = Process.GetCurrentProcess();
    TimeSpan lastCpu;
    DateTime lastWall = DateTime.UtcNow;

    public double Percent()
    {
        self.Refresh();
        var cpu = self.TotalProcessorTime;
        var wall = DateTime.UtcNow;
        double used = (cpu - lastCpu).TotalSeconds / Math.Max(0.001, (wall - lastWall).TotalSeconds) / Environment.ProcessorCount * 100;
        lastCpu = cpu;
        lastWall = wall;
        return used;
    }
}

/// <summary>One line per translation, tab-separated. Never holds audio or keys.</summary>
sealed class TranslationLog
{
    readonly string path;
    readonly object gate = new();

    public TranslationLog(string path) => this.path = path;

    public void Append(Lang source, string original, string translation)
    {
        static string Clean(string s) => string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        lock (gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, string.Join("\t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "self",
                source.ToString().ToLowerInvariant(), Clean(original), Clean(translation), "deepl") + "\n", Encoding.UTF8);
        }
    }
}

/// <summary>The status lines redrawn in place, plus the latest messages under them.</summary>
sealed class Screen
{
    readonly List<string> messages = new();
    readonly object gate = new();
    readonly object writing = new();
    bool paused;
    volatile bool cleared;

    public void Say(string message)
    {
        lock (gate)
        {
            messages.Add(DateTime.Now.ToString("HH:mm:ss ") + message);
            if (messages.Count > 10) messages.RemoveAt(0);
        }
        if (Console.IsOutputRedirected) Console.WriteLine(message);
    }

    public void Pause() { lock (gate) { paused = true; Console.Clear(); } }

    public void Resume() { lock (gate) { paused = false; cleared = false; } }

    /// <summary>
    /// Writes outside the message lock: while text in the window is selected the write waits, and Say (called by the
    /// translating threads) must not wait with it.
    /// </summary>
    public void Show(string[] lines)
    {
        List<string> recent;
        lock (gate)
        {
            if (paused || Console.IsOutputRedirected) return;
            recent = messages.ToList();
        }
        lock (writing)
        {
            if (!cleared) { Console.Clear(); cleared = true; }
            Console.SetCursorPosition(0, 0);
            // In columns (Japanese takes two), and no taller than the window: a wrapped or overlong screen scrolls
            // on every redraw and the heading piles up.
            int width = Math.Max(20, Console.WindowWidth - 1);
            var status = lines.Concat(new[] { "", "― 最近のできごと ―" }).ToList();
            foreach (var l in ConsoleText.Lines(status, recent, Console.WindowHeight))
                Console.WriteLine(ConsoleText.Fit(l, width));
        }
    }
}
