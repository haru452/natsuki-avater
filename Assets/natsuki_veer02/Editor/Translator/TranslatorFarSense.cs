using System.Collections.Generic;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;

/// <summary>
/// "Within N metres" for distances VRChat contacts cannot reach: VRChat clamps a contact's radius to 3 m (a 20 m
/// receiver works as 3 m in game). A probe is held part of the way from one object toward the other (a position
/// constraint, Ratio of the way), and a receiver on the first, ProbeRadius wide, feels the probe's sender: the probe is
/// within ProbeRadius exactly while the two are within the distance.
/// </summary>
public static class TranslatorFarSense
{
    /// <summary>VRChat's largest contact radius (applied after scaling).</summary>
    public const float MaxContactRadius = 3f;
    /// <summary>The receiver's radius: inside the limit, with room for a scaled avatar.</summary>
    public const float ProbeRadius = 1f;
    public const float SenderRadius = 0.01f;

    public static string ProbeName(string param) => param + "_Probe";

    /// <summary>The probe's share of the way toward the far object.</summary>
    public static float Ratio(float distance) => (ProbeRadius + SenderRadius) / distance;

    /// <summary>param (Constant) is on while `far` is within `distance` of `at`. `tag` must be this sense's own.</summary>
    public static VRCContactReceiver Add(GameObject at, Transform far, float distance, string tag, string param)
    {
        var r = at.AddComponent<VRCContactReceiver>();
        r.shapeType = ContactBase.ShapeType.Sphere;
        r.radius = ProbeRadius;
        r.collisionTags = new List<string> { tag };
        r.allowSelf = true;
        r.allowOthers = false;
        r.localOnly = false;
        r.receiverType = ContactReceiver.ReceiverType.Constant;
        r.parameter = param;

        var probe = new GameObject(ProbeName(param));
        probe.transform.SetParent(at.transform, false);
        float k = Ratio(distance);
        var pos = probe.AddComponent<VRCPositionConstraint>();
        pos.Sources.Add(new VRCConstraintSource(at.transform, 1f - k));
        pos.Sources.Add(new VRCConstraintSource(far, k));
        pos.IsActive = true;
        pos.Locked = true;

        var s = probe.AddComponent<VRCContactSender>();
        s.shapeType = ContactBase.ShapeType.Sphere;
        s.radius = SenderRadius;
        s.collisionTags = new List<string> { tag };
        return r;
    }
}
