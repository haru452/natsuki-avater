using UnityEngine;

/// <summary>Central constants for the fire gimmick. Edit here and re-run Setup to regenerate.</summary>
public static class FireConfig
{
    public const string ParamName = "Fire";
    public const string MenuLabel = "燃える";
    public const string LayerName = "Fire";
    public const string SitePrefix = "Fire_";
    public const string EmbersName = SitePrefix + "Embers";
    public const string OutputDir = "Assets/natsuki_veer02/Fire";

    /// <summary>Skinned meshes whose name contains one of these never emit flames (keeps the face readable).</summary>
    public static readonly string[] ExcludeMeshKeywords = { "Face", "Hair" };

    // Flame flipbook: SpriteTilesX x SpriteTilesY frames, each SpriteSize pixels square.
    public const int SpriteTilesX = 4;
    public const int SpriteTilesY = 4;
    public const int SpriteSize = 128;
    public const int EmberSize = 32;

    // Particle budget (VRChat performance): flames are shared across meshes, embers separate.
    public const int FlameBudget = 280;
    public const int EmberMax = 60;
    public const float FlameRatePerMesh = 120f;
    public const float EmberRate = 15f;

    // Flame look.
    public const float FlameSizeMin = 0.10f;
    public const float FlameSizeMax = 0.20f;
    public const float FlameLifeMin = 0.5f;
    public const float FlameLifeMax = 0.9f;
    public const float FlameRiseMin = 0.4f;
    public const float FlameRiseMax = 0.8f;

    /// <summary>Flame colour over lifetime: white-yellow root, orange, red, dark red.</summary>
    public static readonly Color[] FlameColors =
    {
        new Color(1.00f, 0.95f, 0.70f),
        new Color(1.00f, 0.60f, 0.15f),
        new Color(0.90f, 0.25f, 0.05f),
        new Color(0.30f, 0.05f, 0.00f),
    };
    public static readonly float[] FlameColorTimes = { 0f, 0.3f, 0.65f, 1f };
}
