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
        var samples = new float[pcm.Length];
        for (int i = 0; i < pcm.Length; i++) samples[i] = pcm[i] / 32768f;
        lock (gate)
        {
            using var stream = recognizer.CreateStream();
            stream.AcceptWaveform(Segmenter.Rate, samples);
            recognizer.Decode(stream);
            var r = stream.Result;
            var text = (r.Text ?? "").Trim();
            // Silence came back as "그." in testing: one letter (ignoring punctuation) is not speech.
            bool speech = text.Count(char.IsLetterOrDigit) > 1;
            return new RecognizedSpeech(LangCodes.FromText(text), text, speech);
        }
    }

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
