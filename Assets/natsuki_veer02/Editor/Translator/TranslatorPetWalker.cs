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
/// How a humanoid pet walks: when the wearer moves, straight toward its waiting spot (front right of the wearer),
/// facing the way it goes; once there it turns to face the wearer. Plus blinking.
/// </summary>
public static class TranslatorPetWalker
{
    public const string BlinkLayerName = "Translator_Blink";

    /// <summary>Turns Pet_Hop (and so the pet) toward the waiting spot or the wearer — whichever the Body layer weights.</summary>
    public static void AddFacing(GameObject hop, Transform spot, Transform wearer)
    {
        var aim = hop.AddComponent<VRCAimConstraint>();
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

    /// <summary>The Body state's facing weights on Pet_Hop: the waiting spot and the wearer.</summary>
    public static void SetFacing(AnimationClip clip, float seconds, float spot, float wearer)
    {
        Weight(clip, M.HopPath, typeof(VRCAimConstraint), 0, spot, seconds);
        Weight(clip, M.HopPath, typeof(VRCAimConstraint), 1, wearer, seconds);
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
