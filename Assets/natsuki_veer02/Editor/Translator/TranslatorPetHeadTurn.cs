using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using C = TranslatorConfig;

/// <summary>
/// Looking at the wearer, she turns only her head while they are within HeadTurnDegrees of where her body faces;
/// further round, her body turns to them as well (blending in up to BodyTurnDegrees), so her neck never twists too far.
/// How far round they are: Pet_ViewDir (on Pet_Hop, which faces where she would without looking) aims at the
/// wearer's head, yaw only; its child Pet_ViewProbe, 1 m along it, is a tiny contact sender; a proximity receiver
/// 1 m ahead of Pet_Hop (Pet_ViewZone) gives InViewParam: 1 straight ahead, 0 from BodyTurnDegrees round.
/// The probe moves on a 1 m circle, so this is the angle alone, whatever the distance or height.
/// </summary>
public static class TranslatorPetHeadTurn
{
    public const string DirName = "Pet_ViewDir";
    public const string ProbeName = "Pet_ViewProbe";
    public const string ZoneName = "Pet_ViewZone";
    public const string ProbeTag = "TranslatorPetView";
    const float ProbeRadius = 0.01f;

    /// <summary>The distance between two points 1 m out, this many degrees apart.</summary>
    public static float Chord(float degrees) => 2f * Mathf.Sin(degrees * Mathf.Deg2Rad / 2f);

    /// <summary>The receiver reaches the probe up to BodyTurnDegrees round.</summary>
    public static float ZoneRadius => Chord(C.BodyTurnDegrees);

    /// <summary>InViewParam at HeadTurnDegrees round: at or above it, her head alone turns.</summary>
    public static float HeadOnlyFrom => 1f - Chord(C.HeadTurnDegrees) / ZoneRadius;

    /// <summary>hop: Pet_Hop; gaze: the wearer's head (Pet_Gaze).</summary>
    public static void Add(Transform hop, Transform gaze)
    {
        var dir = new GameObject(DirName).transform;
        dir.SetParent(hop, false);
        var aim = dir.gameObject.AddComponent<VRCAimConstraint>();
        aim.Sources.Add(new VRCConstraintSource(gaze, 1f));
        aim.AimAxis = Vector3.forward;
        aim.UpAxis = Vector3.up;
        aim.WorldUp = VRCConstraintBase.WorldUpType.SceneUp;
        aim.AffectsRotationX = false;
        aim.AffectsRotationZ = false;
        aim.IsActive = true;
        aim.Locked = true;

        var probe = new GameObject(ProbeName);
        probe.transform.SetParent(dir, false);
        probe.transform.localPosition = Vector3.forward;
        var s = probe.AddComponent<VRCContactSender>();
        s.shapeType = ContactBase.ShapeType.Sphere;
        s.radius = ProbeRadius;
        s.collisionTags = new List<string> { ProbeTag };

        var zone = new GameObject(ZoneName);
        zone.transform.SetParent(hop, false);
        zone.transform.localPosition = Vector3.forward;
        var r = zone.AddComponent<VRCContactReceiver>();
        r.shapeType = ContactBase.ShapeType.Sphere;
        r.radius = ZoneRadius;
        r.collisionTags = new List<string> { ProbeTag };
        r.allowSelf = true;
        r.allowOthers = false;
        r.localOnly = false; // worked out on every screen, like the rest of her reactions
        r.receiverType = ContactReceiver.ReceiverType.Proximity;
        r.parameter = C.InViewParam;
    }

    /// <summary>
    /// The look as a blend: bodyLook (her body turned to the wearer, as before) when they are far round, the same
    /// with her body left alone (robotPath's aim at 0) when they are within HeadTurnDegrees. Saved in the controller.
    /// </summary>
    public static BlendTree Blend(AnimatorController controller, string name, AnimationClip bodyLook, string robotPath,
        string outputDir)
    {
        if (System.Array.TrueForAll(controller.parameters, p => p.name != C.InViewParam))
            controller.AddParameter(C.InViewParam, AnimatorControllerParameterType.Float);
        var headLook = Object.Instantiate(bodyLook);
        var binding = EditorCurveBinding.FloatCurve(robotPath, typeof(VRCAimConstraint), "GlobalWeight");
        float seconds = bodyLook.length;
        AnimationUtility.SetEditorCurve(headLook, binding, AnimationCurve.Constant(0f, seconds, 0f));
        headLook = TranslatorAnimationBuilder.SaveClip(headLook, outputDir + "/Translator_" + name + "Head.anim");

        var tree = new BlendTree
        {
            name = name,
            blendType = BlendTreeType.Simple1D,
            blendParameter = C.InViewParam,
            useAutomaticThresholds = false,
            hideFlags = HideFlags.HideInHierarchy,
        };
        tree.AddChild(bodyLook, 0f);
        tree.AddChild(headLook, HeadOnlyFrom);
        AssetDatabase.AddObjectToAsset(tree, controller);
        return tree;
    }
}
