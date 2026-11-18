// Port of the phone app's OwnVoicePipeline, with DeepL instead of the on-phone language model.
using System.Collections.Concurrent;

namespace TranslatorPc.Core;

/// <summary>Japanese to kana (KAT cannot show kanji).</summary>
public interface IKana
{
    string ToKana(string text);
}

/// <summary>
/// Glue between the avatar's toggle, the microphone, recognition, DeepL and the display.
/// FeedFrame runs on the microphone thread, ProcessNext on one worker thread, OnOsc on the network thread.
/// </summary>
public class OwnVoice
{
    public const string ActiveParam = Kat.Prefix + "TranslatorActive";
    /// <summary>The avatar's 翻訳 toggle: off, the pet still listens for her name but nothing is translated or shown.</summary>
    public const string TranslateParam = Kat.Prefix + "TranslatorTranslate";
    public const string AvatarChange = "/avatar/change";
    /// <summary>A recording older than this when its turn comes is skipped: the moment has passed.</summary>
    public const double StaleSeconds = 20;

    record Recording(short[] Pcm, int Generation, double RecordedAt);

    readonly Settings settings;
    readonly IRecognizer stt;
    readonly ITranslator translator;
    readonly IDisplay display;
    readonly IKana kana;
    readonly Action<Lang, string, string> log;
    readonly Func<double> clock;
    readonly Action<string> say;
    readonly PetCall? petCall;
    readonly IPetChat? chat;
    readonly object gate = new();
    readonly Segmenter segmenter = new(SegmenterSettings.ForPcMic());
    readonly BlockingCollection<Recording> queue = new();
    int generation; // bumped whenever listening stops, so older recordings are dropped

    public OwnVoice(Settings settings, IRecognizer stt, ITranslator translator, IDisplay display, IKana kana,
        Action<Lang, string, string> log, Func<double> clock, Action<string> say, PetCall? petCall = null,
        IPetChat? chat = null)
    {
        this.petCall = petCall;
        this.chat = chat;
        this.settings = settings;
        this.stt = stt;
        this.translator = translator;
        this.display = display;
        this.kana = kana;
        this.log = log;
        this.clock = clock;
        this.say = say;
    }

    public bool Active { get; private set; }

    /// <summary>The display is shared with the screen loop; take this lock around Tick.</summary>
    public object DisplayLock => gate;

    /// <summary>Translate and show what is said (the avatar's 翻訳 toggle; on until the avatar says otherwise).</summary>
    public bool Translating { get; private set; } = true;

    public void OnOsc(string address, object[] args)
    {
        if (address == AvatarChange) { SetActive(false); return; }
        if (args.Length == 0 || address is not (ActiveParam or TranslateParam)) return;
        bool? on = args[0] switch { bool b => b, int i => i != 0, float f => f != 0, _ => null };
        if (on is not bool value) return;
        if (address == ActiveParam) SetActive(value);
        else SetTranslating(value);
    }

    void SetTranslating(bool on)
    {
        lock (gate)
        {
            if (on == Translating) return;
            Translating = on;
            if (!on) display.Clear();
        }
        say(on ? "翻訳: オン" : "翻訳: オフ（名前だけ聞いています）");
    }

    void SetActive(bool on)
    {
        lock (gate)
        {
            if (on == Active) return;
            Active = on;
            if (!on)
            {
                generation++;
                segmenter.Reset();
                display.Stop();
            }
        }
        say(on ? "翻訳: オン（聞いています）" : "翻訳: オフ");
    }

    bool IsCurrent(int gen) { lock (gate) return Active && gen == generation; }

    void IfCurrent(int gen, Action action) { lock (gate) if (Active && gen == generation) action(); }

    public void FeedFrame(short[] frame)
    {
        Recording? item;
        lock (gate)
        {
            if (!Active) return;
            var pcm = segmenter.Feed(frame);
            item = pcm == null ? null : new Recording(pcm, generation, clock());
        }
        if (item != null) queue.Add(item);
    }

