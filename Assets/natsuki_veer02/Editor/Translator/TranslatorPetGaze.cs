using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using C = TranslatorConfig;

/// <summary>
/// 視線調節 (gaze adjustment): while the toggle is on, her eyes turn as the wearer's head turns — relative to the
/// wearer's body, applied relative to her head — so the wearer can point them; turned off, they keep that direction.
/// Pet_GazeLook (prefab root, turns with the body) copies the wearer's head (Pet_Gaze); Pet_EyeAim (her head) copies
/// it in local space while its constraint is active, and is left where it was when it is switched off; each eye
/// follows a child of the aim set to the eye's modelled rotation.
/// </summary>
public static class TranslatorPetGaze
{
    public const string LayerName = "Translator_GazeAdjust";
    public const string LookName = "Pet_GazeLook";
    public const string AimName = "Pet_EyeAim";

    public static string EyeTargetName(string eyeBone) => "Pet_EyeTarget_" + eyeBone;

    /// <summary>root: the prefab root; gaze: the wearer-head object (Pet_Gaze); head: her head bone.</summary>
    public static void Add(Transform root, Transform gaze, Transform head)
    {
        var look = new GameObject(LookName).transform;
        look.SetParent(root, false);
        Copy(look, gaze, local: false, active: true);

        var aim = new GameObject(AimName).transform;
        aim.SetParent(head, false);
        Copy(aim, look, local: true, active: false);

        foreach (var eyeName in new[] { C.Pet.LeftEyeBone, C.Pet.RightEyeBone })
        {
            var eye = TranslatorPetModel.FindDeep(head, eyeName);
            if (eye == null) continue;
            var target = new GameObject(EyeTargetName(eyeName)).transform;
            target.SetParent(aim, false);
            target.localRotation = Quaternion.Inverse(head.rotation) * eye.rotation; // the eye as modelled, from the head
            Copy(eye, target, local: false, active: true);
        }
    }

    static void Copy(Transform t, Transform source, bool local, bool active)
    {
        var rot = t.gameObject.AddComponent<VRCRotationConstraint>();
        rot.Sources.Add(new VRCConstraintSource(source, 1f));
        rot.SolveInLocalSpace = local;
        rot.IsActive = active;
        rot.Locked = true;
    }

    /// <summary>Hold (default: the aim keeps its direction) ⇄ Adjust (the aim follows the wearer's look).</summary>
    public static void AddLayer(AnimatorController controller, AnimatorStateMachine sm, string aimPath, string outputDir)
    {
        TranslatorPetCommands.EnsureBool(controller, C.GazeAdjustParam);
        AnimatorState State(string name, float on, float y)
        {
            var clip = new AnimationClip();
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(aimPath, typeof(VRCRotationConstraint), "IsActive"),
                AnimationCurve.Constant(0f, 1f / 60f, on));
            var s = sm.AddState(name, new Vector3(300, y));
            s.motion = TranslatorAnimationBuilder.SaveClip(clip, outputDir + "/Translator_Gaze" + name + ".anim");
            s.writeDefaultValues = false;
            return s;
        }
        var hold = State("Hold", 0f, 100);
        var adjust = State("Adjust", 1f, 200);
        sm.defaultState = hold;
        foreach (var (from, to, mode) in new[] { (hold, adjust, AnimatorConditionMode.If), (adjust, hold, AnimatorConditionMode.IfNot) })
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = 0f;
            t.AddCondition(mode, 0f, C.GazeAdjustParam);
        }
    }
}
