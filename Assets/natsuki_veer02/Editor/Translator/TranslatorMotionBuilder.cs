using System.Linq;
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
    public static readonly string TargetPath = C.PetHeadingName + "/" + C.PetSideName + "/" + C.PetTargetName;
    public static readonly string RobotPath = HopPath + "/" + C.RobotName;
    public static readonly string HeartsPath = C.PetFollowName + "/" + C.HeartsName;

    /// <summary>Checked once per loop, just before landing, so the pet never stops mid-air.</summary>
    const float StopExitTime = 0.95f;
    const float Frame = 1f / 60f;

    /// <summary>petModel null: the robot (hop, sleep). A model: TranslatorPetModel's Body layer instead.</summary>
    public static AnimatorController BuildMotionController(string outputDir, GameObject petModel = null)
    {
        var controller = TranslatorAnimationBuilder.NewController(outputDir + "/" + ControllerName);
        controller.AddParameter(C.VelocityParam, AnimatorControllerParameterType.Float);
        controller.AddParameter(C.LookedParam, AnimatorControllerParameterType.Bool);
        controller.AddParameter(C.PatParam, AnimatorControllerParameterType.Bool);
        controller.RemoveLayer(0);

        if (petModel == null) BuildHopLayer(controller, outputDir);
        else TranslatorPetModel.AddBodyLayer(controller, NewLayer(controller, TranslatorPetModel.BodyLayerName), petModel, outputDir);
        var away = WeightClip(typeof(VRCAimConstraint), RobotPath, "GlobalWeight", 0f, Frame);
        var look = WeightClip(typeof(VRCAimConstraint), RobotPath, "GlobalWeight", 1f, Frame);
        if (petModel != null)
        {
            // Her head also tilts toward the wearer's face (TranslatorReactionsBuilder.AddHeadLook).
            string head = TranslatorPetModel.HeadPath(petModel);
            foreach (var (clip, w) in new[] { (away, 0f), (look, C.HeadLookWeight) })
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(head, typeof(VRCAimConstraint), "GlobalWeight"),
                    AnimationCurve.Constant(0f, Frame, w));
        }
        var lookLayer = BuildTwoStateLayer(controller, LookLayerName, outputDir, "Away", away, "Look", look,
            C.LookedParam, C.LookTurnSeconds, holdSeconds: 0f);
        if (petModel != null) AddCalled(controller, lookLayer, look, outputDir);
        var calm = WeightClip(typeof(ParticleSystem), HeartsPath, "EmissionModule.enabled", 0f, Frame);
        var hearts = WeightClip(typeof(ParticleSystem), HeartsPath, "EmissionModule.enabled", 1f, C.HeartsMinSeconds);
        BuildTwoStateLayer(controller, PatLayerName, outputDir, "Calm", calm, "Hearts", hearts,
            C.PatParam, 0f, holdSeconds: C.HeartsMinSeconds);
        if (petModel != null) TranslatorPetEyes.AddLayer(NewLayer(controller, TranslatorPetWalker.BlinkLayerName), outputDir);
        if (petModel != null) TranslatorPetWalker.AddSideLayer(controller, NewLayer(controller, TranslatorPetModel.SideLayerName), outputDir);
        if (petModel != null) TranslatorPetCommands.AddWaitLayer(controller, NewLayer(controller, TranslatorPetCommands.WaitLayerName), petModel, outputDir);
        if (petModel != null)
        {
            TranslatorPetCommands.AddTurnLayer(controller, NewLayer(controller, TranslatorPetCommands.TurnLayerName), outputDir);
            TranslatorPetCommands.NoLookWhileTurned(lookLayer);
            TranslatorPetGaze.AddLayer(controller, NewLayer(controller, TranslatorPetGaze.LayerName),
                TranslatorPetModel.HeadPath(petModel) + "/" + TranslatorPetGaze.AimName, outputDir);
        }
        if (petModel == null)
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

    /// <summary>
    /// Called by name: from Away she turns to the wearer as for a look, for CalledSeconds, then turns back
    /// (or keeps looking, if the wearer is looking at her by then). The eye layer smiles (TranslatorPetEyes).
    /// </summary>
    static void AddCalled(AnimatorController controller, AnimatorStateMachine sm, AnimationClip look, string outputDir)
    {
        controller.AddParameter(C.CalledParam, AnimatorControllerParameterType.Bool);
        var states = sm.states.Select(s => s.state).ToArray();
        var away = states.Single(s => s.name == "Away");
        var looking = states.Single(s => s.name == "Look");
        var clip = new AnimationClip();
        foreach (var b in AnimationUtility.GetCurveBindings(look))
            AnimationUtility.SetEditorCurve(clip, b, AnimationCurve.Constant(0f, C.CalledSeconds, AnimationUtility.GetEditorCurve(look, b).Evaluate(0f)));
        var called = sm.AddState("Called", new Vector3(550, 200));
        called.motion = TranslatorAnimationBuilder.SaveClip(clip, outputDir + "/Translator_Called.anim");
        called.writeDefaultValues = false;

        var enter = away.AddTransition(called);
        enter.hasExitTime = false;
        enter.duration = C.LookTurnSeconds;
        enter.AddCondition(AnimatorConditionMode.If, 0f, C.CalledParam);
        var keep = called.AddTransition(looking);
        keep.hasExitTime = false;
        keep.duration = 0f;
        keep.AddCondition(AnimatorConditionMode.If, 0f, C.LookedParam);
        var back = called.AddTransition(away);
        back.hasExitTime = true;
        back.exitTime = 1f;
        back.duration = C.LookTurnSeconds;
    }

    /// <summary>Off/on layer on a Bool. Fades over fadeSeconds; once on, stays at least holdSeconds.</summary>
    static AnimatorStateMachine BuildTwoStateLayer(AnimatorController controller, string layer, string outputDir,
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
        return sm;
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
        SetHopFlat(clip, AnimationCurve.Constant(0f, w, 0f), w);
        foreach (var axis in new[] { "x", "y", "z" })
            Set(clip, HopPath, "m_LocalScale." + axis, AnimationCurve.Constant(0f, w, 1f));
        SetHeadingWeight(clip, 0f, w);
        float z0 = C.PetOffset.z;
        var wander = new AnimationCurve(
            new Keyframe(0f, z0), new Keyframe(w / 4, z0 + d), new Keyframe(w / 2, z0), new Keyframe(w * 3 / 4, z0 - d), new Keyframe(w, z0));
        for (int i = 0; i < wander.length; i++) wander.SmoothTangents(i, 0f);
        SetTarget(clip, wander, w);
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
        SetHopFlat(clip, new AnimationCurve(
            new Keyframe(0f, 0f, launch, launch),
            new Keyframe(t / 2, C.HopHeight, 0f, 0f),
            new Keyframe(t, 0f, -launch, -launch)), t);

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
        SetTarget(clip, AnimationCurve.Constant(0f, t, C.PetOffset.z), t);
        Loop(clip);
        return clip;
    }

    /// <summary>
    /// The animator writes a Transform's whole position, and an axis a clip leaves out becomes 0 — that put the pet
    /// inside the wearer's feet. So the waiting spot always gets all three axes: PetOffset, with z given.
    /// </summary>
    internal static void SetTarget(AnimationClip clip, AnimationCurve z, float seconds, Vector3? offset = null)
    {
        var o = offset ?? C.PetOffset;
        Set(clip, TargetPath, "m_LocalPosition.x", AnimationCurve.Constant(0f, seconds, o.x));
        Set(clip, TargetPath, "m_LocalPosition.y", AnimationCurve.Constant(0f, seconds, o.y));
        Set(clip, TargetPath, "m_LocalPosition.z", z);
    }

    /// <summary>The hop's height, with x and z held at 0 for the same reason.</summary>
    static void SetHopFlat(AnimationClip clip, AnimationCurve y, float seconds)
    {
        Set(clip, HopPath, "m_LocalPosition.x", AnimationCurve.Constant(0f, seconds, 0f));
        Set(clip, HopPath, "m_LocalPosition.y", y);
        Set(clip, HopPath, "m_LocalPosition.z", AnimationCurve.Constant(0f, seconds, 0f));
    }

    internal static void SetHeadingWeight(AnimationClip clip, float weight, float seconds) =>
        AnimationUtility.SetEditorCurve(clip,
            EditorCurveBinding.FloatCurve(C.PetHeadingName, typeof(VRCRotationConstraint), C.HeadingWeightProperty),
            AnimationCurve.Constant(0f, seconds, weight));

    static void Loop(AnimationClip clip)
    {
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
    }

    internal static void Set(AnimationClip clip, string path, string property, AnimationCurve curve) =>
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), curve);
}
