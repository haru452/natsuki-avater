using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using C = TranslatorConfig;

/// <summary>
/// Spoken commands the PC app sends after her name: Sit (座って) and Wait (待ってて), both synced Bools the app sets.
/// The Body layer sits her down and holds her in place (TranslatorPetModel); this adds the 20 m limit: once the
/// wearer is WaitRange away, the waiting (and sitting) ends and she comes back (the catch-up snaps her over).
/// </summary>
public static class TranslatorPetCommands
{
    public const string WaitLayerName = "Translator_Wait";
    public const string TurnLayerName = "Translator_Turn";
    /// <summary>The facing constraint's turn on top of where it aims (Pet_Hop's VRCAimConstraint).</summary>
    public const string TurnProperty = "RotationOffset.y";

    /// <summary>
    /// "In range": on while the wearer (wearer: the prefab root, which goes where the avatar goes) is within WaitRange
    /// of her (TranslatorFarSense: a 20 m contact would work as 3 m in VRChat).
    /// </summary>
    public static void AddInRange(GameObject follow, Transform wearer) =>
        TranslatorFarSense.Add(follow, wearer, C.WaitRange, C.InRangeContactTag, C.InRangeParam);

    /// <summary>
    /// Free → Nod (yes!) → Waiting on Wait, back to Free when it goes off; Waiting → TooFar when out of range, which
    /// clears Wait and Sit (the wearer's client). model: the pet model, for her head's nod.
    /// </summary>
    public static void AddWaitLayer(AnimatorController controller, AnimatorStateMachine sm, GameObject model, string outputDir)
    {
        foreach (var p in new[] { C.SitParam, C.WaitParam, C.InRangeParam }) EnsureBool(controller, p);
        var free = sm.AddState("Free", new Vector3(300, 100));
        var nod = sm.AddState("Nod", new Vector3(550, 150));
        var waiting = sm.AddState("Waiting", new Vector3(300, 200));
        var tooFar = sm.AddState("TooFar", new Vector3(300, 300));
        foreach (var s in new[] { free, nod, waiting, tooFar }) s.writeDefaultValues = false;
        sm.defaultState = free;
        TranslatorAnimationBuilder.AddDriver(tooFar, (C.WaitParam, 0f), (C.SitParam, 0f));
        nod.motion = TranslatorAnimationBuilder.SaveClip(NodClip(model), outputDir + "/Translator_PetNod.anim");
        // An empty clip, not none: a motionless state in this Write Defaults off layer froze the whole avatar once she
        // had nodded (stuck smiling; the ペット toggle could no longer hide her).
        var empty = TranslatorAnimationBuilder.SaveClip(new AnimationClip(), outputDir + "/Translator_Empty.anim");
        foreach (var s in new[] { free, waiting, tooFar }) s.motion = empty;
        Go(free, nod, AnimatorConditionMode.If, C.WaitParam);
        var nodded = nod.AddTransition(waiting);
        nodded.hasExitTime = true;
        nodded.exitTime = 1f;
        nodded.duration = 0f;
        Go(waiting, free, AnimatorConditionMode.IfNot, C.WaitParam);
        Go(waiting, tooFar, AnimatorConditionMode.IfNot, C.InRangeParam);
        Go(tooFar, free, AnimatorConditionMode.IfNot, C.WaitParam);
    }

