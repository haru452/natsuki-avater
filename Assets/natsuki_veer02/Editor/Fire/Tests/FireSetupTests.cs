using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

public class FireSetupTests
{
    const string Dir = "Assets/natsuki_veer02/Fire/_test_setup";
    const string Out = Dir + "/out";
    const string Backup = Dir + "/backup";

    GameObject avatar;
    AnimatorController fx;
    VRCExpressionParameters parameters;
    VRCExpressionsMenu menu;

    [SetUp]
    public void SetUp()
    {
        AssetDatabase.DeleteAsset(Dir);
        Directory.CreateDirectory(Dir);
        avatar = TestAvatar.Create();
        fx = AnimatorController.CreateAnimatorControllerAtPath(Dir + "/fx.controller");
        parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
        parameters.parameters = new VRCExpressionParameters.Parameter[0];
        AssetDatabase.CreateAsset(parameters, Dir + "/params.asset");
        menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
        menu.controls = new List<VRCExpressionsMenu.Control>();
        AssetDatabase.CreateAsset(menu, Dir + "/menu.asset");
    }

    [TearDown]
    public void TearDown()
    {
        if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
        AssetDatabase.DeleteAsset(Dir);
    }

    void Run()
    {
        FireSetup.Run(avatar, fx, parameters, menu, Out, Backup);
    }

    [Test]
    public void Run_CreatesAllPieces()
    {
        Run();
        Assert.IsTrue(parameters.parameters.Any(p => p.name == FireConfig.ParamName));
        Assert.IsTrue(menu.controls.Any(c => c.name == FireConfig.MenuLabel));
        Assert.AreEqual(1, fx.layers.Count(l => l.name == FireConfig.LayerName));
        Assert.AreEqual(3, FireObjectBuilder.FindAll(avatar).Count);
    }

    [Test]
    public void Run_CreatesBackups()
    {
        Run();
        Assert.IsTrue(File.Exists(Backup + "/fx.controller"));
        Assert.IsTrue(File.Exists(Backup + "/params.asset"));
        Assert.IsTrue(File.Exists(Backup + "/menu.asset"));
    }

    [Test]
    public void Run_AbortsBeforeTouchingAnything_WhenParametersFull()
    {
        parameters.parameters = Enumerable.Range(0, 32)
            .Select(i => new VRCExpressionParameters.Parameter { name = "Dummy" + i, valueType = VRCExpressionParameters.ValueType.Int })
            .ToArray();

        Assert.Throws<InvalidOperationException>(() => Run());

        Assert.AreEqual(0, FireObjectBuilder.FindAll(avatar).Count);
        Assert.AreEqual(0, fx.layers.Count(l => l.name == FireConfig.LayerName));
        Assert.AreEqual(0, menu.controls.Count);
    }

    [Test]
    public void Run_Twice_ProducesSameResult()
    {
        Run();
        Run();
        Assert.AreEqual(1, parameters.parameters.Count(p => p.name == FireConfig.ParamName));
        Assert.AreEqual(1, menu.controls.Count(c => c.name == FireConfig.MenuLabel));
        Assert.AreEqual(1, fx.layers.Count(l => l.name == FireConfig.LayerName));
        Assert.AreEqual(3, FireObjectBuilder.FindAll(avatar).Count);
    }
}
