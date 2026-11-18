using UnityEngine;

/// <summary>Central constants for the translator pet. Edit here and re-run Build Prefab to regenerate.</summary>
public static class TranslatorConfig
{
    public const string OutputDir = "Assets/natsuki_veer02/Translator";
    public const string PrefabName = "TranslatorPet.prefab";

    // Names the phone script and KAT senders rely on. Do not prefix or rename.
    public const string ActiveParam = "TranslatorActive";
    public const string LocalAIParam = "TranslatorLocalAI";
    public const string KatVisibleParam = "KAT_Visible";
    public const string KatPointerParam = "KAT_Pointer";
    public const string KatCharSyncPrefix = "KAT_CharSync";
    public const int KatClearPointer = 255;

    /// <summary>Characters synced per step: 4, 8 or 16. Higher is faster and costs 8 more bits each.</summary>
    public const int KatSyncCount = 8;
    public static readonly int[] KatSyncChoices = { 4, 8, 16 };

    public const string RootName = "TranslatorPet";
    /// <summary>Where the pet waits, at the wearer's right. Moves with the avatar.</summary>
    public const string PetTargetName = "Pet_Target";
    /// <summary>World-frozen container, toggled on and off.</summary>
    public const string PetRootName = "Pet_Root";
    /// <summary>The pet itself: each frame it moves part of the way from its last pose to Pet_Target.</summary>
    public const string PetFollowName = "Pet_Follow";
    public const string RobotName = "Pet_Robot";
    public const string TextName = "Pet_Text";
    public const string SignRootName = "Sign_Root";
    public const string SignBoardName = "Sign_Board";

    // KAT's clips animate these exact paths, relative to Pet_Text.
    public const string KatObjectName = "KillFrenzyAvatarText";
    public const string KatConstraintName = "Constraint";
    public const string KatTextName = "Text";
    public const string KatTextMaterialResource = "KAT_Misc/KAT_Text";
    public const string KatTilesPath = "Assets/KillFrenzy/AvatarText/Textures/KAT_CharTiles.png";

    /// <summary>Height of the robot pet, from the ground to the top of its head.</summary>
    public const float RobotHeight = 0.40f;

    // A humanoid model as the pet instead of the robot. Asset Store models cannot be redistributed, so the
    // BOOTH build sets PetModelPath to "" (or leaves the model out) and gets the robot.
    /// <summary>Which model: TranslatorPetProfile.Muryotaisu (Natsuki), .Homare (VRoid) or .Riko2.</summary>
    public static readonly TranslatorPetProfile Pet = TranslatorPetProfile.Homare;
    public static readonly string PetModelPath = Pet.ModelPath;  // "" for the robot (BOOTH)
    /// <summary>The model's own prefab: its renderers carry the textured materials (the FBX's embedded ones are blank).</summary>
    public static readonly string PetMaterialSource = Pet.MaterialSource;
    public static readonly string PetIdleClip = Pet.IdleClip;
    public static readonly string PetWalkClip = Pet.WalkClip;
    public static readonly string PetBoredClip = Pet.BoredClip;
    public static readonly string PetWakeClip = Pet.WakeClip;
    /// <summary>On while the model is near its waiting spot (a contact, not synced); then it walks the rest in.</summary>
    public const string ArrivedParam = "TranslatorPetArrived";
    public const float ArriveRadius = 0.5f;
    /// <summary>Extra pull toward the spot while walking in (per frame, against the pet's own pose), for LandSeconds.</summary>
    public const float LandPull = 0.15f;
    public const float LandSeconds = 0.5f;
    /// <summary>
    /// On while the wearer is within CloseRadius of the model (a contact): it waits where it is, so walking up to
    /// it does not push it away; once the wearer is farther, it goes to its spot.
    /// </summary>
    public const string CloseParam = "TranslatorPetClose";
    public const float CloseRadius = 2f;
    public const string WearerContactTag = "TranslatorWearer";
    /// <summary>A point this far ahead of the spot: waiting, the model faces the way the wearer faced.</summary>
    public const float AheadDistance = 10f;

    // Left or right: the humanoid waits on whichever side of the wearer she is on when she sets off.
    /// <summary>Between Pet_Heading and Pet_Target; its x scale (+1 / -1) puts the spot on the right or the left.</summary>
    public const string PetSideName = "Pet_Side";
    public const string PetLeftParam = "TranslatorPetLeft";
    public const string PetRightParam = "TranslatorPetRight";
    /// <summary>Mirror mode (menu): she waits right beside the wearer instead of in front, so not inside the mirror.</summary>
    public const string MirrorParam = "TranslatorPetMirror";
    public const string MirrorLabel = "鏡モード";
    /// <summary>
    /// Called by name: the PC translator app sets this for a moment when it hears the wearer say her name
    /// (synced, not in the menu); she turns to the wearer and smiles, as when looked at, for CalledSeconds.
    /// </summary>
    public const string CalledParam = "TranslatorPetCalled";
    public const float CalledSeconds = 2.5f;

