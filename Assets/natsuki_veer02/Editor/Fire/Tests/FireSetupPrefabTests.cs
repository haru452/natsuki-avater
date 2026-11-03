using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

public class FireSetupPrefabTests
{
    const string Dir = "Assets/natsuki_veer02/Fire/_test_setup_prefab";
    const string Out = Dir + "/out";
    const string Backup = Dir + "/backup";
    const string PrefabPath = Dir + "/avatar.prefab";

    AnimatorController fx;
    VRCExpressionParameters parameters;
    VRCExpressionsMenu menu;

    [SetUp]
    public void SetUp()
    {
        AssetDatabase.DeleteAsset(Dir);
        Directory.CreateDirectory(Dir);
        fx = AnimatorController.CreateAnimatorControllerAtPath(Dir + "/fx.controller");
        parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
        parameters.parameters = new VRCExpressionParameters.Parameter[0];
        AssetDatabase.CreateAsset(parameters, Dir + "/params.asset");
        menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
        menu.controls = new List<VRCExpressionsMenu.Control>();
        AssetDatabase.CreateAsset(menu, Dir + "/menu.asset");

        var avatar = TestAvatar.Create();
        // The humanoid Avatar must be an asset or the prefab loses it when saved.
        AssetDatabase.CreateAsset(avatar.GetComponent<Animator>().avatar, Dir + "/humanoid.asset");
        // Same for the skinned meshes the flames emit from.
        foreach (var smr in avatar.GetComponentsInChildren<SkinnedMeshRenderer>())
            AssetDatabase.CreateAsset(smr.sharedMesh, Dir + "/" + smr.name + "_mesh.asset");
        var d =avatar.AddComponent<VRCAvatarDescriptor>();
        d.customizeAnimationLayers = true;
        d.baseAnimationLayers = new[]
        {
            new VRCAvatarDescriptor.CustomAnimLayer
            {
                type = VRCAvatarDescriptor.AnimLayerType.FX,
                animatorController = fx,
                isDefault = false,
                isEnabled = true,
            },
        };
        d.customExpressions = true;
        d.expressionParameters = parameters;
        d.expressionsMenu = menu;
        PrefabUtility.SaveAsPrefabAsset(avatar, PrefabPath);
        UnityEngine.Object.DestroyImmediate(avatar);
    }

    [TearDown]
    public void TearDown()
    {
        AssetDatabase.DeleteAsset(Dir);
    }

    [Test]
    public void SetupPrefab_BacksUpPrefabAndBuildsSites()
    {
        FireSetup.SetupPrefab(PrefabPath, Out, Backup);

        Assert.IsTrue(File.Exists(Backup + "/avatar.prefab"), "prefab must be backed up before it is overwritten");
        Assert.IsTrue(File.Exists(Backup + "/fx.controller"));
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try { Assert.AreEqual(3, FireObjectBuilder.FindAll(root).Count); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    [Test]
    public void Run_NonHumanoidAvatar_AbortsBeforeRegistration()
    {
        var plain = new GameObject("Plain");
        try
        {
            plain.AddComponent<Animator>();
            Assert.Throws<InvalidOperationException>(() => FireSetup.Run(plain, fx, parameters, menu, Out, Backup));
            Assert.AreEqual(0, parameters.parameters.Length, "parameter must not be registered when the avatar is unusable");
            Assert.AreEqual(0, menu.controls.Count, "menu item must not be added when the avatar is unusable");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(plain);
        }
    }
}
