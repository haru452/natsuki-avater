// The pet hears her name: when the wearer says it, the avatar's TranslatorPetCalled is on for a moment,
// and the pet turns to the wearer and smiles (the avatar side is built by the Unity translator pet).
namespace TranslatorPc.Core;

public class PetCall
{
    public const string CalledParam = Kat.Prefix + "TranslatorPetCalled";
    /// <summary>
    /// How long the flag stays on: long enough for VRChat to sync it to everyone, shorter than her reaction
    /// (2.5 s), so one call is one reaction.
    /// </summary>
    public const double PulseSeconds = 1.0;

    readonly Action<byte[]> send;
    readonly Func<double> clock;
    readonly object gate = new();
    double? offAt;

    public PetCall(Action<byte[]> send, Func<double> clock)
    {
        this.send = send;
        this.clock = clock;
    }

    /// <summary>
    /// Whether the sentence has one of the names. Japanese is also checked in its kana reading (誉 → ほまれ),
    /// katakana counts as hiragana (ホマレ = ほまれ), and case and spaces do not matter (Homare = homare).
    /// </summary>
    public static bool Heard(string text, string kana, IEnumerable<string> names)
    {
        var said = new[] { Normalize(text), Normalize(kana) };
        return names.Select(Normalize).Where(n => n.Length > 0).Any(n => said.Any(s => s.Contains(n)));
    }

    public static string Normalize(string s)
    {
        var chars = s.Where(c => !char.IsWhiteSpace(c))
            .Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : char.ToLowerInvariant(c));
        return new string(chars.ToArray());
    }

    /// <summary>Turns the flag on (again) and schedules it off.</summary>
    public void Call()
    {
        lock (gate)
        {
            send(Osc.Message(CalledParam, true));
            offAt = clock() + PulseSeconds;
        }
    }

    /// <summary>Call often (the screen loop does): turns the flag off once its moment is over.</summary>
    public void Tick()
    {
        lock (gate)
        {
            if (offAt is not double t || clock() < t) return;
            send(Osc.Message(CalledParam, false));
            offAt = null;
        }
    }
}