    /// <summary>Recognize and translate one queued utterance. False when there was nothing to do.</summary>
    public bool ProcessNext(int timeoutMs = 500)
    {
        if (!queue.TryTake(out var item, timeoutMs)) return false;
        if (clock() - item.RecordedAt > StaleSeconds)
        {
            say("古い録音を飛ばしました（翻訳が混んでいます）");
            return true;
        }
        try { Process(item); }
        catch (Exception e)
        {
            // Never let one bad sentence stop the translator.
            say("予期しないエラー: " + e.GetType().Name);
            IfCurrent(item.Generation, display.Error);
        }
        return true;
    }

    /// <summary>Her answer on her bubble (English); only the wearer's own recognized words go to Gemini.</summary>
    void Answer(int gen, string said)
    {
        IfCurrent(gen, () =>
        {
            display.ChatboxOnly = false;
            display.Thinking();
        });
        string reply;
        try { reply = chat!.Reply(said); }
        catch (PetChatException e)
        {
            say("ほまれが返事できませんでした: " + e.Message);
            IfCurrent(gen, display.Error);
            return;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            say("ほまれが返事できませんでした（ネットにつながらない？）: " + e.GetType().Name);
            IfCurrent(gen, display.Error);
            return;
        }
        say($"ほまれ: {reply}（{said}）");
        IfCurrent(gen, () => display.Show(new TranslationResult(Lang.JA, Lang.EN, said, reply)));
    }

    void Process(Recording item)
    {
        int gen = item.Generation;
        if (!IsCurrent(gen)) return; // recorded before the toggle went off
        IfCurrent(gen, () =>
        {
            if (!Translating) return;
            display.ChatboxOnly = settings.TargetLang != Lang.EN;
            display.Thinking();
        });

        double t0 = clock();
        var heard = stt.Recognize(item.Pcm);
        double recognize = clock() - t0;
        if (!heard.IsSpeech || heard.Lang is not (Lang.JA or Lang.EN) || string.IsNullOrWhiteSpace(heard.Text))
        {
            say($"出しません（{(heard.IsSpeech ? "言語 " + heard.Lang : "話し声ではない")}）: {heard.Text}");
            IfCurrent(gen, display.Clear);
            return;
        }
        var source = heard.Lang;
        var kanaOriginal = source == Lang.JA ? kana.ToKana(heard.Text) : "";
        // Her name (and a command after it): she reacts at once, without waiting for DeepL.
        if (petCall != null)
        {
            string? reaction = null;
            // Her name in any recognizer's text (the other one may have caught it), the command in the chosen one.
            var also = string.Join(" ", heard.AllTexts.Skip(1));
            IfCurrent(gen, () => reaction = petCall.Hear(heard.Text, kanaOriginal, settings, also));
            if (reaction != null) say(reaction);
            // Called by name, not told to do something: she answers (translated or not; for playing alone too).
            if (chat != null && settings.PetChat && petCall.LastWasCall)
            {
                Answer(gen, heard.Text);
                return;
            }
        }
        bool translating;
        lock (gate) translating = Translating;
        if (!translating) return; // nothing shown, sent to DeepL or logged
        // English is shown as it is (no Japanese anywhere: the wearer did not want it on screen).
        var target = source == Lang.JA ? settings.TargetLang : Lang.EN;
        lock (gate) display.ChatboxOnly = target != Lang.EN;

        double t1 = clock();
        string translation;
        try
        {
            translation = source == Lang.EN ? heard.Text.Trim() : translator.Translate(heard.Text, source, target).Trim();
        }
        catch (DeepLException e)
        {
            say("翻訳できませんでした: " + e.Message);
            IfCurrent(gen, display.Error);
            return;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            say("翻訳できませんでした（ネットにつながらない？）: " + e.GetType().Name);
            IfCurrent(gen, display.Error);
            return;
        }
        say($"時間: 認識 {recognize:0.0} 秒 / DeepL {clock() - t1:0.0} 秒");
        if (translation.Length == 0)
        {
            IfCurrent(gen, display.Clear);
            return;
        }
        var result = new TranslationResult(source, target, heard.Text, translation,
            KanaOriginal: kanaOriginal,
            KanaTranslation: target == Lang.JA ? kana.ToKana(translation) : "");
        try { log(source, heard.Text, translation); }
        catch (IOException e) { say("記録できませんでした: " + e.GetType().Name); }
        say($"{source}: {heard.Text} → {translation}");
        IfCurrent(gen, () => display.Show(result));
    }
}