    // Spoken commands after her name (the PC app hears them; synced, not in the menu).
    /// <summary>Sit (座って): she sits on the floor where she is; she gets up when told to, or when the wearer walks off.</summary>
    public const string SitParam = "TranslatorPetSit";
    /// <summary>Stay (待ってて): she stays where she is, however far the wearer goes, until called back or WaitRange away.</summary>
    public const string WaitParam = "TranslatorPetWait";
    /// <summary>On while the wearer is within WaitRange of her (a contact); leaving it ends the waiting and she respawns.</summary>
    public const string InRangeParam = "TranslatorPetInRange";
    public const float WaitRange = 20f;
    /// <summary>Turn round (後ろ向いて): she faces away until told to face front again or she sets off (synced).</summary>
    public const string TurnParam = "TranslatorPetTurn";
    public const float TurnSeconds = 0.6f;
    /// <summary>The squat she passes through on the way down to sit and back up (TranslatorPetMotions).</summary>
    public const string PetCrouchClip = "Assets/natsuki_veer02/PetMotions/Pet_Crouch.anim";
    public const float SitDownSeconds = 1.2f;
    public const float StandUpSeconds = 1.0f;
    /// <summary>A sitting-on-the-floor motion (humanoid; VRChat's own AFK pose), lowered onto the floor when baked.</summary>
    public const string PetSitClip = "Packages/com.vrchat.avatars/Samples/AV3 Demo Assets/Animation/ProxyAnim/proxy_afk.anim";
    /// <summary>
    /// Each side's sensing capsule, lying front to back (SideSenseLength long, so it also covers her when she is
    /// behind the wearer): this radius, its edge SideSenseGap from the wearer's middle (no flip-flop behind them).
    /// </summary>
    public const float SideSenseRadius = 3f;
    public const float SideSenseLength = 10f;
    public const float SideSenseGap = 0.2f;

    // Name tag over the humanoid pet's head, like VRChat's own: icon, name, a pronoun pill and a status line.
    /// <summary>Whose face the icon shows: the pet herself (Homare on this branch).</summary>
    public static readonly string NameTagAvatarPrefab = PetMaterialSource;
    /// <summary>
    /// The icon photo (cut from the wearer's cherry-blossom screenshot; TranslatorIconCapture can shoot one in Unity
    /// instead, overwriting it) and the finished tag picture (Tools/translator/nametag.py).
    /// </summary>
    public const string NameTagIconSource = "Assets/natsuki_veer02/Translator/NameTag_Icon.png";
    public const string NameTagTexture = "Assets/natsuki_veer02/Translator/NameTag.png";
    public const string NameTagName = "Pet_NameTag";
    /// <summary>Width of the tag in metres before the pet's own scale; its height follows the picture.</summary>
    public const float NameTagWidth = 0.55f;
    /// <summary>Above the head bone, clearing the hair and halo (metres before the pet's scale).</summary>
    public const float NameTagLift = 0.35f;
    /// <summary>The chat bubble's bottom, this far above the name tag's centre (metres before the pet's scale).</summary>
    public const float ChatBubbleLift = 0.10f;
    public const float BlinkSeconds = 4f;
    public static readonly (string mesh, string shape)[] PetBlinkShapes = Pet.Blink;
    /// <summary>The model's eyes sit at this fraction of the avatar's eye height (about 0.6 of its height).</summary>
    public const float PetHeightRatio = 0.6f;
    /// <summary>Where the pet waits: in front and to the right of the wearer, on the ground (out from under their feet).</summary>
    public static readonly Vector3 PetOffset = new Vector3(0.55f, 0f, 0.55f);

    /// <summary>
    /// Follow strength: weight of Pet_Target against the pet's own last pose (weight 1).
    /// Lower lags more. Per frame, so it is a little snappier at high frame rates.
    /// </summary>
    public const float FollowWeight = 0.04f;
    /// <summary>Weight used just after the toggle turns on, so the pet appears beside you instead of flying in.</summary>
    public const float AppearWeight = 1000f;
    public const float AppearSeconds = 0.25f;
    /// <summary>Animatable path of the follow constraint's second source (Pet_Target) weight.</summary>
    public const string FollowWeightProperty = "Sources.source1.Weight";

    // Teleport: a contact on Pet_Target notices when the pet is this far behind, and the pet snaps over.
    /// <summary>Far enough that running (the humanoid keeps up, RunPull) never sets it off; it is for teleports.</summary>
    public const float CatchUpDistance = 12f;
    public const string NearParam = "TranslatorPetNear";
    public const string PetContactTag = "TranslatorPetHome";
    public const float PetSenderRadius = 0.05f;

    // Where the pet waits turns with the wearer only while walking, so turning on the spot leaves it be.
    public const string PetHeadingName = "Pet_Heading";
    public const string HeadingWeightProperty = "Sources.source1.Weight";
    public const float MovingHeadingWeight = 1000f;
    /// <summary>
    /// The humanoid's spot turns with the wearer gradually while walking (this much per frame against its own
    /// direction), so a snap turn does not send the pet dashing round to the other side.
    /// </summary>
    public const float PetModelHeadingWeight = 0.08f;

