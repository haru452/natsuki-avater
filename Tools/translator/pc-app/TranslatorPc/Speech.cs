// The exe's real recognizer, translator and kana converter (the core only knows their interfaces).
using System.Net.Http.Headers;
using System.Text;
using NMeCab.Specialized;
using SherpaOnnx;
using TranslatorPc.Core;

namespace TranslatorPc;

/// <summary>SenseVoice (sherpa-onnx). language "" detects it; "ja" fixes it.</summary>
sealed class SenseVoice : IRecognizer, IDisposable
{
    readonly OfflineRecognizer recognizer;
    readonly object gate = new();

    public SenseVoice(string modelDir, string language = "", int threads = 2)
    {
        var config = new OfflineRecognizerConfig();
        config.ModelConfig.SenseVoice.Model = Path.Combine(modelDir, "model.int8.onnx");
        config.ModelConfig.SenseVoice.Language = language;
        config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
        config.ModelConfig.Tokens = Path.Combine(modelDir, "tokens.txt");
        config.ModelConfig.NumThreads = threads;
        config.ModelConfig.Provider = "cpu";
        recognizer = new OfflineRecognizer(config);
    }

    public RecognizedSpeech Recognize(short[] pcm)
    {
        var text = Decode(recognizer, gate, pcm);
        return new RecognizedSpeech(LangCodes.FromText(text), text, IsSpeech(text));
    }

    /// <summary>Silence came back as "그." in testing: one letter (ignoring punctuation) is not speech.</summary>
    internal static bool IsSpeech(string text) => text.Count(char.IsLetterOrDigit) > 1;

    internal static string Decode(OfflineRecognizer recognizer, object gate, short[] pcm)
    {
        var samples = new float[pcm.Length];
        for (int i = 0; i < pcm.Length; i++) samples[i] = pcm[i] / 32768f;
        lock (gate)
        {
            using var stream = recognizer.CreateStream();
            stream.AcceptWaveform(Segmenter.Rate, samples);
            recognizer.Decode(stream);
            return (stream.Result.Text ?? "").Trim();
        }
    }

    public void Dispose() => recognizer.Dispose();
}

/// <summary>
/// ReazonSpeech (k2 zipformer, sherpa-onnx): Japanese only, trained on a large amount of Japanese conversation —
/// much better at casual Japanese than SenseVoice. No punctuation. Whatever it hears is Japanese.
/// </summary>
sealed class ReazonSpeech : IRecognizer, IDisposable
{
    const string Encoder = "encoder-epoch-99-avg-1.int8.onnx";
    readonly OfflineRecognizer recognizer;
    readonly object gate = new();

    public static bool IsIn(string modelDir) => File.Exists(Path.Combine(modelDir, Encoder));

    /// <param name="hotwords">
    /// Words to listen harder for (the pet's names: said alone or first, they were dropped or misheard — "ほまれ、座って"
    /// came out as "座って"). Its tokens are single characters, so Japanese words go in as they are.
    /// </param>
    public ReazonSpeech(string modelDir, int threads = 2, IEnumerable<string>? hotwords = null, string? hotwordsFile = null)
    {
        var words = (hotwords ?? Array.Empty<string>()).Where(IsJapanese).Distinct().ToList();
        try
        {
            recognizer = new OfflineRecognizer(Config(modelDir, threads, words, hotwordsFile));
            Hotwords = words;
        }
        catch (Exception) when (words.Count > 0)
        {
            recognizer = new OfflineRecognizer(Config(modelDir, threads, new List<string>(), null)); // without them, rather than not at all
            Hotwords = new List<string>();
        }
    }

    /// <summary>The hotwords in use (empty when none, or when the model would not take them).</summary>
    public IReadOnlyList<string> Hotwords { get; }

    static bool IsJapanese(string word) => word.Any(c => c >= 0x3040 && c <= 0x30FF || c >= 0x4E00 && c <= 0x9FFF);

