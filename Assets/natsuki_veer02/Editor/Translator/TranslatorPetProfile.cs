/// <summary>
/// Which humanoid model the pet is, and the names in it the pet relies on (bones, eye and smile shapes).
/// Pick one in TranslatorConfig.Pet and re-run Build Prefab.
/// </summary>
public sealed class TranslatorPetProfile
{
    public string ModelPath;
    /// <summary>The model's own prefab: its renderers carry the textured materials.</summary>
    public string MaterialSource;
    // Humanoid motions; they are retargeted onto this model's bones when baked.
    public string IdleClip, WalkClip, BoredClip, WakeClip;
    public string HeadBone, LeftEyeBone, RightEyeBone;
    /// <summary>The smile (petted, or looked at).</summary>
    public (string mesh, string shape)[] Smile;
    /// <summary>The blink: every shape closes together.</summary>
    public (string mesh, string shape)[] Blink;
    /// <summary>The blink shapes that also stay shut under the smile (the rest open, so they do not fight it).</summary>
    public (string mesh, string shape)[] ShutUnderSmile;
    /// <summary>A mesh hidden while the eyes are shut (eye highlights), or null.</summary>
    public string Highlight;
    /// <summary>
    /// Keep the model's own size (it is already small) instead of sizing her to PetHeightRatio of the wearer.
    /// </summary>
    public bool OwnSize;

    const string Mryotaisu = "Assets/Mryotaisu/";

    /// <summary>Natsuki (Asset Store model Muryotaisu).</summary>
    public static readonly TranslatorPetProfile Muryotaisu = new TranslatorPetProfile
    {
        ModelPath = Mryotaisu + "Models/Muryotaisu.fbx",
        MaterialSource = Mryotaisu + "Prefabs/Muryotaisu.prefab",
        IdleClip = Mryotaisu + "Animators/IdleA.anim",
        WalkClip = Mryotaisu + "Animators/Walk.anim",
        BoredClip = Mryotaisu + "Animators/IdleB.anim",
        WakeClip = Mryotaisu + "Animators/Jump.anim",
        HeadBone = "Head",
        LeftEyeBone = "Left Eye",
        RightEyeBone = "Right Eye",
        Smile = new[] { ("EyeLash", "Smile") },
        Blink = new[] { ("EyeLash", "Blink"), ("Face", "EyeClose"), ("Eye", "EyeClose") },
        // The lashes' smile alone sat over open eyes and looked like eyebrows.
        ShutUnderSmile = new[] { ("Face", "EyeClose"), ("Eye", "EyeClose") },
        Highlight = "EyeLight",
    };

    /// <summary>Homare (VRoid, imported with XAvatar), with the pet's own motions (TranslatorPetMotions): no Asset Store motion needed.</summary>
    public static readonly TranslatorPetProfile Homare = new TranslatorPetProfile
    {
        ModelPath = "Assets/homare/Prefab/homare.prefab",
        MaterialSource = "Assets/homare/Prefab/homare.prefab",
        IdleClip = "Assets/natsuki_veer02/PetMotions/Pet_Idle.anim",
        WalkClip = "Assets/natsuki_veer02/PetMotions/Pet_Walk.anim",
        BoredClip = "Assets/natsuki_veer02/PetMotions/Pet_Bored.anim",
        WakeClip = "Assets/natsuki_veer02/PetMotions/Pet_Jump.anim",
        HeadBone = "J_Bip_C_Head",
        LeftEyeBone = "J_Adj_L_FaceEye",
        RightEyeBone = "J_Adj_R_FaceEye",
        // VRoid's joy face already closes the eyes into smiling arcs, so the blink opens under it.
        Smile = new[] { ("Face", "Fcl_ALL_Joy") },
        Blink = new[] { ("Face", "Fcl_EYE_Close") },
        ShutUnderSmile = new (string, string)[0],
        Highlight = null,
        OwnSize = true, // Homare is small as she is
    };

    /// <summary>riko2 (VRoid; the first model tried for Homare), with the pet's own motions (TranslatorPetMotions): no Asset Store motion needed.</summary>
    public static readonly TranslatorPetProfile Riko2 = new TranslatorPetProfile
    {
        ModelPath = "Assets/riko2/Prefab/riko2.prefab",
        MaterialSource = "Assets/riko2/Prefab/riko2.prefab",
        IdleClip = "Assets/natsuki_veer02/PetMotions/Pet_Idle.anim",
        WalkClip = "Assets/natsuki_veer02/PetMotions/Pet_Walk.anim",
        BoredClip = "Assets/natsuki_veer02/PetMotions/Pet_Bored.anim",
        WakeClip = "Assets/natsuki_veer02/PetMotions/Pet_Jump.anim",
        HeadBone = "J_Bip_C_Head",
        LeftEyeBone = "J_Adj_L_FaceEye",
        RightEyeBone = "J_Adj_R_FaceEye",
        // VRoid's joy face already closes the eyes into smiling arcs, so the blink opens under it.
        Smile = new[] { ("Face", "Fcl_ALL_Joy") },
        Blink = new[] { ("Face", "Fcl_EYE_Close") },
        ShutUnderSmile = new (string, string)[0],
        Highlight = null,
    };
}
