// Spoken commands after the pet's name (Japanese only, matched in hiragana, no Gemini): sit, wait, come, stand up,
// turn round, face front and look at me (こっち向いて: she keeps looking until she sets off).
namespace TranslatorPc.Core;

public enum PetCommand { Sit, Wait, Come, Stand, TurnAround, FaceFront, LookAtMe }

public static class PetCommands
{
    public const string SitParam = Kat.Prefix + "TranslatorPetSit";
    public const string WaitParam = Kat.Prefix + "TranslatorPetWait";
    public const string TurnParam = Kat.Prefix + "TranslatorPetTurn";
    /// <summary>On: she keeps looking at the wearer (the avatar clears it when she sets off).</summary>
    public const string FaceParam = Kat.Prefix + "TranslatorPetFace";

    /// <summary>
    /// The phrases for each command, in hiragana (katakana and kanji are matched by their reading: 座って → すわって).
    /// A sentence matches the command whose phrase is longest, so "おすわり" is Sit, not something shorter inside it.
    /// </summary>
    public static Dictionary<string, List<string>> Defaults() => new()
    {
        [nameof(PetCommand.Sit)] = new()
        {
            "すわって", "すわれ", "すわろう", "すわろっか", "すわりなさい", "すわりな", "すわっていいよ", "すわってみて",
            "すわってて", "すわってね", "すわってくれ", "すわってください", "おすわり", "こしかけて", "こしをおろして",
            "ちゃくせき", "やすんで", "ひとやすみ", "ひとやすみしよう",
        },
        [nameof(PetCommand.Wait)] = new()
        {
            "まってて", "まってって", "まってね", "まっててね", "まっていて", "まっててくれ", "まっててください", "まって",
            "まて", "まってろ", "まちなさい", "ここでまって", "そこでまって", "ここにいて", "そこにいて", "ここにいてね",
            "とまって", "とまれ", "すとっぷ", "すてい", "うごかないで", "うごかないでね", "るすばん", "おるすばん",
        },
        [nameof(PetCommand.Come)] = new()
        {
            "おいで", "おいでー", "こっちおいで", "こっちきて", "こっちにきて", "きて", "きてきて", "ついてきて", "ついてこい",
            "ついてきな", "いくよ", "いこう", "いこっか", "もういいよ", "もどってきて", "もどって", "かえってきて", "かえろう",
            "あつまって", "おまたせ",
        },
        [nameof(PetCommand.Stand)] = new()
        {
            "たって", "たちあがって", "たっていいよ", "たってみて", "たちなさい", "おきて", "おきあがって", "すたんどあっぷ",
        },
        [nameof(PetCommand.TurnAround)] = new()
        {
            "うしろむいて", "うしろをむいて", "うしろむいてて", "うしろむいてみて", "うしろむいてね", "うしろむけ", "うしろむきに",
            "うしろみて", "うしろをみて", "せなかむけて", "せなかをむけて", "あっちむいて", "あっちをむいて", "むこうむいて",
            "むこうをむいて", "まわれみぎ", "はんたいむいて", "はんたいをむいて",
        },
        [nameof(PetCommand.FaceFront)] = new()
        {
            "まえむいて", "まえをむいて", "まえむいてて", "まえむいてね", "まえむいていいよ", "まえむけ", "まえみて", "まえをみて",
            "もとにもどって", "もとどおり",
        },
        [nameof(PetCommand.LookAtMe)] = new()
        {
            "こっちむいて", "こっちをむいて", "こっちむいてて", "こっちむいてね", "こっちみて", "こっちをみて", "こっちみてて",
            "ふりむいて", "わたしをみて", "わたしみて", "ぼくをみて", "おれをみて", "めをみて", "こっちみてよ",
        },
    };

    /// <summary>The command said in this sentence, or null. Names are taken out first, so a name never reads as a command.</summary>
    public static PetCommand? Find(string text, string kana, IDictionary<string, List<string>> phrases, IEnumerable<string> names)
    {
        var said = new[] { PetCall.Normalize(text), PetCall.Normalize(kana) };
        foreach (var name in names.Select(PetCall.Normalize).Where(n => n.Length > 0))
            for (int i = 0; i < said.Length; i++) said[i] = said[i].Replace(name, "|");
        PetCommand? best = null;
        int bestLength = 0;
        foreach (var (key, list) in phrases)
        {
            if (!Enum.TryParse<PetCommand>(key, out var command)) continue;
            foreach (var phrase in list.Select(PetCall.Normalize))
                if (phrase.Length > bestLength && said.Any(s => s.Contains(phrase)))
                {
                    best = command;
                    bestLength = phrase.Length;
                }
        }
        return best;
    }

    /// <summary>The avatar parameters a command sets.</summary>
    public static IEnumerable<(string Address, bool Value)> Parameters(PetCommand command) => command switch
    {
        PetCommand.Sit => new[] { (SitParam, true) },
        PetCommand.Wait => new[] { (WaitParam, true) },
        PetCommand.Come => new[] { (WaitParam, false), (SitParam, false), (TurnParam, false), (FaceParam, false) },
        PetCommand.TurnAround => new[] { (TurnParam, true), (FaceParam, false) },
        PetCommand.FaceFront => new[] { (TurnParam, false), (FaceParam, false) },
        PetCommand.LookAtMe => new[] { (TurnParam, false), (FaceParam, true) },
        _ => new[] { (SitParam, false) },
    };

    public static string Describe(PetCommand command) => command switch
    {
        PetCommand.Sit => "座る",
        PetCommand.Wait => "待つ",
        PetCommand.Come => "来る（待つ・座る・後ろ向きをやめる）",
        PetCommand.TurnAround => "後ろを向く",
        PetCommand.FaceFront => "前を向く",
        PetCommand.LookAtMe => "こっちを見続ける",
        _ => "立つ",
    };
}
