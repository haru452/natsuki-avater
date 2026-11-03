using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public class FireAnimationBuilderTests
{
    const string Dir = "Assets/natsuki_veer02/Fire/_test_anim";

    Texture2D sheet;
    Texture2D ember;
    GameObject avatar;
    AnimatorController fx;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        AssetDatabase.DeleteAsset(Dir);
        Directory.CreateDirectory(Dir);
        sheet = FireSpriteGenerator.Generate(Dir);
        ember = FireSpriteGenerator.GenerateEmber(Dir);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        AssetDatabase.DeleteAsset(Dir);
    }

    [SetUp]
    public void SetUp()
    {
        avatar = TestAvatar.Create();
        FireObjectBuilder.Build(avatar, sheet, ember, Dir);
        AssetDatabase.DeleteAsset(Dir + "/fx.controller");
        fx = AnimatorController.CreateAnimatorControllerAtPath(Dir + "/fx.controller");
    }

    [TearDown]
    public void TearDown()
    {
        if (avatar != null) Object.DestroyImmediate(avatar);
    }

    AnimatorControllerLayer FireLayer()
    {
        return fx.layers.Single(l => l.name == FireConfig.LayerName);
    }

    [Test]
    public void Apply_AddsBoolParameter()
    {
        FireAnimationBuilder.Apply(fx, avatar, Dir);
        var p = fx.parameters.Single(x => x.name == FireConfig.ParamName);
        Assert.AreEqual(AnimatorControllerParameterType.Bool, p.type);
    }

    [Test]
    public void Apply_AddsFireLayerWithTwoTransitions()
    {
        FireAnimationBuilder.Apply(fx, avatar, Dir);
        var layer = FireLayer();
        Assert.AreEqual(1f, layer.defaultWeight);
        var states = layer.stateMachine.states.Select(s => s.state).ToList();
        CollectionAssert.AreEquivalent(new[] { "Off", "On" }, states.Select(s => s.name).ToArray());
        var off = states.Single(s => s.name == "Off");
        var on = states.Single(s => s.name == "On");
        Assert.AreSame(off, layer.stateMachine.defaultState);

        var toOn = off.transitions.Single();
        Assert.AreSame(on, toOn.destinationState);
        Assert.IsFalse(toOn.hasExitTime);
        Assert.AreEqual(0f, toOn.duration);
        Assert.AreEqual(AnimatorConditionMode.If, toOn.conditions.Single().mode);
        Assert.AreEqual(FireConfig.ParamName, toOn.conditions.Single().parameter);

        var toOff = on.transitions.Single();
        Assert.AreSame(off, toOff.destinationState);
        Assert.AreEqual(AnimatorConditionMode.IfNot, toOff.conditions.Single().mode);
    }

    [Test]
    public void Apply_OnClipTogglesEveryFireObject()
    {
        FireAnimationBuilder.Apply(fx, avatar, Dir);
        var on = AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "/Fire_On.anim");
        var off = AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "/Fire_Off.anim");
        var expected = FireObjectBuilder.FindAll(avatar).Select(g => FireObjectBuilder.PathOf(avatar, g)).OrderBy(x => x).ToArray();

        var onBindings = AnimationUtility.GetCurveBindings(on);
        Assert.AreEqual(3, onBindings.Length);
        CollectionAssert.AreEqual(expected, onBindings.Select(b => b.path).OrderBy(x => x).ToArray());
        Assert.IsTrue(onBindings.All(b => b.propertyName == "m_IsActive" && b.type == typeof(GameObject)));
        Assert.IsTrue(onBindings.All(b => AnimationUtility.GetEditorCurve(on, b).keys[0].value == 1f));

        var offBindings = AnimationUtility.GetCurveBindings(off);
        Assert.AreEqual(3, offBindings.Length);
        Assert.IsTrue(offBindings.All(b => AnimationUtility.GetEditorCurve(off, b).keys[0].value == 0f));
    }

    [Test]
    public void Apply_IsIdempotent()
    {
        FireAnimationBuilder.Apply(fx, avatar, Dir);
        FireAnimationBuilder.Apply(fx, avatar, Dir);
        Assert.AreEqual(1, fx.layers.Count(l => l.name == FireConfig.LayerName));
        Assert.AreEqual(1, fx.parameters.Count(p => p.name == FireConfig.ParamName));
    }

    [Test]
    public void Apply_MatchesExistingWriteDefaults()
    {
        fx.AddLayer("Other");
        var other = fx.layers.Single(l => l.name == "Other");
        var existing = other.stateMachine.AddState("Existing");
        existing.writeDefaultValues = false;

        FireAnimationBuilder.Apply(fx, avatar, Dir);

        var states = FireLayer().stateMachine.states.Select(s => s.state).ToList();
        Assert.AreEqual(2, states.Count);
        Assert.IsTrue(states.All(s => s.writeDefaultValues == false));
    }
}
