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

    /// <summary>"In range": a receiver on the pet, WaitRange wide, for the wearer's sender (TranslatorPetWalker.AddClose).</summary>
    public static void AddInRange(GameObject follow)
    {
        var r = follow.AddComponent<VRCContactReceiver>();
        r.shapeType = ContactBase.ShapeType.Sphere;
        r.radius = C.WaitRange;
        r.collisionTags = new List<string> { C.WearerContactTag };
        r.allowSelf = true;
        r.allowOthers = false;
        r.localOnly = false;
        r.receiverType = ContactReceiver.ReceiverType.Constant;
        r.parameter = C.InRangeParam;
    }

    /// <summary>Free ⇄ Waiting on Wait; Waiting → TooFar when out of range, which clears Wait and Sit (the wearer's client).</summary>
    public static void AddWaitLayer(AnimatorController controller, AnimatorStateMachine sm)
    {
        foreach (var p in new[] { C.SitParam, C.WaitParam, C.InRangeParam }) EnsureBool(controller, p);
        var free = sm.AddState("Free", new Vector3(300, 100));
        var waiting = sm.AddState("Waiting", new Vector3(300, 200));
        var tooFar = sm.AddState("TooFar", new Vector3(300, 300));
        foreach (var s in new[] { free, waiting, tooFar }) s.writeDefaultValues = false;
        sm.defaultState = free;
        TranslatorAnimationBuilder.AddDriver(tooFar, (C.WaitParam, 0f), (C.SitParam, 0f));
        Go(free, waiting, AnimatorConditionMode.If, C.WaitParam);
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

    /// <summary>Turned round, she does not turn back to face whoever looks or calls (Look layer: Away, Look, Called).</summary>
    public static void NoLookWhileTurned(AnimatorStateMachine look)
    {
        var states = look.states.Select(s => s.state).ToArray();
        foreach (var t in states.SelectMany(s => s.transitions).Where(t => t.destinationState.name is "Look" or "Called"))
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, C.TurnParam);
        var away = states.Single(s => s.name == "Away");
        foreach (var from in states.Where(s => s.name is "Look" or "Called"))
        {
            var t = from.AddTransition(away);
            t.hasExitTime = false;
            t.duration = C.LookTurnSeconds;
            t.AddCondition(AnimatorConditionMode.If, 0f, C.TurnParam);
        }
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
