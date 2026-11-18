using System.Linq;
using VRC.SDK3.Avatars.ScriptableObjects;

/// <summary>Synced-parameter bit counting. VRChat allows 256 bits per avatar.</summary>
public static class TranslatorBudget
{
    public const int Limit = 256;

    /// <summary>KAT without keyboard: KAT_Visible (1) + KAT_Pointer (8) + one Float (8) per synced character.</summary>
    public static int KatBits(int katSyncCount) => 1 + 8 + 8 * katSyncCount;

    /// <summary>Everything the prefab adds: the two translator Bools plus KAT.</summary>
    public static int SyncedBits(int katSyncCount) => 2 + KatBits(katSyncCount);

    /// <summary>Bits the prefab adds to these parameters; names the avatar already declares are not counted twice.</summary>
    /// <param name="mirror">the prefab has the humanoid's mirror-mode toggle, called-by-name flag and sit/wait/turn commands and gaze adjustment (six more bits)</param>
    public static int AddedBits(VRCExpressionParameters parameters, int katSyncCount, bool mirror = false)
    {
        // Unity's == null, not ?.: a missing asset reference is a fake-null object.
        var existing = parameters != null && parameters.parameters != null
            ? parameters.parameters.Where(p => p != null).Select(p => p.name).ToHashSet()
            : new System.Collections.Generic.HashSet<string>();
        return TranslatorPrefabBuilder.ParameterList(katSyncCount, mirror)
            .Where(p => !existing.Contains(p.nameOrPrefix) && !p.localOnly)
            .Sum(p => p.syncType == nadena.dev.modular_avatar.core.ParameterSyncType.Bool ? 1 : 8);
    }

    /// <summary>Bits already used by an avatar's expression parameters (unnamed slots are ignored).</summary>
    public static int UsedBits(VRCExpressionParameters parameters)
    {
        if (parameters == null || parameters.parameters == null) return 0;
        return parameters.parameters
            .Where(p => p != null && p.networkSynced && !string.IsNullOrEmpty(p.name))
            .Sum(p => p.valueType == VRCExpressionParameters.ValueType.Bool ? 1 : 8);
    }
}
