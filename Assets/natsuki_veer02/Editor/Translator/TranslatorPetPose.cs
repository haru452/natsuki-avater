using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using C = TranslatorConfig;
using M = TranslatorMotionBuilder;

/// <summary>
/// 向き合わせ (PoseParam): while the toggle is on, her body faces the way the wearer's body faces; turned off, she
/// keeps that direction until she sets off after the wearer. Looks and calls work as ever, from that direction.
/// Pet_PoseDir (on Pet_Root, kept at her by a position constraint) turns like Pet_Heading: a source on itself keeps its
/// own world direction, and the wearer's body (source1) is weighted in only while following. Pet_PoseAhead, 1 m
/// ahead of it, is Pet_Hop's third aim source, which the Pose layer weights in over the Body layer's two.
/// </summary>
public static class TranslatorPetPose
{
    public const string LayerName = "Translator_Pose";
    public const string DirName = "Pet_PoseDir";
    public const string AheadName = "Pet_PoseAhead";
    /// <summary>Pet_Hop's aim source for the kept direction (after the spot, 0, and the point ahead, 1).</summary>
    public const int AheadSource = 2;

    /// <summary>pet: Pet_Root; wearer: the prefab root (turns with the wearer's body); hop: Pet_Hop, its aim already built.</summary>
    public static void Add(Transform pet, Transform wearer, Transform hop)
    {
        var dir = new GameObject(DirName).transform;
        dir.SetParent(pet, false);
        var pos = dir.gameObject.AddComponent<VRCPositionConstraint>();
        pos.Sources.Add(new VRCConstraintSource(hop, 1f));
        pos.IsActive = true;
        pos.Locked = true;
        var rot = dir.gameObject.AddComponent<VRCRotationConstraint>();
        rot.Sources.Add(new VRCConstraintSource(dir, 1f));
        rot.Sources.Add(new VRCConstraintSource(wearer, 0f));
        rot.AffectsRotationX = false; // stay upright
        rot.AffectsRotationZ = false;
        rot.IsActive = true;
        rot.Locked = true;

        var ahead = new GameObject(AheadName).transform;
        ahead.SetParent(dir, false);
        ahead.localPosition = Vector3.forward;

        var aim = hop.GetComponent<VRCAimConstraint>();
        Debug.Assert(aim.Sources.Count == AheadSource, "Pet_Hop's aim: the spot and the point ahead come first");
        aim.Sources.Add(new VRCConstraintSource(ahead, 0f));
    }

    /// <summary>
    /// Free (default: the Body layer turns her) → Follow (toggle on: she faces the wearer's way) ⇄ Keep (toggle off: that
    /// way, kept); setting off after the wearer, back to Free. After the Body layer, so its weights win while posed.
    /// </summary>
    public static void AddLayer(AnimatorController controller, AnimatorStateMachine sm, string outputDir)
    {
        foreach (var p in new[] { C.PoseParam, C.CloseParam, C.WaitParam })
            TranslatorPetCommands.EnsureBool(controller, p);
        AnimatorState State(string name, bool follow, bool posed, float y)
        {
            var clip = new AnimationClip();
            void Set(string path, System.Type type, string property, float value) =>
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property),
                    AnimationCurve.Constant(0f, 1f / 60f, value));
            Set(DirName, typeof(VRCRotationConstraint), "Sources.source1.Weight", follow ? C.MovingHeadingWeight : 0f);
            Set(M.HopPath, typeof(VRCAimConstraint), "Sources.source" + AheadSource + ".Weight", posed ? 1f : 0f);
            if (posed) // the Body layer's spot and point ahead give way
                foreach (int source in new[] { 0, 1 })
                    Set(M.HopPath, typeof(VRCAimConstraint), "Sources.source" + source + ".Weight", 0f);
            var s = sm.AddState(name, new Vector3(300, y));
            s.motion = TranslatorAnimationBuilder.SaveClip(clip, outputDir + "/Translator_Pose" + name + ".anim");
            s.writeDefaultValues = false;
            return s;
        }
        var free = State("Free", follow: false, posed: false, 100);
        var follow = State("Follow", follow: true, posed: true, 200);
        var keep = State("Keep", follow: false, posed: true, 300);
        sm.defaultState = free;

        AnimatorStateTransition Go(AnimatorState from, AnimatorState to)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = C.LookTurnSeconds;
            return t;
        }
        var start = Go(free, follow);
        start.AddCondition(AnimatorConditionMode.If, 0f, C.PoseParam);
        start.AddCondition(AnimatorConditionMode.Less, C.HopStopSpeed, C.VelocityParam); // not while walking after the wearer
        Go(keep, follow).AddCondition(AnimatorConditionMode.If, 0f, C.PoseParam);
        Go(follow, keep).AddCondition(AnimatorConditionMode.IfNot, 0f, C.PoseParam);
        foreach (var from in new[] { follow, keep })
        {
            var leave = Go(from, free); // setting off after the wearer, as for こっち向いて
            leave.AddCondition(AnimatorConditionMode.IfNot, 0f, C.CloseParam);
            leave.AddCondition(AnimatorConditionMode.Greater, C.HopStartSpeed, C.VelocityParam);
            leave.AddCondition(AnimatorConditionMode.IfNot, 0f, C.WaitParam);
        }
    }
}
