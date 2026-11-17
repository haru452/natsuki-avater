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
    public const string PetModelPath = "Assets/Mryotaisu/Models/Muryotaisu.fbx";  // "" for the robot (BOOTH)
    /// <summary>The model's own prefab: its renderers carry the textured materials (the FBX's embedded ones are blank).</summary>
    public const string PetMaterialSource = "Assets/Mryotaisu/Prefabs/Muryotaisu.prefab";
    public const string PetIdleClip = "Assets/Mryotaisu/Animators/IdleA.anim";
    public const string PetWalkClip = "Assets/Mryotaisu/Animators/Walk.anim";
    public const string PetBoredClip = "Assets/Mryotaisu/Animators/IdleB.anim";
    public const string PetWakeClip = "Assets/Mryotaisu/Animators/Jump.anim";
    public const string PetSmileShape = "Smile";
    public static readonly string[] PetSmileMeshes = { "Face", "Eye", "EyeLash" };
    /// <summary>On while the model is near its waiting spot (a contact, not synced); then it jumps the rest.</summary>
    public const string ArrivedParam = "TranslatorPetArrived";
    public const float ArriveRadius = 0.5f;
    /// <summary>Extra pull toward the spot while jumping in (per frame, against the pet's own pose).</summary>
    public const float LandPull = 0.15f;
    /// <summary>
    /// On while the wearer is within CloseRadius of the model (a contact): it waits where it is, so walking up to
    /// it does not push it away; once the wearer is farther, it goes to its spot.
    /// </summary>
    public const string CloseParam = "TranslatorPetClose";
    public const float CloseRadius = 2f;
    public const string WearerContactTag = "TranslatorWearer";
    /// <summary>A point this far ahead of the spot: waiting, the model faces the way the wearer faced.</summary>
    public const float AheadDistance = 10f;

    // Name tag over the humanoid pet's head, like VRChat's own: icon, name, a pronoun pill and a status line.
    /// <summary>Whose face the icon shows: the pet herself (Natsuki).</summary>
    public const string NameTagAvatarPrefab = PetMaterialSource;
    /// <summary>The icon photo (TranslatorIconCapture) and the finished tag picture (Tools/translator/nametag.py).</summary>
    public const string NameTagIconSource = "Assets/natsuki_veer02/Translator/NameTag_Icon.png";
    public const string NameTagTexture = "Assets/natsuki_veer02/Translator/NameTag.png";
    public const string NameTagName = "Pet_NameTag";
    /// <summary>Width of the tag in metres before the pet's own scale; its height follows the picture.</summary>
    public const float NameTagWidth = 0.55f;
    /// <summary>Above the head bone, clearing the hair and halo (metres before the pet's scale).</summary>
    public const float NameTagLift = 0.35f;
    public const float BlinkSeconds = 4f;
    public static readonly (string mesh, string shape)[] PetBlinkShapes = { ("EyeLash", "Blink"), ("Face", "EyeClose"), ("Eye", "EyeClose") };
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
    public const float CatchUpDistance = 4f;
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
    public const string ActiveLabel = "翻訳";
    public const string LocalAILabel = "ローカルAI";
}
