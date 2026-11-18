// Other players' voices (VRChat's sound): recognized on the PC and shown on the phone's page (OthersFeed) as they
// were said; or, with translate on, what is not Japanese is translated into Japanese by DeepL (Japanese skipped).
// Nothing is written to disk.
using System.Collections.Concurrent;

namespace TranslatorPc.Core;

public class OthersVoice
{
    /// <summary>A recording older than this when its turn comes is skipped: the moment has passed.</summary>
    public const double StaleSeconds = 20;

    // VRChat's sound is digital (no room noise), and people talk on: cut long speech so lines come up as it goes.
    readonly Segmenter segmenter = new(new SegmenterSettings { MaxS = 15f });
    readonly BlockingCollection<(short[] Pcm, double At)> queue = new();
    readonly IRecognizer stt;
    readonly ITranslator translator;
    readonly OthersFeed feed;
    readonly Func<double> clock;
    readonly Action<string> say;
    readonly Func<bool> translate;
    readonly object gate = new();
    bool listening;

    /// <param name="translate">Settings.TranslateOthers, read for each sentence (null: as it was said)</param>
    public OthersVoice(IRecognizer stt, ITranslator translator, OthersFeed feed, Func<double> clock, Action<string> say,
        Func<bool>? translate = null)
    {
        this.translate = translate ?? (() => false);
        this.stt = stt;
        this.translator = translator;
        this.feed = feed;
        this.clock = clock;
        this.say = say;
    }

    /// <summary>On while the phone's page is open (OthersFeed.HasViewer); off drops what was half heard.</summary>
    public bool Listening
    {
        get { lock (gate) return listening; }
        set
        {
            lock (gate)
            {
                if (value == listening) return;
                listening = value;
                if (!value) segmenter.Reset();
            }
        }
    }

    public void FeedFrame(short[] frame)
    {
        short[]? pcm;
        lock (gate)
        {
            if (!listening) return;
            pcm = segmenter.Feed(frame);
        }
        if (pcm != null) queue.Add((pcm, clock()));
    }

    /// <summary>Recognize and translate one queued utterance. False when there was nothing to do.</summary>
    public bool ProcessNext(int timeoutMs = 500)
    {
        if (!queue.TryTake(out var item, timeoutMs)) return false;
        if (clock() - item.At > StaleSeconds) return true;
        try { Process(item.Pcm); }
        catch (Exception e) { say("相手の声: 予期しないエラー: " + e.GetType().Name); }
        return true;
    }

    void Process(short[] pcm)
    {
        var heard = stt.Recognize(pcm);
        if (!heard.IsSpeech || heard.Lang == Lang.NONE || string.IsNullOrWhiteSpace(heard.Text)) return;
        if (!translate())
        {
            // As it was said: no DeepL.
            feed.Add(heard.Lang, heard.Text.Trim(), "");
            say($"相手の声（{heard.Lang}）: {heard.Text.Trim()}");
            return;
        }
        if (heard.Lang == Lang.JA) return; // Japanese needs no translating (and is not sent to DeepL)
        string translation;
        try
        {
            translation = translator.Translate(heard.Text.Trim(), heard.Lang, Lang.JA).Trim();
        }
        catch (DeepLException e)
        {
            say("相手の声を訳せませんでした: " + e.Message);
            return;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            say("相手の声を訳せませんでした（ネットにつながらない？）: " + e.GetType().Name);
            return;
        }
        if (translation.Length == 0) return;
        feed.Add(heard.Lang, heard.Text.Trim(), translation);
        say($"相手の声（{heard.Lang}）: {heard.Text.Trim()} → {translation}");
    }
}