    // Standing still, the pet ambles forward and back.
    public const float WanderDistance = 0.15f;
    public const float WanderSeconds = 8f;

    // Looking at the pet: a capsule from the wearer's head reaches forward; the pet turns to face them.
    public const string GazeName = "Pet_Gaze";
    public const string GazeTag = "TranslatorPetGaze";
    public const string LookedParam = "TranslatorPetLooked";
    public const float GazeLength = 2.0f;
    public const float GazeRadius = 0.25f;
    public const float LookZoneRadius = 0.3f;
    public const float LookTurnSeconds = 0.3f;
    /// <summary>
    /// The humanoid's head turns this share of the way toward the wearer's face while looked at (up and down too,
    /// so she looks down at a crouching wearer); less than all the way, so her neck never bends too far.
    /// </summary>
    public const float HeadLookWeight = 0.7f;

    // Petting the head (anyone's hand) makes hearts.
    public const string PatParam = "TranslatorPetPat";
    public static readonly string[] PatTags = { "Hand", "Finger" };
    public const float PatRadius = 0.15f;
    public const string HeartsName = "Pet_Hearts";
    public const float HeartsMinSeconds = 1f;

    // Hopping while the wearer moves. VelocityMagnitude is a VRChat built-in parameter (m/s).
    public const string PetHopName = "Pet_Hop";
    public const string VelocityParam = "VelocityMagnitude";
    public const float HopStartSpeed = 0.5f;
    public const float HopStopSpeed = 0.3f;
    // The humanoid runs while the wearer runs (VRChat: walking about 2 m/s, running about 4).
    /// <summary>A humanoid running motion (her own, TranslatorPetMotions; or one you make); it plays in place.</summary>
    public const string PetRunClip = "Assets/natsuki_veer02/PetMotions/Pet_Run.anim";
    public const float RunStartSpeed = 3f;
    public const float RunStopSpeed = 2.5f;
    /// <summary>The wearer's speed at which the run motion plays at its own pace (it plays faster or slower with them).</summary>
    public const float RunClipSpeed = 4f;
    /// <summary>
    /// Extra pull toward her spot while running (per frame, against her own pose; walking has none), so she keeps
    /// up at the wearer's speed, about a metre behind, instead of falling back until the catch-up snaps her over.
    /// </summary>
    public const float RunPull = 0.15f;
    public const float HopHeight = 0.12f;
    public const float HopSeconds = 0.4f;
    /// <summary>Scale on landing (wide and flat) and at the top of the hop (tall and thin).</summary>
    public static readonly Vector3 HopSquash = new Vector3(1.12f, 0.82f, 1.12f);
    public static readonly Vector3 HopStretch = new Vector3(0.94f, 1.1f, 0.94f);
    // Sleep: after a minute of the wearer standing still and silent, the eyes close to bars and Zzz float up.
    // Moving or talking wakes it: a few head shakes with eyes shut, then the eyes open.
    public const string VoiceParam = "Voice";
    public const float SleepAfterSeconds = 60f;
    public const float SleepStillSpeed = 0.05f;
    public const float SleepVoiceLevel = 0.08f;
    public const float WakeShakeSeconds = 1f;
    public const float WakeShakeAngle = 20f;
    public const int WakeShakes = 3;
    public const string ZzzName = "Pet_Zzz";

    public const float TextHeightAboveRobot = 0.12f;
    /// <summary>Size of KAT's text quad (32 characters x 4 rows).</summary>
    public static readonly Vector2 TextSize = new Vector2(0.5f, 1f / 6f);

    /// <summary>Board centre above the head bone: about 0.2 m of head plus 0.25 m of clearance.</summary>
    public const float SignHeightAboveHeadBone = 0.45f;
    /// <summary>Where the sign sits when the avatar has no head bone for the bone proxy to find.</summary>
    public const float SignFallbackHeight = 1.5f;
    public const float SignWidth = 0.8f;
    public const int SignTextureWidth = 1024;
    public const int SignTextureHeight = 128;
    /// <summary>The wearer asked to hide the "I'm using a translator" sign for now. Set true to bring it back.</summary>
    public const bool ShowSign = false;
    public static readonly string[] SignLines =
    {
        "Since I can't speak your language,",
        "I'm using a translator.",
    };

    public const string MenuLabel = "翻訳";
    /// <summary>The main toggle: the pet comes out and the PC app listens (for her name, and to translate).</summary>
    public const string ActiveLabel = "ペット";
    /// <summary>
    /// Translation on or off while the pet is out (read only by the PC app: local, not synced, remembered, on by
    /// default). Off, the app still listens for her name but shows and sends nothing.
    /// </summary>
    public const string TranslateParam = "TranslatorTranslate";
    public const string TranslateLabel = "翻訳";
    public const string LocalAILabel = "ローカルAI";
}
