#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Dynamics.Constraint.Components;

/// <summary>Plays the built TranslatorPet.prefab and checks that the pet trails the wearer, then settles beside them.</summary>
public class TranslatorFollowPlayTests
{
    const string PrefabPath = "Assets/natsuki_veer02/Translator/TranslatorPet.prefab";
    GameObject avatar;

    [TearDown]
    public void TearDown() => Object.Destroy(avatar);

    [UnityTest]
    public IEnumerator Pet_LagsBehindThenSettlesAtTheRight()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab, "Build the prefab first: Tools > Translator Pet > Build Prefab");
        avatar = new GameObject("avatar");
        var pet = Object.Instantiate(prefab, avatar.transform);
        var root = pet.transform.Find("Pet_Root");
        var follow = root.Find("Pet_Follow");
        root.gameObject.SetActive(true); // what the toggle's On state does

        for (int i = 0; i < 10; i++) yield return null;
        Assert.AreEqual(0.55f, follow.position.x, 0.01f, "waits at the front right while standing still");

        avatar.transform.position = new Vector3(0, 0, 3); // walk 3 m forward
        yield return null;
        yield return null;
        float lag = 3.55f - follow.position.z;
        Assert.Greater(lag, 2f, "trails behind right after moving");
        Assert.Less(lag, 3f, "but has started to follow");

        for (int i = 0; i < 300; i++) yield return null;
        Assert.AreEqual(3.55f, follow.position.z, 0.02f, "catches up");
        Assert.AreEqual(0.55f, follow.position.x, 0.02f, "and settles at the front right");
    }

    [UnityTest]
    public IEnumerator Pet_TurnsWithTheWearerSlowly()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        avatar = new GameObject("avatar");
        var pet = Object.Instantiate(prefab, avatar.transform);
        var root = pet.transform.Find("Pet_Root");
        var follow = root.Find("Pet_Follow");
        root.gameObject.SetActive(true);
        for (int i = 0; i < 10; i++) yield return null;

        // While walking the motion layer lets the waiting spot turn with the wearer.
        var rot = root.Find("Pet_Heading").GetComponent<VRCRotationConstraint>();
        var sources = rot.Sources; // a struct: edit a copy and assign it back
        var src = sources[1];
        src.Weight = 1000f;
        sources[1] = src;
        rot.Sources = sources;
        avatar.transform.rotation = Quaternion.Euler(0, 90, 0);
        yield return null;
        Assert.Greater(Quaternion.Angle(follow.rotation, avatar.transform.rotation), 45f, "turns late");
        for (int i = 0; i < 300; i++) yield return null;
        Assert.Less(Quaternion.Angle(follow.rotation, avatar.transform.rotation), 1f, "ends facing the same way");
        Assert.Less(Vector3.Distance(follow.position, avatar.transform.TransformPoint(0.55f, 0, 0.55f)), 0.02f, "still at the right");
    }

    [UnityTest]
    public IEnumerator Pet_StaysPutWhenTheWearerOnlyTurns()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        avatar = new GameObject("avatar");
        var pet = Object.Instantiate(prefab, avatar.transform);
        var root = pet.transform.Find("Pet_Root");
        var follow = root.Find("Pet_Follow");
        root.gameObject.SetActive(true);
        for (int i = 0; i < 30; i++) yield return null;
        var before = follow.position;

        avatar.transform.rotation = Quaternion.Euler(0, 120, 0); // turn on the spot, standing still
        for (int i = 0; i < 200; i++) yield return null;
        Assert.Less(Vector3.Distance(before, follow.position), 0.01f, "does not orbit around the wearer");
    }

    [UnityTest]
    public IEnumerator Pet_SnapsOverAfterATeleport_PositionStillFollows()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        avatar = new GameObject("avatar");
        var pet = Object.Instantiate(prefab, avatar.transform);
        var root = pet.transform.Find("Pet_Root");
        var target = root.Find("Pet_Heading/Pet_Target");
        root.gameObject.SetActive(true);
        for (int i = 0; i < 10; i++) yield return null;
        avatar.transform.position = new Vector3(30, 0, 0); // standing still, so only position changes
        yield return null;
        yield return null;
        Assert.AreEqual(30.55f, target.position.x, 0.01f, "the waiting spot moves with the wearer even when standing");
    }

    [Test]
    public void FollowConstraintIsPresent()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab.transform.Find("Pet_Root/Pet_Follow").GetComponent<VRCParentConstraint>());
    }
}
#endif
