using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using C = TranslatorConfig;
using M = TranslatorMotionBuilder;

/// <summary>
/// The humanoid pet's eyes, in one layer that owns every eye shape (so the blink never fights the smile):
/// Blink (open, a quick blink every BlinkSeconds), Happy (petted: a closed-eye smile), Glance (looked at: the same
/// smile for a moment) and Seen (open again until the wearer looks away, so each look gets one smile).
/// A smile closes the eyes too: the lashes' smile shape alone sat over open eyes and looked like eyebrows.
/// </summary>
public static class TranslatorPetEyes
{
    const float GlanceSeconds = 2.5f;

    public static void AddLayer(AnimatorStateMachine sm, string outputDir)
    {
        var blinkClip = Blink();
        var blink = State(sm, "Blink", 100, blinkClip, outputDir + "/Translator_PetBlink.anim");
        var happy = State(sm, "Happy", 200, Smile(AnimationCurve.Constant(0f, C.HeartsMinSeconds, 100f), C.HeartsMinSeconds),
            outputDir + "/Translator_PetHappy.anim");
        var smile = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.15f, 100f), new Keyframe(GlanceSeconds - 0.45f, 100f), new Keyframe(GlanceSeconds, 0f));
        var glance = State(sm, "Glance", 300, Smile(smile, GlanceSeconds), outputDir + "/Translator_PetGlance.anim");
        var seen = sm.AddState("Seen", new Vector3(550, 300));
        seen.motion = blink.motion;
        seen.writeDefaultValues = false;
        sm.defaultState = blink;

        Go(blink, happy, 0.1f).AddCondition(AnimatorConditionMode.If, 0f, C.PatParam);
        Go(blink, glance, 0f).AddCondition(AnimatorConditionMode.If, 0f, C.LookedParam);
        Go(happy, blink, 0.15f, exitTime: 1f).AddCondition(AnimatorConditionMode.IfNot, 0f, C.PatParam);
        foreach (var s in new[] { glance, seen })
            Go(s, happy, 0.1f).AddCondition(AnimatorConditionMode.If, 0f, C.PatParam);
        Go(glance, seen, 0f, exitTime: 1f);
        Go(seen, blink, 0f).AddCondition(AnimatorConditionMode.IfNot, 0f, C.LookedParam);
        // Called by name: the same smile, even while already looked at.
        foreach (var s in new[] { blink, seen })
            Go(s, glance, 0f).AddCondition(AnimatorConditionMode.If, 0f, C.CalledParam);
    }

    /// <summary>Open, shut and open again quickly at the end of every BlinkSeconds; no smile, highlights on.</summary>
    static AnimationClip Blink()
    {
        float t = C.BlinkSeconds;
        var clip = new AnimationClip();
        foreach (var (mesh, shape) in C.Pet.Blink)
            Shape(clip, mesh, shape, Linear(new Keyframe(0f, 0f), new Keyframe(t - 0.16f, 0f), new Keyframe(t - 0.09f, 100f),
                new Keyframe(t - 0.02f, 0f), new Keyframe(t, 0f)));
        foreach (var (mesh, shape) in C.Pet.Smile)
            Shape(clip, mesh, shape, AnimationCurve.Constant(0f, t, 0f));
        Highlight(clip, AnimationCurve.Constant(0f, t, 1f));
        Loop(clip, true);
        return clip;
    }

    /// <summary>The smile follows amount (0–100): the smile shapes, the eyes shut under them, highlights hidden.</summary>
    static AnimationClip Smile(AnimationCurve amount, float seconds)
    {
        var clip = new AnimationClip();
        foreach (var blink in C.Pet.Blink)
            Shape(clip, blink.mesh, blink.shape,
                System.Array.IndexOf(C.Pet.ShutUnderSmile, blink) >= 0 ? amount : AnimationCurve.Constant(0f, seconds, 0f));
        foreach (var (mesh, shape) in C.Pet.Smile)
            Shape(clip, mesh, shape, amount);
        // Hidden while the eyes are mostly shut (an on/off property: stepped, not blended).
        var on = new AnimationCurve();
        for (float t = 0f; t <= seconds + 1e-4f; t += seconds / 40f) on.AddKey(t, amount.Evaluate(t) > 50f ? 0f : 1f);
        for (int i = 0; i < on.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(on, i, AnimationUtility.TangentMode.Constant);
            AnimationUtility.SetKeyRightTangentMode(on, i, AnimationUtility.TangentMode.Constant);
        }
        Highlight(clip, on);
        Loop(clip, false);
        return clip;
    }

    static void Shape(AnimationClip clip, string mesh, string shape, AnimationCurve curve) =>
        AnimationUtility.SetEditorCurve(clip,
            EditorCurveBinding.FloatCurve(M.RobotPath + "/" + mesh, typeof(SkinnedMeshRenderer), "blendShape." + shape), curve);

    static void Highlight(AnimationClip clip, AnimationCurve curve)
    {
        if (C.Pet.Highlight == null) return;
        AnimationUtility.SetEditorCurve(clip,
            EditorCurveBinding.FloatCurve(M.RobotPath + "/" + C.Pet.Highlight, typeof(SkinnedMeshRenderer), "m_Enabled"), curve);
    }

    static AnimationCurve Linear(params Keyframe[] keys)
    {
        var curve = new AnimationCurve(keys);
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        }
        return curve;
    }

    static void Loop(AnimationClip clip, bool loop)
    {
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
    }

    static AnimatorState State(AnimatorStateMachine sm, string name, float y, AnimationClip clip, string path)
    {
        var s = sm.AddState(name, new Vector3(300, y));
        s.motion = TranslatorAnimationBuilder.SaveClip(clip, path);
        s.writeDefaultValues = false;
        return s;
    }

    static AnimatorStateTransition Go(AnimatorState from, AnimatorState to, float duration, float? exitTime = null)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = exitTime.HasValue;
        t.exitTime = exitTime ?? 0f;
        t.duration = duration;
        return t;
    }
}
