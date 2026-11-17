using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using C = TranslatorConfig;
using M = TranslatorMotionBuilder;

/// <summary>
/// How a humanoid pet walks: along the wearer's own path, facing the way it goes (never backwards), then round to
/// the waiting spot, where it turns to face the wearer. Plus blinking.
/// Avatars cannot run scripts, so the path is a chain of invisible points (Pet_Trail) under the world-frozen
/// Pet_Root: the first creeps toward the wearer each frame, each next one toward the one ahead, so the last one
/// traces the wearer's path some way behind. Sharp corners are cut a little.
/// </summary>
public static class TranslatorPetWalker
{
    public const string TrailName = "Pet_Trail";
    public const string BlinkLayerName = "Translator_Blink";
    const float Frame = 1f / 60f;

    /// <summary>The chain of points under petRoot; Trail0 follows wearer (the TranslatorPet root).</summary>
    public static Transform[] AddTrail(Transform petRoot, Transform wearer)
    {
        var trail = new GameObject(TrailName).transform;
        trail.SetParent(petRoot, false);
        var links = new Transform[C.TrailLinks];
        for (int i = 0; i < links.Length; i++)
        {
            links[i] = new GameObject("Trail" + i).transform;
            links[i].SetParent(trail, false);
            var c = links[i].gameObject.AddComponent<VRCPositionConstraint>();
            c.Sources.Add(new VRCConstraintSource(links[i], 1f));
            c.Sources.Add(new VRCConstraintSource(i == 0 ? wearer : links[i - 1], C.TrailWeight));
            c.IsActive = true;
            c.Locked = true;
        }
        return links;
    }

    /// <summary>A third source on Pet_Follow's damped follow: the end of the trail, off until walking.</summary>
    public static void RideTrail(GameObject follow, Transform last)
    {
        var c = follow.GetComponent<VRCParentConstraint>();
        c.Sources.Add(new VRCConstraintSource(last, 0f));
    }

    /// <summary>
    /// Turns Pet_Hop (and so the pet) toward the path ahead, the waiting spot, or the wearer — whichever the Body
    /// layer weights. Only around the vertical axis.
    /// </summary>
    public static void AddFacing(GameObject hop, Transform ahead, Transform spot, Transform wearer)
    {
        var aim = hop.AddComponent<VRCAimConstraint>();
        aim.Sources.Add(new VRCConstraintSource(ahead, 0f));
        aim.Sources.Add(new VRCConstraintSource(spot, 0f));
        aim.Sources.Add(new VRCConstraintSource(wearer, 1f));
        aim.AimAxis = Vector3.forward;
        aim.UpAxis = Vector3.up;
        aim.WorldUp = VRCConstraintBase.WorldUpType.SceneUp;
        aim.AffectsRotationX = false;
        aim.AffectsRotationZ = false;
        aim.IsActive = true;
        aim.Locked = true;
    }

    /// <summary>On while the pet stands within ArriveRadius of its waiting spot (the pet's own Home sender).</summary>
    public static void AddArrive(GameObject target)
    {
        var r = target.AddComponent<VRCContactReceiver>();
        r.shapeType = ContactBase.ShapeType.Sphere;
        r.radius = C.ArriveRadius;
        r.collisionTags = new List<string> { C.PetContactTag };
        r.allowSelf = true;
        r.allowOthers = false;
        r.localOnly = false;
        r.receiverType = ContactReceiver.ReceiverType.Constant;
        r.parameter = C.ArrivedParam;
    }

    /// <summary>The Body state's follow and facing weights (trail on Pet_Follow; ahead/spot/wearer on Pet_Hop).</summary>
    public static void SetWalking(AnimationClip clip, float seconds, float trail, float ahead, float spot, float wearer)
    {
        Weight(clip, C.PetFollowName, typeof(VRCParentConstraint), 2, trail, seconds);
        Weight(clip, M.HopPath, typeof(VRCAimConstraint), 0, ahead, seconds);
        Weight(clip, M.HopPath, typeof(VRCAimConstraint), 1, spot, seconds);
        Weight(clip, M.HopPath, typeof(VRCAimConstraint), 2, wearer, seconds);
    }

    /// <summary>
    /// Join: the trail's pull grows from nothing to full over the clip, so the pet walks from its spot onto the
    /// wearer's path instead of jumping to the trail's end (at the wearer's feet while they stood).
    /// </summary>
    public static void RampTrail(AnimationClip clip, float seconds)
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(seconds * 0.3f, 0.03f),
            new Keyframe(seconds * 0.6f, 0.15f), new Keyframe(seconds * 0.85f, 1f), new Keyframe(seconds, 1000f));
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        }
        AnimationUtility.SetEditorCurve(clip,
            EditorCurveBinding.FloatCurve(C.PetFollowName, typeof(VRCParentConstraint), "Sources.source2.Weight"), curve);
    }

    static void Weight(AnimationClip clip, string path, System.Type type, int source, float value, float seconds) =>
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, "Sources.source" + source + ".Weight"),
            AnimationCurve.Constant(0f, seconds, value));

    /// <summary>One looping state: eyes open, then shut and open again quickly at the end of every BlinkSeconds.</summary>
    public static void AddBlinkLayer(AnimatorStateMachine sm, string outputDir)
    {
        float t = C.BlinkSeconds;
        var clip = new AnimationClip();
        foreach (var (mesh, shape) in C.PetBlinkShapes)
        {
            var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(t - 0.16f, 0f), new Keyframe(t - 0.09f, 100f),
                new Keyframe(t - 0.02f, 0f), new Keyframe(t, 0f));
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(M.RobotPath + "/" + mesh, typeof(SkinnedMeshRenderer), "blendShape." + shape), curve);
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        var s = sm.AddState("Blink", new Vector3(300, 100));
        s.motion = TranslatorAnimationBuilder.SaveClip(clip, outputDir + "/Translator_PetBlink.anim");
        s.writeDefaultValues = false;
        sm.defaultState = s;
    }

    /// <summary>The eyelid curves the blink layer owns, so baked motions leave them alone.</summary>
    public static bool IsBlinkCurve(EditorCurveBinding b)
    {
        foreach (var (_, shape) in C.PetBlinkShapes)
            if (b.propertyName == "blendShape." + shape) return true;
        return false;
    }
}
