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
/// How a humanoid pet walks: once the wearer is away from it, straight toward its waiting spot (front right of the
/// wearer), facing the way it goes, jumping the last bit; there it faces the way the wearer faced (the Look layer
/// turns it to the wearer while they look at it). While it waits the spot stays put, so the wearer can walk up to
/// it or look around. Plus blinking.
/// </summary>
public static class TranslatorPetWalker
{
    public const string BlinkLayerName = "Translator_Blink";
    public const string AheadName = "Pet_Ahead";

    /// <summary>A point far in front of the spot, along the heading (the wearer's direction when it last moved).</summary>
    public static Transform AddAhead(Transform heading)
    {
        var ahead = new GameObject(AheadName).transform;
        ahead.SetParent(heading, false);
        ahead.localPosition = new Vector3(C.PetOffset.x, 0f, C.PetOffset.z + C.AheadDistance);
        return ahead;
    }

    /// <summary>The spot's follow of the wearer becomes a weight the Body layer sets (source 0); otherwise it stays.</summary>
    public static void MakeSpotHoldable(GameObject heading)
    {
        var pos = heading.GetComponent<VRCPositionConstraint>();
        var wearer = pos.Sources[0];
        wearer.Weight = 1000f;
        pos.Sources[0] = wearer;
        pos.Sources.Add(new VRCConstraintSource(heading.transform, 1f));
    }

    /// <summary>A third source on Pet_Follow's damped follow: the spot again, raised while jumping in.</summary>
    public static void AddLandingPull(GameObject follow, Transform spot) =>
        follow.GetComponent<VRCParentConstraint>().Sources.Add(new VRCConstraintSource(spot, 0f));

    /// <summary>"Close": a receiver on the pet, the wearer's sender on the avatar side.</summary>
    public static void AddClose(GameObject follow, GameObject wearer)
    {
        var r = follow.AddComponent<VRCContactReceiver>();
        r.shapeType = ContactBase.ShapeType.Sphere;
        r.radius = C.CloseRadius;
        r.collisionTags = new List<string> { C.WearerContactTag };
        r.allowSelf = true;
        r.allowOthers = false;
        r.localOnly = false;
        r.receiverType = ContactReceiver.ReceiverType.Constant;
        r.parameter = C.CloseParam;

        var s = wearer.AddComponent<VRCContactSender>();
        s.shapeType = ContactBase.ShapeType.Sphere;
        s.radius = 0.05f;
        s.collisionTags = new List<string> { C.WearerContactTag };
    }

    /// <summary>Turns Pet_Hop (and so the pet) toward the waiting spot or the point ahead — whichever the Body layer weights.</summary>
    public static void AddFacing(GameObject hop, Transform spot, Transform ahead)
    {
        var aim = hop.AddComponent<VRCAimConstraint>();
        aim.Sources.Add(new VRCConstraintSource(spot, 0f));
        aim.Sources.Add(new VRCConstraintSource(ahead, 1f));
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

    /// <summary>
    /// A Body state's weights: facing the spot or the point ahead (Pet_Hop), whether the spot follows the wearer
    /// (Pet_Heading), and the landing pull (Pet_Follow).
    /// </summary>
    public static void SetBody(AnimationClip clip, float seconds, float spot, float ahead, float spotFollows, float pull)
    {
        Weight(clip, M.HopPath, typeof(VRCAimConstraint), 0, spot, seconds);
        Weight(clip, M.HopPath, typeof(VRCAimConstraint), 1, ahead, seconds);
        Weight(clip, C.PetHeadingName, typeof(VRCPositionConstraint), 0, spotFollows, seconds);
        Weight(clip, C.PetFollowName, typeof(VRCParentConstraint), 2, pull, seconds);
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
