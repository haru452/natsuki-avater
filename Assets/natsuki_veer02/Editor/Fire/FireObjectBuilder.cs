using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates inactive, realistic flame Particle Systems that emit from the surface of the avatar's
/// body/cloth skinned meshes, plus one ember system.
/// </summary>
public static class FireObjectBuilder
{
    const string FlameMaterialName = "FireMat.mat";
    const string EmberMaterialName = "EmberMat.mat";
    const string ShaderName = "Legacy Shaders/Particles/Additive";

    /// <summary>Skinned meshes flames may emit from (Face/Hair and mesh-less renderers excluded).</summary>
    public static List<SkinnedMeshRenderer> BurnableMeshes(GameObject avatarRoot)
    {
        return avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(r => r.sharedMesh != null)
            .Where(r => !FireConfig.ExcludeMeshKeywords.Any(k => r.name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
            .ToList();
    }

    /// <summary>Checks everything Build needs, so callers can fail before writing anything.</summary>
    public static void Preflight(GameObject avatarRoot)
    {
        if (BurnableMeshes(avatarRoot).Count == 0)
            throw new InvalidOperationException("[Fire] no body/cloth Skinned Mesh Renderer found to emit flames from");
        if (Shader.Find(ShaderName) == null)
            throw new InvalidOperationException("[Fire] shader not found: " + ShaderName);
    }

    /// <returns>Number of fire objects created (one per burnable mesh, plus embers).</returns>
    public static int Build(GameObject avatarRoot, Texture2D sheet, Texture2D ember, string outputDir)
    {
        Preflight(avatarRoot);
        foreach (var old in FindAll(avatarRoot)) UnityEngine.Object.DestroyImmediate(old);

        var meshes = BurnableMeshes(avatarRoot);
        var flameMat = LoadOrCreateMaterial(sheet, outputDir, FlameMaterialName);
        var emberMat = LoadOrCreateMaterial(ember, outputDir, EmberMaterialName);
        int perMesh = FireConfig.FlameBudget / meshes.Count;

        var used = new HashSet<string>();
        foreach (var smr in meshes)
        {
            string name = FireConfig.SitePrefix + smr.name;
            for (int i = 2; !used.Add(name); i++) name = FireConfig.SitePrefix + smr.name + "_" + i;
            CreateFlame(avatarRoot.transform, name, smr, flameMat, perMesh);
        }
        CreateEmbers(avatarRoot.transform, meshes[0], emberMat);
        return meshes.Count + 1;
    }

    public static List<GameObject> FindAll(GameObject avatarRoot)
    {
        return avatarRoot.GetComponentsInChildren<Transform>(true)
            .Where(t => t != avatarRoot.transform && t.name.StartsWith(FireConfig.SitePrefix))
            .Select(t => t.gameObject)
            .ToList();
    }

    public static string PathOf(GameObject avatarRoot, GameObject fire)
    {
        return AnimationUtility.CalculateTransformPath(fire.transform, avatarRoot.transform);
    }

    static Material LoadOrCreateMaterial(Texture2D tex, string outputDir, string fileName)
    {
        var shader = Shader.Find(ShaderName);
        Directory.CreateDirectory(outputDir);
        string path = Path.Combine(outputDir, fileName).Replace("\\", "/");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        mat.mainTexture = tex;
        // The shader multiplies by 2 * _TintColor, so 0.5 keeps the particle colours as set.
        mat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static ParticleSystem NewSystem(Transform parent, string name, SkinnedMeshRenderer source, Material mat, int maxParticles)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // trails behind when moving
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;      // follows avatar scale
        main.maxParticles = maxParticles;
        main.startSpeed = 0f;
        main.gravityModifier = 0f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
        shape.skinnedMeshRenderer = source;
        shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
        shape.normalOffset = 0.02f;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return ps;
    }

    static void Rise(ParticleSystem ps, float min, float max, float sway)
    {
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-sway, sway);
        vel.y = new ParticleSystem.MinMaxCurve(min, max);
        vel.z = new ParticleSystem.MinMaxCurve(-sway, sway);
    }

    static void Turbulence(ParticleSystem ps, float strength, float frequency)
    {
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = strength;
        noise.frequency = frequency;
        noise.scrollSpeed = 1f;
        noise.damping = true;
        noise.quality = ParticleSystemNoiseQuality.Medium;
    }

    static void CreateFlame(Transform parent, string name, SkinnedMeshRenderer source, Material mat, int maxParticles)
    {
        var ps = NewSystem(parent, name, source, mat, maxParticles);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(FireConfig.FlameLifeMin, FireConfig.FlameLifeMax);
        main.startSize = new ParticleSystem.MinMaxCurve(FireConfig.FlameSizeMin, FireConfig.FlameSizeMax);
        main.startRotation = new ParticleSystem.MinMaxCurve(-10f * Mathf.Deg2Rad, 10f * Mathf.Deg2Rad);

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = FireConfig.FlameRatePerMesh;

        Rise(ps, FireConfig.FlameRiseMin, FireConfig.FlameRiseMax, 0.05f);
        Turbulence(ps, 0.25f, 2f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var keys = new GradientColorKey[FireConfig.FlameColors.Length];
        for (int i = 0; i < keys.Length; i++) keys[i] = new GradientColorKey(FireConfig.FlameColors[i], FireConfig.FlameColorTimes[i]);
        var g = new Gradient();
        g.SetKeys(keys, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        var curve = new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.3f));
        size.size = new ParticleSystem.MinMaxCurve(1f, curve);

        var tsa = ps.textureSheetAnimation;
        tsa.enabled = true;
        tsa.mode = ParticleSystemAnimationMode.Grid;
        tsa.numTilesX = FireConfig.SpriteTilesX;
        tsa.numTilesY = FireConfig.SpriteTilesY;
        tsa.animation = ParticleSystemAnimationType.WholeSheet;
        tsa.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
        tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f - 1f / (FireConfig.SpriteTilesX * FireConfig.SpriteTilesY));

        ps.gameObject.SetActive(false);
    }

    static void CreateEmbers(Transform parent, SkinnedMeshRenderer source, Material mat)
    {
        var ps = NewSystem(parent, FireConfig.EmbersName, source, mat, FireConfig.EmberMax);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.035f);

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = FireConfig.EmberRate;

        Rise(ps, 0.6f, 1.2f, 0.15f);
        Turbulence(ps, 0.6f, 1.5f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.9f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.5f, 0.1f), 0.5f), new GradientColorKey(new Color(0.8f, 0.2f, 0f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

        ps.gameObject.SetActive(false);
    }
}
