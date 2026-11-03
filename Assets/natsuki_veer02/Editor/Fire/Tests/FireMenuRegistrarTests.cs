using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

public class FireMenuRegistrarTests
{
    VRCExpressionParameters parameters;
    VRCExpressionsMenu menu;

    [SetUp]
    public void SetUp()
    {
        parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
        parameters.parameters = new VRCExpressionParameters.Parameter[0];
        menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
        menu.controls = new List<VRCExpressionsMenu.Control>();
    }

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Object.DestroyImmediate(parameters);
        UnityEngine.Object.DestroyImmediate(menu);
    }

    [Test]
    public void Register_AddsSyncedBoolParameter()
    {
        FireMenuRegistrar.Register(parameters, menu);
        var p = parameters.parameters.Single(x => x.name == FireConfig.ParamName);
        Assert.AreEqual(VRCExpressionParameters.ValueType.Bool, p.valueType);
        Assert.IsTrue(p.networkSynced);
    }

    [Test]
    public void Register_AddsToggleControl()
    {
        FireMenuRegistrar.Register(parameters, menu);
        var c = menu.controls.Single();
        Assert.AreEqual(FireConfig.MenuLabel, c.name);
        Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.Toggle, c.type);
        Assert.AreEqual(FireConfig.ParamName, c.parameter.name);
    }

    [Test]
    public void Register_IsIdempotent()
    {
        FireMenuRegistrar.Register(parameters, menu);
        FireMenuRegistrar.Register(parameters, menu);
        Assert.AreEqual(1, parameters.parameters.Count(x => x.name == FireConfig.ParamName));
        Assert.AreEqual(1, menu.controls.Count(c => c.name == FireConfig.MenuLabel));
    }

    [Test]
    public void Register_ThrowsAndWritesNothing_WhenParametersFull()
    {
        // 32 Int parameters cost 8 bits each = exactly the 256-bit limit.
        parameters.parameters = Enumerable.Range(0, 32)
            .Select(i => new VRCExpressionParameters.Parameter { name = "Dummy" + i, valueType = VRCExpressionParameters.ValueType.Int })
            .ToArray();
        Assert.AreEqual(VRCExpressionParameters.MAX_PARAMETER_COST, parameters.CalcTotalCost());

        Assert.Throws<InvalidOperationException>(() => FireMenuRegistrar.Register(parameters, menu));

        Assert.AreEqual(32, parameters.parameters.Length);
        Assert.AreEqual(0, menu.controls.Count);
    }

    [Test]
    public void Register_ThrowsAndWritesNothing_WhenMenuFull()
    {
        for (int i = 0; i < VRCExpressionsMenu.MAX_CONTROLS; i++)
            menu.controls.Add(new VRCExpressionsMenu.Control { name = "Dummy" + i, type = VRCExpressionsMenu.Control.ControlType.Button });

        Assert.Throws<InvalidOperationException>(() => FireMenuRegistrar.Register(parameters, menu));

        Assert.AreEqual(0, parameters.parameters.Length);
        Assert.AreEqual(VRCExpressionsMenu.MAX_CONTROLS, menu.controls.Count);
    }
}
