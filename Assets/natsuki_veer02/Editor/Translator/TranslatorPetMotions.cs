using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The pet's own motions, made here from humanoid muscle curves (no borrowed animation, so they can ship anywhere):
/// Idle, Bored, Walk, Run and Jump. Saved as humanoid clips in Dir; the pet bakes them onto its model like any other
/// motion (TranslatorPetModel.Bake). Re-run Tools/Translator Pet/Generate Pet Motions after changing them here.
/// </summary>
public static class TranslatorPetMotions
{
    public const string Dir = "Assets/natsuki_veer02/PetMotions";
    public static string Path(string name) => Dir + "/Pet_" + name + ".anim";
    public static readonly string Idle = Path("Idle"), Bored = Path("Bored"), Walk = Path("Walk"), Run = Path("Run"), Jump = Path("Jump");

    const float Fps = 30f;
    const float Tau = Mathf.PI * 2f;

    /// <summary>A muscle's value over time.</summary>
    delegate float Curve(float t);

    [MenuItem("Tools/Translator Pet/Generate Pet Motions")]
    public static void Generate()
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(Dir).Replace('\\', '/'), "PetMotions");
        Save(Idle, IdleMotion(), 4f, loop: true);
        Save(Bored, BoredMotion(), 8f, loop: true);
        Save(Walk, WalkMotion(cycle: 1.0f, stride: 0.4f, knee: 0.45f, arms: 0.22f, lean: 0.03f, bob: 0.012f), 1.0f, loop: true);
        Save(Run, WalkMotion(cycle: 0.62f, stride: 0.6f, knee: 0.8f, arms: 0.45f, lean: 0.14f, bob: 0.03f, elbows: 0.25f), 0.62f, loop: true);
        Save(Jump, JumpMotion(), 1.1f, loop: false);
        AssetDatabase.SaveAssets();
        Debug.Log("[Translator] ペットの動きを作りました: " + Dir);
    }

    // A natural standing pose in muscle values (legs straight at 0.6; smaller is forward, for legs and arms alike).
    const float Thigh = 0.6f, Knee = 0.98f, ArmDown = -0.41f, ArmFront = 0.22f, Elbow = 0.93f, Height = 0.98f;

    /// <summary>Standing at rest: arms down by her sides, knees almost straight.</summary>
    static Dictionary<string, Curve> Stand() => new()
    {
        ["Left Arm Down-Up"] = _ => ArmDown,
        ["Right Arm Down-Up"] = _ => ArmDown,
        ["Left Arm Front-Back"] = _ => ArmFront,
        ["Right Arm Front-Back"] = _ => ArmFront,
        ["Left Forearm Stretch"] = _ => Elbow,
        ["Right Forearm Stretch"] = _ => Elbow,
        ["Left Upper Leg Front-Back"] = _ => Thigh,
        ["Right Upper Leg Front-Back"] = _ => Thigh,
        ["Left Lower Leg Stretch"] = _ => Knee,
        ["Right Lower Leg Stretch"] = _ => Knee,
        ["Left Foot Up-Down"] = _ => 0f,
        ["Right Foot Up-Down"] = _ => 0f,
        ["Spine Front-Back"] = _ => 0f,
        ["Chest Front-Back"] = _ => 0f,
        ["Head Nod Down-Up"] = _ => 0f,
        ["Head Turn Left-Right"] = _ => 0f,
        ["Head Tilt Left-Right"] = _ => 0f,
        ["Spine Left-Right"] = _ => 0f,
        ["RootT.x"] = _ => 0f,
        ["RootT.y"] = _ => Height,
        ["RootT.z"] = _ => 0f,
    };

    /// <summary>Breathing (chest), a slow sway and a little head movement; 4 s loop.</summary>
    static Dictionary<string, Curve> IdleMotion()
    {
        var m = Stand();
        Curve breathe = t => Mathf.Sin(t / 4f * Tau * 2f);
        m["Chest Front-Back"] = t => 0.03f * breathe(t);
        m["Left Arm Down-Up"] = t => ArmDown + 0.015f * breathe(t);
        m["Right Arm Down-Up"] = t => ArmDown + 0.015f * breathe(t);
        m["Spine Left-Right"] = t => 0.03f * Mathf.Sin(t / 4f * Tau);
        m["RootT.x"] = t => 0.008f * Mathf.Sin(t / 4f * Tau);
        m["Head Tilt Left-Right"] = t => 0.05f * Mathf.Sin(t / 4f * Tau + 0.8f);
        m["Head Nod Down-Up"] = t => 0.03f * Mathf.Sin(t / 4f * Tau * 2f + 1.2f);
        return m;
    }

    /// <summary>Bored: shifts her weight, looks around, swings her arms; 8 s loop.</summary>
    static Dictionary<string, Curve> BoredMotion()
    {
        var m = IdleMotion();
        Curve slow = t => Mathf.Sin(t / 8f * Tau);
        m["Spine Left-Right"] = t => 0.09f * slow(t);
        m["RootT.x"] = t => 0.03f * slow(t);
        m["Left Lower Leg Stretch"] = t => Knee - 0.12f * Mathf.Max(0f, -slow(t));
        m["Right Lower Leg Stretch"] = t => Knee - 0.12f * Mathf.Max(0f, slow(t));
        m["Head Turn Left-Right"] = t => 0.45f * Mathf.Sin(t / 8f * Tau * 2f);
        m["Head Nod Down-Up"] = t => -0.1f + 0.08f * Mathf.Sin(t / 8f * Tau * 3f);
        // Arms swinging idly.
        m["Left Arm Front-Back"] = t => ArmFront + 0.12f * Mathf.Sin(t / 8f * Tau * 4f);
        m["Right Arm Front-Back"] = t => ArmFront + 0.12f * Mathf.Sin(t / 8f * Tau * 4f + 0.7f);
        return m;
    }

    /// <summary>
    /// A walk (or, larger and faster, a run) on the spot: legs swing in turn, the knee bends as the leg comes forward,
    /// arms swing against the legs, the hips bob twice a cycle and the body leans into it.
    /// </summary>
    static Dictionary<string, Curve> WalkMotion(float cycle, float stride, float knee, float arms, float lean, float bob, float elbows = Elbow)
    {
        var m = Stand();
        float Phase(float t) => t / cycle * Tau;
        foreach (var (side, offset) in new[] { ("Left", 0f), ("Right", Mathf.PI) })
        {
            m[side + " Upper Leg Front-Back"] = t => Thigh + stride * Mathf.Sin(Phase(t) + offset);
            // Bent while the leg swings forward (its value falling), straight while it carries her.
            m[side + " Lower Leg Stretch"] = t => Knee - knee * Mathf.Max(0f, -Mathf.Cos(Phase(t) + offset + 0.3f));
            m[side + " Foot Up-Down"] = t => 0.15f * Mathf.Sin(Phase(t) + offset - 0.6f);
            m[side + " Arm Front-Back"] = t => ArmFront - arms * Mathf.Sin(Phase(t) + offset);
            m[side + " Forearm Stretch"] = t => elbows;
        }
        m["RootT.y"] = t => Height + bob * Mathf.Cos(2f * Phase(t));
        m["Spine Front-Back"] = _ => lean;
        m["Spine Twist Left-Right"] = t => 0.06f * Mathf.Sin(Phase(t));
        m["Head Nod Down-Up"] = _ => -lean * 0.5f;
        return m;
    }

    /// <summary>A happy hop: crouch, jump with arms up, land and straighten (1.1 s, once).</summary>
    static Dictionary<string, Curve> JumpMotion()
    {
        var m = Stand();
        // 0: stand, 0.25: crouched, 0.45: up in the air, 0.7: landed (crouched a little), 1.1: standing.
        Curve height = t => Key(t, (0f, 0f), (0.25f, -0.12f), (0.45f, 0.3f), (0.62f, 0f), (0.72f, -0.06f), (1.1f, 0f));
        Curve bend = t => Key(t, (0f, 0f), (0.25f, 1f), (0.38f, 0f), (0.62f, 0f), (0.72f, 0.5f), (1.1f, 0f));
        Curve armsUp = t => Key(t, (0f, 0f), (0.25f, -0.15f), (0.42f, 1f), (0.62f, 0.8f), (1.1f, 0f));
        m["RootT.y"] = t => Height + height(t);
        foreach (var side in new[] { "Left", "Right" })
        {
            m[side + " Upper Leg Front-Back"] = t => Thigh - 0.45f * bend(t);
            m[side + " Lower Leg Stretch"] = t => Knee - 0.9f * bend(t);
            m[side + " Arm Down-Up"] = t => ArmDown + 1.3f * armsUp(t);
            m[side + " Forearm Stretch"] = t => Elbow;
        }
        m["Spine Front-Back"] = t => 0.25f * bend(t);
        m["Head Nod Down-Up"] = t => 0.15f * armsUp(t);
        return m;
    }

    /// <summary>Smooth (cosine) steps between keys.</summary>
    static float Key(float t, params (float time, float value)[] keys)
    {
        if (t <= keys[0].time) return keys[0].value;
        for (int i = 1; i < keys.Length; i++)
            if (t <= keys[i].time)
            {
                float u = (t - keys[i - 1].time) / (keys[i].time - keys[i - 1].time);
                return Mathf.Lerp(keys[i - 1].value, keys[i].value, 0.5f - 0.5f * Mathf.Cos(u * Mathf.PI));
            }
        return keys[keys.Length - 1].value;
    }

    static void Save(string path, Dictionary<string, Curve> muscles, float length, bool loop)
    {
        var known = new HashSet<string>(HumanTrait.MuscleName) { "RootT.x", "RootT.y", "RootT.z" };
        var clip = new AnimationClip { frameRate = Fps };
        int frames = Mathf.RoundToInt(length * Fps);
        foreach (var (name, f) in muscles)
        {
            if (!known.Contains(name)) throw new ArgumentException("[Translator] 筋肉の名前が違います: " + name);
            var keys = Enumerable.Range(0, frames + 1).Select(i => new Keyframe(i / Fps, f(Mathf.Min(i / Fps, length)))).ToArray();
            if (loop) keys[frames].value = keys[0].value; // ends where it starts
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), name), curve);
        }
        foreach (var (name, v) in new[] { ("RootQ.x", 0f), ("RootQ.y", 0f), ("RootQ.z", 0f), ("RootQ.w", 1f) })
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), name), AnimationCurve.Constant(0f, length, v));
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing == null) AssetDatabase.CreateAsset(clip, path);
        else
        {
            EditorUtility.CopySerialized(clip, existing);
            UnityEngine.Object.DestroyImmediate(clip);
        }
    }
}
