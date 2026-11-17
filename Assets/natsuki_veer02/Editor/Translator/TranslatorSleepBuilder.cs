using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using C = TranslatorConfig;
using R = TranslatorRobotBuilder;

/// <summary>
/// Sleep: when the wearer stands still and stays silent for SleepAfterSeconds, the pet's eyes become flat bars and
/// "Z"s float up. Moving or talking wakes it: the head shakes left-right WakeShakes times with eyes shut, then they open.
/// Uses VRChat's built-in VelocityMagnitude and Voice, so it adds no synced parameters and everyone sees the same.
/// A layer of the motion controller (merged on Pet_Root; paths are relative to it).
/// </summary>
public static class TranslatorSleepBuilder
{
    public const string LayerName = "Translator_Sleep";

    public static readonly string HeadPivotPath = TranslatorMotionBuilder.RobotPath + "/" + R.HeadPivotName;
    public static readonly string EyeLPath = HeadPivotPath + "/" + R.EyeLeftName;
    public static readonly string EyeRPath = HeadPivotPath + "/" + R.EyeRightName;
    public static readonly string SleepEyeLPath = HeadPivotPath + "/" + R.SleepEyeLeftName;
    public static readonly string SleepEyeRPath = HeadPivotPath + "/" + R.SleepEyeRightName;
    public static readonly string ZzzPath = C.PetFollowName + "/" + C.ZzzName;

    const float Frame = 1f / 60f;

    public static void AddLayer(AnimatorController controller, AnimatorStateMachine sm, string outputDir)
    {
        controller.AddParameter(C.VoiceParam, AnimatorControllerParameterType.Float);

        var awake = State(sm, "Awake", 100, PoseClip(asleep: false, Frame), outputDir);
        var drowsy = State(sm, "Drowsy", 200, PoseClip(asleep: false, C.SleepAfterSeconds), outputDir);
        var sleep = State(sm, "Sleep", 300, Loop(PoseClip(asleep: true, 1f)), outputDir);
        var wake = State(sm, "Wake", 400, WakeClip(), outputDir);
        sm.defaultState = awake;

        var settle = Go(awake, drowsy);
        settle.AddCondition(AnimatorConditionMode.Less, C.SleepStillSpeed, C.VelocityParam);
        settle.AddCondition(AnimatorConditionMode.Less, C.SleepVoiceLevel, C.VoiceParam);

        // Any movement or sound while drowsy starts the minute again; one full minute of quiet falls asleep.
        Go(drowsy, awake).AddCondition(AnimatorConditionMode.Greater, C.SleepStillSpeed, C.VelocityParam);
        Go(drowsy, awake).AddCondition(AnimatorConditionMode.Greater, C.SleepVoiceLevel, C.VoiceParam);
        Go(drowsy, sleep, exitTime: 1f);

        Go(sleep, wake).AddCondition(AnimatorConditionMode.Greater, C.SleepStillSpeed, C.VelocityParam);
        Go(sleep, wake).AddCondition(AnimatorConditionMode.Greater, C.SleepVoiceLevel, C.VoiceParam);
        Go(wake, awake, exitTime: 1f);
    }

    static AnimatorState State(AnimatorStateMachine sm, string name, float y, AnimationClip clip, string outputDir)
    {
        var s = sm.AddState(name, new Vector3(300, y));
        s.motion = TranslatorAnimationBuilder.SaveClip(clip, outputDir + "/Translator_" + name + ".anim");
        s.writeDefaultValues = false;
        return s;
    }