    /// <summary>
    /// Turn (後ろ向いて): Front ⇄ Back on Turn, turning 180° over TurnSeconds (Pet_Hop's facing gets a half-turn on top).
    /// Clear: she sets off after the wearer (not told to wait), so she faces front again and clears Turn.
    /// </summary>
    public static void AddTurnLayer(AnimatorController controller, AnimatorStateMachine sm, string outputDir)
    {
        foreach (var p in new[] { C.TurnParam, C.WaitParam, C.CloseParam }) EnsureBool(controller, p);
        AnimatorState State(string name, float yaw, float y)
        {
            var clip = new AnimationClip();
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(TranslatorMotionBuilder.HopPath,
                typeof(VRCAimConstraint), TurnProperty), AnimationCurve.Constant(0f, 1f / 60f, yaw));
            var s = sm.AddState(name, new Vector3(300, y));
            s.motion = TranslatorAnimationBuilder.SaveClip(clip, outputDir + "/Translator_PetTurn" + name + ".anim");
            s.writeDefaultValues = false;
            return s;
        }
        var front = State("Front", 0f, 100);
        var back = State("Back", 180f, 200);
        var clear = State("Clear", 0f, 300);
        sm.defaultState = front;
        TranslatorAnimationBuilder.AddDriver(clear, (C.TurnParam, 0f));
        Go(front, back, AnimatorConditionMode.If, C.TurnParam).duration = C.TurnSeconds;
        Go(back, front, AnimatorConditionMode.IfNot, C.TurnParam).duration = C.TurnSeconds;
        var leave = Go(back, clear, AnimatorConditionMode.IfNot, C.CloseParam);
        leave.duration = C.TurnSeconds * 0.5f;
        leave.AddCondition(AnimatorConditionMode.Greater, C.HopStartSpeed, C.VelocityParam);
        leave.AddCondition(AnimatorConditionMode.IfNot, 0f, C.WaitParam);
        Go(clear, front, AnimatorConditionMode.IfNot, C.TurnParam);
    }

    /// <summary>
    /// こっち向いて (FaceParam): from Away, Look or Called she turns to the wearer and holds it (Facing, the Look clip)
    /// after the smile is over. Told otherwise (FaceParam off) she looks away; setting off after the wearer, FaceEnd
    /// clears it on her wearer's client (a driver; the sync tells the rest).
    /// </summary>
    public static void AddFacing(AnimatorController controller, AnimatorStateMachine look)
    {
        foreach (var p in new[] { C.FaceParam, C.CloseParam, C.WaitParam }) EnsureBool(controller, p);
        var states = look.states.Select(s => s.state).ToArray();
        var away = states.Single(s => s.name == "Away");
        var looking = states.Single(s => s.name == "Look");
        var facing = look.AddState("Facing", new Vector3(550, 350));
        var end = look.AddState("FaceEnd", new Vector3(300, 350));
        foreach (var s in new[] { facing, end })
        {
            s.motion = looking.motion; // never motionless: an empty state froze the avatar
            s.writeDefaultValues = false;
        }
        TranslatorAnimationBuilder.AddDriver(end, (C.FaceParam, 0f));
        foreach (var from in states.Where(s => s.name is "Away" or "Look" or "Called"))
            Go(from, facing, AnimatorConditionMode.If, C.FaceParam).duration = C.LookTurnSeconds;
        Go(facing, away, AnimatorConditionMode.IfNot, C.FaceParam).duration = C.LookTurnSeconds;
        var leave = Go(facing, end, AnimatorConditionMode.IfNot, C.CloseParam);
        leave.AddCondition(AnimatorConditionMode.Greater, C.HopStartSpeed, C.VelocityParam);
        leave.AddCondition(AnimatorConditionMode.IfNot, 0f, C.WaitParam);
        Go(end, away, AnimatorConditionMode.IfNot, C.FaceParam).duration = C.LookTurnSeconds;
    }

    /// <summary>Turned round, she does not turn back to face whoever looks or calls (Look layer: Away, Look, Called, Facing).</summary>
    public static void NoLookWhileTurned(AnimatorStateMachine look)
    {
        var states = look.states.Select(s => s.state).ToArray();
        foreach (var t in states.SelectMany(s => s.transitions).Where(t => t.destinationState.name is "Look" or "Called" or "Facing"))
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, C.TurnParam);
        var away = states.Single(s => s.name == "Away");
        foreach (var from in states.Where(s => s.name is "Look" or "Called" or "Facing"))
        {
            var t = from.AddTransition(away);
            t.hasExitTime = false;
            t.duration = C.LookTurnSeconds;
            t.AddCondition(AnimatorConditionMode.If, 0f, C.TurnParam);
        }
    }

    /// <summary>Her head dips NodDegrees from its rest pose and comes back up (over the Body layer's head).</summary>
    static AnimationClip NodClip(GameObject model)
    {
        var rest = TranslatorPetModel.Head(model.transform).localRotation;
        var keys = new[] { (0f, 0f), (C.NodSeconds * 0.4f, C.NodDegrees), (C.NodSeconds, 0f) };
        var curves = new AnimationCurve[4];
        for (int i = 0; i < 4; i++) curves[i] = new AnimationCurve();
        foreach (var (t, deg) in keys)
        {
            var q = rest * Quaternion.Euler(deg, 0f, 0f);
            for (int i = 0; i < 4; i++) curves[i].AddKey(new Keyframe(t, q[i], 0f, 0f));
        }
        var clip = new AnimationClip();
        string head = TranslatorPetModel.HeadPath(model);
        for (int i = 0; i < 4; i++)
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(head, typeof(Transform), "m_LocalRotation." + "xyzw"[i]), curves[i]);
        // Called by name she is looking at the wearer: her head's aim constraint overrides the animation, so the nod
        // goes on its offset too (pitch about the head's own side axis).
        var pitch = new AnimationCurve(keys.Select(k => new Keyframe(k.Item1, k.Item2, 0f, 0f)).ToArray());
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(head, typeof(VRCAimConstraint), "RotationOffset.x"), pitch);
        return clip;
    }

    public static void EnsureBool(AnimatorController controller, string name)
    {
        if (!controller.parameters.Any(p => p.name == name)) controller.AddParameter(name, AnimatorControllerParameterType.Bool);
    }

    static AnimatorStateTransition Go(AnimatorState from, AnimatorState to, AnimatorConditionMode mode, string param)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = false;
        t.duration = 0f;
        t.AddCondition(mode, 0f, param);
        return t;
    }
}
