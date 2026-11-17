using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Dynamics.Constraint.Components;
using C = TranslatorConfig;

/// <summary>
/// The pet's own motion, merged on Pet_Root (paths are relative to it). Four layers:
/// Hop (walking: hop, and let the waiting spot turn with the wearer; standing: amble back and forth),
/// Look (face the wearer while they look at the pet), Pat (hearts while petted) and Sleep (TranslatorSleepBuilder).
/// </summary>
public static class TranslatorMotionBuilder
{
    public const string ControllerName = "Translator_Motion.controller";
    public const string HopLayerName = "Translator_Hop";
    public const string LookLayerName = "Translator_Look";
    public const string PatLayerName = "Translator_Pat";

    public static readonly string HopPath = C.PetFollowName + "/" + C.PetHopName;
    public static readonly string TargetPath = C.PetHeadingName + "/" + C.PetTargetName;
    public static readonly string RobotPath = HopPath + "/" + C.RobotName;
    public static readonly string HeartsPath = C.PetFollowName + "/" + C.HeartsName;

    /// <summary>Checked once per loop, just before landing, so the pet never stops mid-air.</summary>
    const float StopExitTime = 0.95f;
    const float Frame = 1f / 60f;

    public static AnimatorController BuildMotionController(string outputDir)
    {
        var controller = TranslatorAnimationBuilder.NewController(outputDir + "/" + ControllerName);
        controller.AddParameter(C.VelocityParam, AnimatorControllerParameterType.Float);
        controller.AddParameter(C.LookedParam, AnimatorControllerParameterType.Bool);
        controller.AddParameter(C.PatParam, AnimatorControllerParameterType.Bool);
        controller.RemoveLayer(0);

        BuildHopLayer(controller, outputDir);
        BuildTwoStateLayer(controller, LookLayerName, outputDir,
            "Away", WeightClip(typeof(VRCAimConstraint), RobotPath, "GlobalWeight", 0f, Frame),
            "Look", WeightClip(typeof(VRCAimConstraint), RobotPath, "GlobalWeight", 1f, Frame),
            C.LookedParam, C.LookTurnSeconds, holdSeconds: 0f);
        BuildTwoStateLayer(controller, PatLayerName, outputDir,
            "Calm", WeightClip(typeof(ParticleSystem), HeartsPath, "EmissionModule.enabled", 0f, Frame),
            "Hearts", WeightClip(typeof(ParticleSystem), HeartsPath, "EmissionModule.enabled", 1f, C.HeartsMinSeconds),
            C.PatParam, 0f, holdSeconds: C.HeartsMinSeconds);
        TranslatorSleepBuilder.AddLayer(controller, NewLayer(controller, TranslatorSleepBuilder.LayerName), outputDir);

        EditorUtility.SetDirty(controller);
        return controller;
    }

    internal static AnimatorStateMachine NewLayer(AnimatorController controller, string name)
    {
        controller.AddLayer(name);
        var layers = controller.layers;
        layers[layers.Length - 1].defaultWeight = 1f;
        controller.layers = layers;
        return layers[layers.Length - 1].stateMachine;
    }

    static void BuildHopLayer(AnimatorController controller, string outputDir)
    {
        var sm = NewLayer(controller, HopLayerName);
        var idle = sm.AddState("Idle", new Vector3(300, 100));
        var hop = sm.AddState("Hop", new Vector3(300, 200));
        sm.defaultState = idle;
        idle.motion = TranslatorAnimationBuilder.SaveClip(IdleClip(), outputDir + "/Translator_Idle.anim");
        hop.motion = TranslatorAnimationBuilder.SaveClip(HopClip(), outputDir + "/Translator_Hop.anim");
        idle.writeDefaultValues = false;
        hop.writeDefaultValues = false;

        // Different start and stop speeds, so walking slowly does not flicker between the two.
        var start = idle.AddTransition(hop);
        start.hasExitTime = false;
        start.duration = 0f;
        start.AddCondition(AnimatorConditionMode.Greater, C.HopStartSpeed, C.VelocityParam);

        var stop = hop.AddTransition(idle);
        stop.hasExitTime = true;
        stop.exitTime = StopExitTime;
        stop.duration = 0.05f;
        stop.AddCondition(AnimatorConditionMode.Less, C.HopStopSpeed, C.VelocityParam);
    }

