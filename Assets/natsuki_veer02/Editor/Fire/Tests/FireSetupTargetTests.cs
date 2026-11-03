using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class FireSetupTargetTests
{
    const string Dir = "Assets/natsuki_veer02/Fire/_test_target";
    const string AssetPath = Dir + "/reko_test.prefab";

    GameObject prefab;
    GameObject instance;

    [SetUp]
    public void SetUp()
    {
        AssetDatabase.DeleteAsset(Dir);
        Directory.CreateDirectory(Dir);
        var go = new GameObject("reko_test");
        go.AddComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
        new GameObject("Child").transform.SetParent(go.transform, false);
        prefab = PrefabUtility.SaveAsPrefabAsset(go, AssetPath);
        Object.DestroyImmediate(go);
    }

    [TearDown]
    public void TearDown()
    {
        if (instance != null) Object.DestroyImmediate(instance);
        AssetDatabase.DeleteAsset(Dir);
    }

    [Test]
    public void ResolvePrefabPath_PrefabAsset_ReturnsItsPath()
    {
        Assert.AreEqual(AssetPath, FireSetup.ResolvePrefabPath(prefab));
    }

    [Test]
    public void ResolvePrefabPath_SceneInstanceChild_ReturnsSourcePrefabPath()
    {
        instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var child = instance.transform.Find("Child").gameObject;
        Assert.AreEqual(AssetPath, FireSetup.ResolvePrefabPath(child));
    }

    [Test]
    public void ResolvePrefabPath_NothingSelected_UsesTheOnlyAvatarInScene()
    {
        instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Assert.AreEqual(AssetPath, FireSetup.ResolvePrefabPath(null));
    }

    [Test]
    public void ResolvePrefabPath_NothingSelectedAndNoAvatarInScene_ReturnsNull()
    {
        Assert.IsNull(FireSetup.ResolvePrefabPath(null));
    }

    [Test]
    public void ResolvePrefabPath_NonPrefabSelection_FallsBackToSceneAvatar()
    {
        instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var loose = new GameObject("Loose");
        try { Assert.AreEqual(AssetPath, FireSetup.ResolvePrefabPath(loose)); }
        finally { Object.DestroyImmediate(loose); }
    }

    [Test]
    public void OutputDirFor_SeparatesAvatarsByPrefabName()
    {
        Assert.AreEqual(FireConfig.OutputDir + "/reko", FireSetup.OutputDirFor("Assets/reko/Prefab/reko.prefab"));
    }
}
