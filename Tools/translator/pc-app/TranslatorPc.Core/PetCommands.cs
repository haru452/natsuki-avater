// Spoken commands after the pet's name (Japanese only, matched in hiragana, no Gemini): sit, wait, come, stand up,
// turn round and face front.
namespace TranslatorPc.Core;

public enum PetCommand { Sit, Wait, Come, Stand, TurnAround, FaceFront }

public static class PetCommands
{
    public const string SitParam = Kat.Prefix + "TranslatorPetSit";
    public const string WaitParam = Kat.Prefix + "TranslatorPetWait";
    public const string TurnParam = Kat.Prefix + "TranslatorPetTurn";

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
            "こっちむいて", "こっちをむいて", "こっちみて", "こっちをみて", "ふりむいて", "もとにもどって", "もとどおり",
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
        PetCommand.Come => new[] { (WaitParam, false), (SitParam, false), (TurnParam, false) },
        PetCommand.TurnAround => new[] { (TurnParam, true) },
        PetCommand.FaceFront => new[] { (TurnParam, false) },
        _ => new[] { (SitParam, false) },
    };

    public static string Describe(PetCommand command) => command switch
    {
        PetCommand.Sit => "座る",
        PetCommand.Wait => "待つ",
        PetCommand.Come => "来る（待つ・座る・後ろ向きをやめる）",
        PetCommand.TurnAround => "後ろを向く",
        PetCommand.FaceFront => "前を向く",
        _ => "立つ",
    };
}