    /// <summary>Off/on layer on a Bool. Fades over fadeSeconds; once on, stays at least holdSeconds.</summary>
    static void BuildTwoStateLayer(AnimatorController controller, string layer, string outputDir,
        string offName, AnimationClip offClip, string onName, AnimationClip onClip, string param,
        float fadeSeconds, float holdSeconds)
    {
        var sm = NewLayer(controller, layer);
        var off = sm.AddState(offName, new Vector3(300, 100));
        var on = sm.AddState(onName, new Vector3(300, 200));
        sm.defaultState = off;
        off.motion = TranslatorAnimationBuilder.SaveClip(offClip, outputDir + "/Translator_" + offName + ".anim");
        on.motion = TranslatorAnimationBuilder.SaveClip(onClip, outputDir + "/Translator_" + onName + ".anim");
        off.writeDefaultValues = false;
        on.writeDefaultValues = false;

        var enter = off.AddTransition(on);
        enter.hasExitTime = false;
        enter.duration = fadeSeconds;
        enter.AddCondition(AnimatorConditionMode.If, 0f, param);

        var leave = on.AddTransition(off);
        leave.hasExitTime = holdSeconds > 0f;
        leave.exitTime = 1f;
        leave.duration = fadeSeconds;
        leave.AddCondition(AnimatorConditionMode.IfNot, 0f, param);
    }

    static AnimationClip WeightClip(System.Type type, string path, string property, float value, float seconds)
    {
        var clip = new AnimationClip();
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property),
            AnimationCurve.Constant(0f, seconds, value));
        return clip;
    }

    /// <summary>
    /// Standing still: rest pose, the waiting spot keeps its direction (so turning on the spot leaves the pet
    /// alone), and the spot drifts forward and back so the pet ambles. Write Defaults is off, so every value is set.
    /// </summary>
    static AnimationClip IdleClip()
    {
        float w = C.WanderSeconds, d = C.WanderDistance;
        var clip = new AnimationClip();
        Set(clip, HopPath, "m_LocalPosition.y", AnimationCurve.Constant(0f, w, 0f));
        foreach (var axis in new[] { "x", "y", "z" })
            Set(clip, HopPath, "m_LocalScale." + axis, AnimationCurve.Constant(0f, w, 1f));
        SetHeadingWeight(clip, 0f, w);
        var wander = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(w / 4, d), new Keyframe(w / 2, 0f), new Keyframe(w * 3 / 4, -d), new Keyframe(w, 0f));
        for (int i = 0; i < wander.length; i++) wander.SmoothTangents(i, 0f);
        Set(clip, TargetPath, "m_LocalPosition.z", wander);
        Loop(clip);
        return clip;
    }

    /// <summary>One hop: squash on the ground, stretch at the top, squash again on landing. Loops.</summary>
    static AnimationClip HopClip()
    {
        float t = C.HopSeconds;
        var clip = new AnimationClip();

        // Height follows a parabola: launch fast, slow at the top, fall fast.
        float launch = 4f * C.HopHeight / t;
        Set(clip, HopPath, "m_LocalPosition.y", new AnimationCurve(
            new Keyframe(0f, 0f, launch, launch),
            new Keyframe(t / 2, C.HopHeight, 0f, 0f),
            new Keyframe(t, 0f, -launch, -launch)));

        for (int i = 0; i < 3; i++)
        {
            string axis = "xyz"[i].ToString();
            Set(clip, HopPath, "m_LocalScale." + axis, new AnimationCurve(
                new Keyframe(0f, C.HopSquash[i]),
                new Keyframe(t * 0.15f, 1f),
                new Keyframe(t / 2, C.HopStretch[i]),
                new Keyframe(t * 0.85f, 1f),
                new Keyframe(t, C.HopSquash[i])));
        }

        // While walking, the waiting spot turns with the wearer and stops ambling.
        SetHeadingWeight(clip, C.MovingHeadingWeight, t);
        Set(clip, TargetPath, "m_LocalPosition.z", AnimationCurve.Constant(0f, t, 0f));
        Loop(clip);
        return clip;
    }

    static void SetHeadingWeight(AnimationClip clip, float weight, float seconds) =>
        AnimationUtility.SetEditorCurve(clip,
            EditorCurveBinding.FloatCurve(C.PetHeadingName, typeof(VRCRotationConstraint), C.HeadingWeightProperty),
            AnimationCurve.Constant(0f, seconds, weight));

    static void Loop(AnimationClip clip)
    {
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
    }

    static void Set(AnimationClip clip, string path, string property, AnimationCurve curve) =>
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), curve);
}
