// Port of the phone app's Segmenter and JaEnRecognizer.
namespace TranslatorPc.Core;

public class SegmenterSettings
{
    public float StartS { get; set; } = 0.15f;
    /// <summary>0.8 s cut sentences into pieces ("うん、なか。") in real use; 1.2 s keeps a sentence whole.</summary>
    public float EndSilenceS { get; set; } = 1.2f;
    public float PrerollS { get; set; } = 0.3f;
    public float MaxS { get; set; } = 60f;
    public float MinS { get; set; } = 0.5f;
    public float Margin { get; set; } = 3f;
    public float FloorMin { get; set; } = 200f;

    /// <summary>The PC's microphone: Windows suppresses room noise to ~0, so quieter speech counts.</summary>
    public static SegmenterSettings ForPcMic() => new() { FloorMin = 60f };
}

/// <summary>
/// Feed 30 ms frames; get back one utterance (PCM) when the speaker stops. A frame is voice when it is Margin times
/// louder than the room's noise floor (and above FloorMin * Margin). Recording starts after StartS of voice, keeps
/// PrerollS from before, ends after EndSilenceS of quiet (keeping a short tail), and is cut at MaxS.
/// Utterances with less than MinS of voice are dropped.
/// </summary>
public class Segmenter
{
    public const int Rate = 16000;
    public const int FrameMs = 30;
    public const int FrameSamples = Rate * FrameMs / 1000;
    const float TailS = 0.2f;

    readonly int startFrames, endFrames, maxFrames, minFrames, tailFrames, prerollMax;
    readonly float margin, floorMin;
    readonly LinkedList<short[]> preroll = new();
    List<short[]> buffer = new();
    int voicedRun, voicedTotal, silence;

    public Segmenter(SegmenterSettings s)
    {
        startFrames = Frames(s.StartS);
        endFrames = Frames(s.EndSilenceS);
        maxFrames = Frames(s.MaxS);
        minFrames = Frames(s.MinS);
        tailFrames = Frames(TailS);
        prerollMax = Frames(s.PrerollS) + startFrames;
        margin = s.Margin;
        floorMin = s.FloorMin;
        NoiseFloor = floorMin;
    }

    static int Frames(float seconds) => Math.Max(1, (int)Math.Round(seconds * 1000.0 / FrameMs, MidpointRounding.ToEven));

    public static float Rms(short[] frame)
    {
        if (frame.Length == 0) return 0f;
        double sum = 0;
        foreach (var v in frame) sum += (double)v * v;
        return (float)Math.Sqrt(sum / frame.Length);
    }

    public float NoiseFloor { get; private set; }
    public bool Recording { get; private set; }
    public float Threshold => Math.Max(NoiseFloor, floorMin) * margin;

    /// <summary>Forget anything half-recorded (used when the toggle turns off).</summary>
    /// <summary>The speech with this much silence before it (a recognizer that drops the first word wants it).</summary>
    public static short[] WithLeadIn(short[] pcm, double seconds)
    {
        int lead = (int)(Rate * seconds);
        var padded = new short[lead + pcm.Length];
        Array.Copy(pcm, 0, padded, lead, pcm.Length);
        return padded;
    }

    public void Reset()
    {
        Recording = false;
        buffer = new();
        voicedRun = voicedTotal = silence = 0;
        preroll.Clear();
    }

    public short[]? Feed(short[] frame)
    {
        float level = Rms(frame);
        bool voiced = level > Threshold;
        if (!Recording)
        {
            // Follow the room: quickly while quiet, very slowly otherwise.
            NoiseFloor += (level - NoiseFloor) * (voiced ? 0.002f : 0.05f);
            preroll.AddLast(frame);
            while (preroll.Count > prerollMax) preroll.RemoveFirst();
            voicedRun = voiced ? voicedRun + 1 : 0;
            if (voicedRun >= startFrames)
            {
                Recording = true;
                buffer = preroll.ToList();
                voicedTotal = voicedRun;
                silence = 0;
            }
            return null;
        }

        buffer.Add(frame);
        if (voiced) { voicedTotal++; silence = 0; }
        else silence++;
        if (buffer.Count >= maxFrames) return Finish(trim: false);
        if (silence >= endFrames) return Finish(trim: true);
        return null;
    }

    short[]? Finish(bool trim)
    {
        var kept = trim ? buffer.Take(buffer.Count - (silence - tailFrames)).ToList() : buffer;
        bool enough = voicedTotal >= minFrames;
        var pcm = enough ? kept.SelectMany(f => f).ToArray() : null;
        Reset();
        return pcm;
    }
}

/// <param name="Heard">
/// Every recognizer's text for this speech (Text first): the pet's name is looked for in all of them, so one
/// recognizer missing it does not lose it.
/// </param>
public record RecognizedSpeech(Lang Lang, string Text, bool IsSpeech, IReadOnlyList<string>? Heard = null)
{
    public IReadOnlyList<string> AllTexts => Heard ?? new[] { Text };
}

public interface IRecognizer
{
    RecognizedSpeech Recognize(short[] pcm);
}

/// <summary>
/// The wearer speaks only Japanese or English. SenseVoice's automatic guess sometimes calls short Japanese Chinese,
/// Korean or a single English word ("好嘞。", "也希요。", "The."). Keep Japanese and real English sentences (two or
/// more words, no kana/kanji); hear anything else again with the recognizer fixed to Japanese.
/// refineJapanese: the Japanese recognizer is better at Japanese (ReazonSpeech), so Japanese is heard again by it too.
/// </summary>
public class JaEnRecognizer : IRecognizer
{
    readonly IRecognizer auto, japanese;
    readonly bool refineJapanese;

    public JaEnRecognizer(IRecognizer auto, IRecognizer japanese, bool refineJapanese = false)
    {
        this.auto = auto;
        this.japanese = japanese;
        this.refineJapanese = refineJapanese;
    }

    public RecognizedSpeech Recognize(short[] pcm)
    {
        var first = auto.Recognize(pcm);
        if (IsEnglishSentence(first)) return first;
        if (first.Lang == Lang.JA && !refineJapanese) return first;
        var again = japanese.Recognize(pcm);
        var chosen = first.Lang == Lang.JA && !again.IsSpeech ? first : again;
        return chosen with { Heard = new[] { chosen.Text, chosen == first ? again.Text : first.Text } };
    }

    static bool IsEnglishSentence(RecognizedSpeech r)
    {
        if (r.Lang != Lang.EN) return false;
        if (r.Text.Any(c => c >= 0x3040 && c <= 0x30FF || c >= 0x4E00 && c <= 0x9FFF || c >= 0xAC00 && c <= 0xD7AF)) return false;
        return System.Text.RegularExpressions.Regex.Split(r.Text, "[^A-Za-z']+").Count(w => w.Length > 0) >= 2;
    }
}
