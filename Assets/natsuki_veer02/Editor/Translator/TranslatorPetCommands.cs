using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
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

    public static void EnsureBool(AnimatorController controller, string name)
    {
        if (!controller.parameters.Any(p => p.name == name)) controller.AddParameter(name, AnimatorControllerParameterType.Bool);
    }

    static void Go(AnimatorState from, AnimatorState to, AnimatorConditionMode mode, string param)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = false;
        t.duration = 0f;
        t.AddCondition(mode, 0f, param);
    }
}
