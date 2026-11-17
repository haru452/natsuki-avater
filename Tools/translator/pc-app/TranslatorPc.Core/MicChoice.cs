namespace TranslatorPc.Core;

/// <summary>Which microphone to send: the one VRChat uses, so the phone hears what VRChat hears.</summary>
public static class MicChoice
{
    /// <summary>
    /// Index of the device VRChat chose by name; null means VRChat uses Windows' default device
    /// (no name saved, or that device is unplugged), so we use the default too.
    /// </summary>
    public static int? Pick(IReadOnlyList<string> deviceNames, string? vrchatName)
    {
        string want = Normalize(vrchatName);
        if (want.Length == 0) return null;
        for (int i = 0; i < deviceNames.Count; i++)
            if (Normalize(deviceNames[i]) == want) return i;
        return null;
    }

    static string Normalize(string? s) => string.Concat((s ?? "").Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
}