    static AnimatorStateTransition Go(AnimatorState from, AnimatorState to, float? exitTime = null)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = exitTime.HasValue;
        t.exitTime = exitTime ?? 0f;
        t.duration = 0f;
        return t;
    }

    /// <summary>Eyes open or shut, Zzz on or off, head straight. Write Defaults is off, so every value is set.</summary>
    static AnimationClip PoseClip(bool asleep, float seconds)
    {
        var clip = new AnimationClip();
        SetEyes(clip, AnimationCurve.Constant(0f, seconds, asleep ? 0f : 1f));
        SetZzz(clip, asleep ? 1f : 0f, seconds);
        SetYaw(clip, AnimationCurve.Constant(0f, seconds, 0f));
        return clip;
    }

    /// <summary>Head shakes left-right with eyes shut, comes back to straight, then the eyes open on the last frame.</summary>
    static AnimationClip WakeClip()
    {
        float t = C.WakeShakeSeconds, a = C.WakeShakeAngle;
        int swings = C.WakeShakes * 2;
        var keys = new Keyframe[swings + 2];
        keys[0] = new Keyframe(0f, 0f);
        for (int i = 0; i < swings; i++)
            keys[i + 1] = new Keyframe(t * 0.85f * (i + 0.5f) / swings, i % 2 == 0 ? -a : a);
        keys[swings + 1] = new Keyframe(t * 0.9f, 0f);
        var yaw = new AnimationCurve(keys);
        yaw.AddKey(t, 0f);
        for (int i = 0; i < yaw.length; i++) yaw.SmoothTangents(i, 0f);

        var clip = new AnimationClip();
        SetYaw(clip, yaw);
        var open = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(t - Frame, 0f), new Keyframe(t, 1f));
        for (int i = 0; i < open.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(open, i, AnimationUtility.TangentMode.Constant);
            AnimationUtility.SetKeyRightTangentMode(open, i, AnimationUtility.TangentMode.Constant);
        }
        SetEyes(clip, open);
        SetZzz(clip, 0f, t);
        return clip;
    }

    /// <summary>open = 1: ring eyes shown, bars hidden; open = 0: the other way round.</summary>
    static void SetEyes(AnimationClip clip, AnimationCurve open)
    {
        var shut = new AnimationCurve(System.Array.ConvertAll(open.keys, k => new Keyframe(k.time, 1f - k.value)));
        for (int i = 0; i < shut.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(shut, i, AnimationUtility.TangentMode.Constant);
            AnimationUtility.SetKeyRightTangentMode(shut, i, AnimationUtility.TangentMode.Constant);
        }
        foreach (var path in new[] { EyeLPath, EyeRPath }) Active(clip, path, open);
        foreach (var path in new[] { SleepEyeLPath, SleepEyeRPath }) Active(clip, path, shut);
    }

    static void Active(AnimationClip clip, string path, AnimationCurve curve) =>
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive"), curve);

    static void SetZzz(AnimationClip clip, float on, float seconds) =>
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(ZzzPath, typeof(ParticleSystem), "EmissionModule.enabled"),
            AnimationCurve.Constant(0f, seconds, on));

    static void SetYaw(AnimationClip clip, AnimationCurve yaw)
    {
        float end = yaw.keys[yaw.length - 1].time;
        foreach (var axis in new[] { "x", "y", "z" })
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(HeadPivotPath, typeof(Transform), "localEulerAnglesRaw." + axis),
                axis == "y" ? yaw : AnimationCurve.Constant(0f, end, 0f));
    }

    static AnimationClip Loop(AnimationClip clip)
    {
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    /// <summary>Slow "Z"s drifting up and to the side from above the head. Emission is off until asleep.</summary>
    public static ParticleSystem AddZzz(Transform parent, string outputDir)
    {
        var go = new GameObject(C.ZzzName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0.06f, C.RobotHeight, 0);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.duration = 1f;
        main.startLifetime = 2.4f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.06f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 6;

        var emission = ps.emission;
        emission.rateOverTime = 1.2f;
        emission.enabled = false;

        var shape = ps.shape;
        shape.enabled = false;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
        vel.y = new ParticleSystem.MinMaxCurve(0.07f, 0.09f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var grow = ps.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.4f)));

        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        var tex = TranslatorAssetGenerator.SavePng(DrawZ(), outputDir + "/Pet_Z.png", overwrite: true);
        var mat = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended")) { mainTexture = tex };
        r.sharedMaterial = TranslatorAssetGenerator.SaveMaterial(mat, outputDir + "/Pet_Z.mat");
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    /// <summary>A closed eye: one glowing horizontal bar with rounded ends, the colour of the open eye.</summary>
    public static Texture2D DrawSleepEye()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var a0 = new Vector2(26f, 64f);
        var a1 = new Vector2(102f, 64f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            tex.SetPixel(x, y, new Color(R.Glow.r, R.Glow.g, R.Glow.b, Stroke(new Vector2(x + 0.5f, y + 0.5f), a0, a1, 9f)));
        return tex;
    }

    /// <summary>A bold white "Z" (top bar, diagonal, bottom bar) on a transparent background.</summary>
    public static Texture2D DrawZ()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        // Texture y grows upward: the top bar is at high y.
        var tl = new Vector2(24f, 110f);
        var tr = new Vector2(104f, 110f);
        var bl = new Vector2(24f, 18f);
        var br = new Vector2(104f, 18f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            float a = Mathf.Max(Stroke(p, tl, tr, 10f), Mathf.Max(Stroke(p, tr, bl, 10f), Stroke(p, bl, br, 10f)));
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        return tex;
    }

    /// <summary>Alpha of a round-capped line from a to b with the given half-width, soft over 1.5 px.</summary>
    static float Stroke(Vector2 p, Vector2 a, Vector2 b, float halfWidth)
    {
        var ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        float d = Vector2.Distance(p, a + ab * t);
        return Mathf.Clamp01((halfWidth - d) / 1.5f);
    }
}