    static OfflineRecognizerConfig Config(string modelDir, int threads, List<string> hotwords, string? hotwordsFile)
    {
        var config = new OfflineRecognizerConfig();
        config.ModelConfig.Transducer.Encoder = Path.Combine(modelDir, Encoder);
        config.ModelConfig.Transducer.Decoder = Path.Combine(modelDir, "decoder-epoch-99-avg-1.int8.onnx");
        config.ModelConfig.Transducer.Joiner = Path.Combine(modelDir, "joiner-epoch-99-avg-1.int8.onnx");
        config.ModelConfig.Tokens = Path.Combine(modelDir, "tokens.txt");
        config.ModelConfig.NumThreads = threads;
        config.ModelConfig.Provider = "cpu";
        config.DecodingMethod = "greedy_search";
        if (hotwords.Count > 0 && hotwordsFile != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(hotwordsFile)!);
            File.WriteAllLines(hotwordsFile, hotwords);
            config.DecodingMethod = "modified_beam_search"; // hotwords need a beam search
            config.MaxActivePaths = 4;
            config.HotwordsFile = hotwordsFile;
            config.HotwordsScore = HotwordsScore;
            config.ModelConfig.ModelingUnit = "cjkchar";
        }
        return config;
    }

    /// <summary>How strongly the hotwords are favoured (sherpa-onnx's default is 1.5).</summary>
    public const float HotwordsScore = 2.0f;

    public RecognizedSpeech Recognize(short[] pcm)
    {
        var text = SenseVoice.Decode(recognizer, gate, Segmenter.WithLeadIn(pcm, LeadInSeconds));
        bool speech = SenseVoice.IsSpeech(text);
        return new RecognizedSpeech(speech ? Lang.JA : Lang.NONE, text, speech);
    }

    /// <summary>
    /// Half a second of silence before the speech: without it ReazonSpeech dropped the first word, and the
    /// recordings start just as the speaking does ("ほまれ、座って" came out as "座って"; padded, it came out whole).
    /// </summary>
    const double LeadInSeconds = 0.5;

    public void Dispose() => recognizer.Dispose();
}

/// <summary>DeepL over HTTPS. The key goes only in the Authorization header.</summary>
sealed class DeepLClient : ITranslator
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    readonly Func<string> key;

    public DeepLClient(Func<string> key) => this.key = key;

    public string Translate(string text, Lang source, Lang target)
    {
        var k = key().Trim();
        if (k.Length == 0) throw new DeepLException("DeepL のキーがありません（K キーで入れてください）");
        using var request = new HttpRequestMessage(HttpMethod.Post, DeepL.Endpoint(k))
        {
            Content = new StringContent(DeepL.Body(text, source, target), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", k);
        using var response = Http.Send(request);
        if (!response.IsSuccessStatusCode) throw new DeepLException(DeepL.Problem((int)response.StatusCode));
        using var reader = new StreamReader(response.Content.ReadAsStream(), Encoding.UTF8);
        return DeepL.Parse(reader.ReadToEnd());
    }
}

/// <summary>Kanji to hiragana readings (MeCab with IPADIC); characters KAT cannot show are dropped.</summary>
sealed class MeCabKana : IKana, IDisposable
{
    readonly MeCabIpaDicTagger tagger = MeCabIpaDicTagger.Create();
    readonly object gate = new();
    static readonly int Unknown = Kat.EncodeText("?").Single();

    public string ToKana(string japanese)
    {
        if (japanese.Length == 0) return "";
        var sb = new StringBuilder();
        lock (gate)
        {
            foreach (var node in tagger.Parse(japanese))
            {
                bool hasKanji = node.Surface.Any(c => c >= 0x4E00 && c <= 0x9FFF || c >= 0x3400 && c <= 0x4DBF || c == '々');
                if (hasKanji && !string.IsNullOrEmpty(node.Reading) && node.Reading != "*") sb.Append(ToHiragana(node.Reading));
                else sb.Append(node.Surface);
            }
        }
        // Keep a real '?', drop what KAT would show as '?' (unknown kanji, emoji).
        return string.Concat(Kat.Chars(sb.ToString()).Where(ch => ch == "?" || !Kat.EncodeText(ch).Contains(Unknown)));
    }

    static string ToHiragana(string katakana) =>
        new(katakana.Select(c => c >= 'ァ' && c <= 'ヶ' ? (char)(c - 0x60) : c).ToArray());

    public void Dispose() => tagger.Dispose();
}
