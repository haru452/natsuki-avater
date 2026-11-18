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
        using var reazon = ReazonSpeech.IsIn(reazonDir) ? new ReazonSpeech(reazonDir) : null;
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
        var display = new KatDisplay(Send, settings.Display, settings.KatSync, settings.ShowOriginal, Now, onEvent: screen.Say);
        var log = new TranslationLog(Path.Combine(DataDir, "translation_log.tsv"));
        var petCall = new PetCall(Send, Now);
        var pipeline = new OwnVoice(settings, recorder, deepl, display, kana, log.Append, Now, screen.Say, petCall);

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

        using var mic = new MicSource(new FrameSink(pipeline.FeedFrame));
        var cpu = new CpuMeter();
        double nextScreen = 0;
        while (!stop.IsCancellationRequested)
        {
            lock (pipeline.DisplayLock) display.Tick();
            petCall.Tick();
            if (Now() >= nextScreen)
            {
                nextScreen = Now() + 1;
                mic.Update(pipeline.Active); // the microphone is open only while the pet is out (ペット)
                screen.Show(new[]
                {
                    "VRC Translator PC（K: DeepL のキー / L: 相手の言語 / R: 声の保存 / Ctrl+C: 終わる）",
                    "",
                    "ペット:      " + (pipeline.Active ? "オン（聞いています）" : "オフ（アバターの「ペット」で切り替え）"),
                    "翻訳:        " + (pipeline.Translating ? "オン" : "オフ（名前を呼ぶと反応だけします）"),
                    "マイク:      " + mic.State,
                    "DeepL キー:  " + Settings.MaskKey(settings.DeepLKey),
                    "相手の言語:  " + (settings.TargetLang == Lang.ZH ? "中国語（チャットに出ます）" : "英語"),
                    "音声認識:    " + (reazon != null ? "SenseVoice + 日本語は ReazonSpeech" : "SenseVoice"),
                    "声の保存:    " + (recorder.On ? $"オン（{recorder.Saved} 文、{SampleRecorder.Dir}）" : "オフ"),
                    $"CPU:         {cpu.Percent():0.0} %",
                });
                HandleKeys(settings, screen, recorder);
            }
            stop.Token.WaitHandle.WaitOne(50);
        }
        fromVrchat.Close();
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
    static int TestWav(string path, IRecognizer stt, ITranslator deepl, IKana kana, Settings settings)
    {
        var pcm = Wav.Read(path);
        var sw = Stopwatch.StartNew();
        var heard = stt.Recognize(pcm);
        Console.WriteLine($"認識 {sw.Elapsed.TotalSeconds:0.00} 秒: [{heard.Lang}] {heard.Text}");
        if (heard.Lang == Lang.JA) Console.WriteLine("かな: " + kana.ToKana(heard.Text));
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
    bool paused;
    bool cleared;

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

    public void Show(string[] lines)
    {
        lock (gate)
        {
            if (paused || Console.IsOutputRedirected) return;
            if (!cleared) { Console.Clear(); cleared = true; }
            Console.SetCursorPosition(0, 0);
            int width = Math.Max(20, Console.WindowWidth - 1);
            foreach (var l in lines.Concat(new[] { "", "― 最近のできごと ―" }).Concat(messages))
                Console.WriteLine(l.Length > width ? l[..width] : l.PadRight(width));
        }
    }
}
