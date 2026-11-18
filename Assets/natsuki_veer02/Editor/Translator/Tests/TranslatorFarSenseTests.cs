using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;

public class TranslatorFarSenseTests
{
    /// <summary>
    /// How far `param`'s far sense on `at` reaches (and what it measures to), checking it works inside VRChat's
    /// limits: every contact at most 3 m, the probe's sender alone on the receiver's tag.
    /// </summary>
    public static float Reach(Transform at, string param, out Transform far)
    {
        var r = at.GetComponents<VRCContactReceiver>().Single(x => x.parameter == param);
        Assert.LessOrEqual(r.radius, TranslatorFarSense.MaxContactRadius, "VRChat clamps contacts to 3 m");
        Assert.AreEqual(ContactReceiver.ReceiverType.Constant, r.receiverType);
        Assert.IsTrue(r.allowSelf);
        Assert.IsFalse(r.allowOthers, "other players cannot set it off");
        Assert.IsFalse(r.localOnly, "every client moves its own copy of the pet");
        var probe = at.Find(TranslatorFarSense.ProbeName(param));
        var s = probe.GetComponent<VRCContactSender>();
        CollectionAssert.AreEqual(r.collisionTags, s.collisionTags);
        Assert.AreEqual(1, probe.root.GetComponentsInChildren<ContactBase>(true).Count(c => c.collisionTags.SequenceEqual(r.collisionTags)) - 1,
            "the probe's own tag: nothing else sets it off");
        var pos = probe.GetComponent<VRCPositionConstraint>();
        Assert.IsTrue(pos.IsActive && pos.Locked);
        Assert.AreEqual(Vector3.zero, pos.PositionOffset);
        Assert.AreEqual(at, pos.Sources[0].SourceTransform);
        far = pos.Sources[1].SourceTransform;
        float k = pos.Sources[1].Weight;
        Assert.AreEqual(1f, pos.Sources[0].Weight + k, 1e-5f, "weights add up to one: the probe is k of the way");
        return (r.radius + s.radius) / k;
    }

    [Test]
    public void ReachesTheDistance_WithSmallContacts()
    {
        var root = new GameObject("root");
        try
        {
            var at = new GameObject("at");
            at.transform.SetParent(root.transform);
            var far = new GameObject("far");
            far.transform.SetParent(root.transform);
            TranslatorFarSense.Add(at, far.transform, 20f, "Tag", "Param");
            Assert.AreEqual(20f, Reach(at.transform, "Param", out var to), 1e-3f);
            Assert.AreEqual(far.transform, to);
        }
        finally { Object.DestroyImmediate(root); }
    }
}
