using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using C = TranslatorConfig;
using M = TranslatorMotionBuilder;

/// <summary>
/// The pet as a humanoid model instead of the robot (TranslatorConfig.PetModelPath; the BOOTH build leaves it empty).
/// The model's own motions are humanoid, which an avatar's FX layer cannot play on a second rig, so they are baked
/// here into plain bone rotations (and the hips' position) under Pet_Robot, which FX can play.
/// One Body layer owns every bone: Idle ⇄ Walk by speed; a still, silent minute → Bored (IdleB);
/// moving or talking → Wake (one Jump) → Idle.
/// </summary>
public static class TranslatorPetModel
{
    public const string BodyLayerName = "Translator_Body";
    const float Fps = 30f;
    const string LeftEye = "Left Eye";

    /// <summary>The configured model, or null for the robot.</summary>
    public static GameObject Configured() =>
        string.IsNullOrEmpty(C.PetModelPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(C.PetModelPath);

    /// <summary>The model as Pet_Robot under parent, unpacked and without its Animator.</summary>
    public static GameObject Build(Transform parent, GameObject model)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = C.RobotName;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        foreach (var a in go.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
        CopyMaterials(go, AssetDatabase.LoadAssetAtPath<GameObject>(C.PetMaterialSource));
        return go;
    }

    /// <summary>Each renderer takes the materials of the same-named renderer in source (when there is one).</summary>
    static void CopyMaterials(GameObject target, GameObject source)
    {
        if (source == null) return;
        var byName = source.GetComponentsInChildren<Renderer>(true).GroupBy(r => r.name).ToDictionary(g => g.Key, g => g.First());
        foreach (var r in target.GetComponentsInChildren<Renderer>(true))
            if (byName.TryGetValue(r.name, out var s)) r.sharedMaterials = s.sharedMaterials;
    }

    public static Transform FindDeep(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

    public static bool IsModel(Transform robot) => robot != null && FindDeep(robot, LeftEye) != null;

    /// <summary>Eye height above the pet's feet in its own (unscaled) space, in the rest pose.</summary>
    public static float EyeHeight(Transform robot) => robot.InverseTransformPoint(FindDeep(robot, LeftEye).position).y;

    /// <summary>Top of the model above its feet (unscaled), from its meshes.</summary>
    public static float TopHeight(Transform robot)
    {
        float top = 0f;
        foreach (var r in robot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            top = Mathf.Max(top, robot.InverseTransformPoint(r.bounds.max).y);
        return top > 0f ? top : EyeHeight(robot) + 0.15f;
    }

    /// <summary>
    /// Scales the placed pet so its eyes are at PetHeightRatio of the avatar's view height, and lifts the
    /// text and hearts to stay just above the head.
    /// </summary>
    public static void FitTo(GameObject instance, VRCAvatarDescriptor avatar)
    {
        var robot = instance.transform.Find(C.PetRootName + "/" + M.RobotPath);
        if (!IsModel(robot) || avatar.ViewPosition.y <= 0f) return;
        float s = C.PetHeightRatio * avatar.ViewPosition.y / EyeHeight(robot);
        robot.localScale = Vector3.one * s;
        Lift(instance.transform.Find(C.PetRootName + "/" + C.PetFollowName + "/" + C.TextName), s, C.TextHeightAboveRobot);
        Lift(instance.transform.Find(C.PetRootName + "/" + C.PetFollowName + "/" + C.HeartsName), s, 0f);
        PrefabUtility.RecordPrefabInstancePropertyModifications(robot);
    }

    /// <summary>prefab height = top + margin; placed height = top * s + margin.</summary>
    static void Lift(Transform t, float s, float margin)
    {
        if (t == null) return;
        var source = PrefabUtility.GetCorrespondingObjectFromSource(t);
        float y = source != null ? source.localPosition.y : t.localPosition.y;
        var p = t.localPosition;
        p.y = (y - margin) * s + margin;
        t.localPosition = p;
        PrefabUtility.RecordPrefabInstancePropertyModifications(t);
    }

    public static void AddBodyLayer(AnimatorController controller, AnimatorStateMachine sm, GameObject model, string outputDir)
    {
        controller.AddParameter(C.VoiceParam, AnimatorControllerParameterType.Float);
        controller.AddParameter(C.ArrivedParam, AnimatorControllerParameterType.Bool);
        controller.AddParameter(C.CloseParam, AnimatorControllerParameterType.Bool);
        var walkBody = Bake(Load(C.PetWalkClip), model, M.RobotPath);
        // Walk: toward the waiting spot (which turns with the wearer), facing it — its own direction of travel,
        // even when the wearer walks backwards. Approach: the same once the wearer stops. Land: a jump onto the
        // spot. Everything else holds the spot where it is (so walking up to the pet or looking around never moves
        // it) and faces the way the wearer faced; it sets off only once the wearer is no longer close.
        var idleClip = Body(Bake(Load(C.PetIdleClip), model, M.RobotPath), Pose.Wait, loop: true);
        var idle = State(sm, "Idle", 100, idleClip, outputDir);
        var walk = State(sm, "Walk", 200, Body(walkBody, Pose.Walk, loop: true), outputDir);
        var approach = State(sm, "Approach", 250, Body(Object.Instantiate(walkBody), Pose.Approach, loop: true), outputDir);
        var land = State(sm, "Land", 270, Body(Bake(Load(C.PetWakeClip), model, M.RobotPath), Pose.Land, loop: false), outputDir);
        var drowsy = State(sm, "Drowsy", 300, (AnimationClip)idle.motion, outputDir, save: false);
        var bored = State(sm, "Bored", 400, Body(Bake(Load(C.PetBoredClip), model, M.RobotPath), Pose.Wait, loop: true), outputDir);
        var wake = State(sm, "Wake", 500, Body(Bake(Load(C.PetWakeClip), model, M.RobotPath), Pose.Wait, loop: false), outputDir);
        sm.defaultState = idle;

        var setOff = Go(idle, walk, Turn);
        setOff.AddCondition(AnimatorConditionMode.IfNot, 0f, C.CloseParam);
        setOff.AddCondition(AnimatorConditionMode.Greater, C.HopStartSpeed, C.VelocityParam);
        Go(idle, approach, Turn).AddCondition(AnimatorConditionMode.IfNot, 0f, C.CloseParam);
        Go(idle, approach, Turn).AddCondition(AnimatorConditionMode.IfNot, 0f, C.ArrivedParam);
        var settle = Go(idle, drowsy, 0f);
        settle.AddCondition(AnimatorConditionMode.Less, C.SleepStillSpeed, C.VelocityParam);
        settle.AddCondition(AnimatorConditionMode.Less, C.SleepVoiceLevel, C.VoiceParam);
        Go(walk, approach, Turn).AddCondition(AnimatorConditionMode.Less, C.HopStopSpeed, C.VelocityParam);
        Go(approach, walk, Turn).AddCondition(AnimatorConditionMode.Greater, C.HopStartSpeed, C.VelocityParam);
        Go(approach, land, 0.1f).AddCondition(AnimatorConditionMode.If, 0f, C.ArrivedParam);
        Go(land, idle, 0.2f, exitTime: 1f);

        // Any movement or sound while drowsy starts the minute again.
        Go(drowsy, idle, 0f).AddCondition(AnimatorConditionMode.Greater, C.SleepStillSpeed, C.VelocityParam);
        Go(drowsy, idle, 0f).AddCondition(AnimatorConditionMode.Greater, C.SleepVoiceLevel, C.VoiceParam);
        Go(drowsy, bored, 0.4f, exitTime: C.SleepAfterSeconds / idleClip.length);

        Go(bored, wake, 0.15f).AddCondition(AnimatorConditionMode.Greater, C.SleepStillSpeed, C.VelocityParam);
        Go(bored, wake, 0.15f).AddCondition(AnimatorConditionMode.Greater, C.SleepVoiceLevel, C.VoiceParam);
        Go(wake, idle, 0.2f, exitTime: 1f);
    }

    static AnimationClip Load(string path) =>
        AssetDatabase.LoadAssetAtPath<AnimationClip>(path) ?? throw new System.InvalidOperationException("[Translator] モーションがありません: " + path);

    static AnimatorState State(AnimatorStateMachine sm, string name, float y, AnimationClip clip, string outputDir, bool save = true)
    {
        var s = sm.AddState(name, new Vector3(300, y));
        s.motion = save ? TranslatorAnimationBuilder.SaveClip(clip, outputDir + "/Translator_Pet" + name + ".anim") : clip;
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

    const float Turn = 0.3f;

    enum Pose { Walk, Approach, Land, Wait }

    /// <summary>
    /// A baked motion plus where the pet goes and which way it faces: the waiting spot (all axes; the heading
    /// turns with the wearer while walking) and the facing weights (TranslatorPetWalker).
    /// </summary>
    static AnimationClip Body(AnimationClip clip, Pose pose, bool loop)
    {
        float end = Mathf.Max(clip.length, 1f / Fps);
        M.SetHeadingWeight(clip, pose == Pose.Walk ? C.PetModelHeadingWeight : 0f, end);
        M.SetTarget(clip, AnimationCurve.Constant(0f, end, C.PetOffset.z), end);
        bool going = pose is Pose.Walk or Pose.Approach;
        TranslatorPetWalker.SetBody(clip, end, spot: going ? 1f : 0f, ahead: going ? 0f : 1f,
            spotFollows: going ? 1000f : 0f, pull: pose == Pose.Land ? C.LandPull : 0f);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    /// <summary>
    /// Plays a humanoid clip on a hidden copy of the model and records every bone under the armature
    /// (rotations; the hips also their position) at 30 fps, plus the clip's blend-shape curves except Smile.
    /// Paths are prefixed with robotPath.
    /// </summary>
    public static AnimationClip Bake(AnimationClip source, GameObject model, string robotPath)
    {
        var temp = (GameObject)Object.Instantiate(model);
        temp.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var animator = temp.GetComponent<Animator>();
            var hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : FindDeep(temp.transform, "Hips");
            var armature = hips != null ? hips.parent : temp.transform;
            var bones = armature.GetComponentsInChildren<Transform>(true).Where(t => t != armature).ToArray();
            string Path(Transform t) => robotPath + "/" + AnimationUtility.CalculateTransformPath(t, temp.transform);

            int frames = Mathf.Max(2, Mathf.RoundToInt(source.length * Fps) + 1);
            var times = Enumerable.Range(0, frames).Select(f => Mathf.Min(f / Fps, source.length)).ToArray();
            var rot = bones.Select(_ => new Quaternion[frames]).ToArray();
            var hipPos = new Vector3[frames];
            for (int f = 0; f < frames; f++)
            {
                source.SampleAnimation(temp, times[f]);
                for (int b = 0; b < bones.Length; b++)
                {
                    var q = bones[b].localRotation;
                    if (f > 0 && Quaternion.Dot(q, rot[b][f - 1]) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    rot[b][f] = q;
                }
                if (hips != null) hipPos[f] = hips.localPosition;
            }

            var clip = new AnimationClip { frameRate = Fps };
            for (int b = 0; b < bones.Length; b++)
                for (int k = 0; k < 4; k++)
                    M.Set(clip, Path(bones[b]), "m_LocalRotation." + "xyzw"[k], Linear(times, rot[b].Select(q => q[k]).ToArray()));
            if (hips != null)
                for (int k = 0; k < 3; k++)
                    M.Set(clip, Path(hips), "m_LocalPosition." + "xyz"[k], Linear(times, hipPos.Select(p => p[k]).ToArray()));

            foreach (var binding in AnimationUtility.GetCurveBindings(source)
                         .Where(b => b.type == typeof(SkinnedMeshRenderer) && b.propertyName.StartsWith("blendShape.")
                                     && b.propertyName != "blendShape." + C.PetSmileShape && !TranslatorPetWalker.IsBlinkCurve(b)))
            {
                var copy = binding;
                copy.path = robotPath + "/" + binding.path;
                AnimationUtility.SetEditorCurve(clip, copy, AnimationUtility.GetEditorCurve(source, binding));
            }
            return clip;
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }
    }

    /// <summary>A curve through the samples with straight-line tangents, so 30 fps samples play back smoothly.</summary>
    static AnimationCurve Linear(float[] t, float[] v)
    {
        var keys = new Keyframe[t.Length];
        for (int i = 0; i < t.Length; i++)
        {
            int a = Mathf.Max(0, i - 1), b = Mathf.Min(t.Length - 1, i + 1);
            float slope = t[b] > t[a] ? (v[b] - v[a]) / (t[b] - t[a]) : 0f;
            keys[i] = new Keyframe(t[i], v[i], slope, slope);
        }
        return new AnimationCurve(keys);
    }

    /// <summary>Smile while petted, neutral otherwise (on every face mesh that may carry the shape).</summary>
    public static void AddSmile(AnimationClip clip, float weight, float seconds)
    {
        foreach (var mesh in C.PetSmileMeshes)
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(C.PetFollowName + "/" + C.PetHopName + "/" + C.RobotName + "/" + mesh,
                    typeof(SkinnedMeshRenderer), "blendShape." + C.PetSmileShape),
                AnimationCurve.Constant(0f, seconds, weight));
    }
}
