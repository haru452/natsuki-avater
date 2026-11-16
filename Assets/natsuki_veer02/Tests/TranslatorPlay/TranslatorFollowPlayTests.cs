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
        pet.transform.Find("Pet_Target").gameObject.SetActive(true);

        for (int i = 0; i < 10; i++) yield return null;
        Assert.AreEqual(0.5f, follow.position.x, 0.01f, "waits at the right while standing still");

        avatar.transform.position = new Vector3(0, 0, 3); // walk 3 m forward
        yield return null;
        yield return null;
        float lag = 3f - follow.position.z;
        Assert.Greater(lag, 2f, "trails behind right after moving");
        Assert.Less(lag, 3f, "but has started to follow");

        for (int i = 0; i < 300; i++) yield return null;
        Assert.AreEqual(3f, follow.position.z, 0.02f, "catches up");
        Assert.AreEqual(0.5f, follow.position.x, 0.02f, "and settles at the right");
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
        pet.transform.Find("Pet_Target").gameObject.SetActive(true);
        for (int i = 0; i < 10; i++) yield return null;

        avatar.transform.rotation = Quaternion.Euler(0, 90, 0);
        yield return null;
        Assert.Greater(Quaternion.Angle(follow.rotation, avatar.transform.rotation), 45f, "turns late");
        for (int i = 0; i < 300; i++) yield return null;
        Assert.Less(Quaternion.Angle(follow.rotation, avatar.transform.rotation), 1f, "ends facing the same way");
        Assert.Less(Vector3.Distance(follow.position, avatar.transform.TransformPoint(0.5f, 0, 0)), 0.02f, "still at the right");
    }

    [Test]
    public void FollowConstraintIsPresent()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab.transform.Find("Pet_Root/Pet_Follow").GetComponent<VRCParentConstraint>());
    }
}
#endif
