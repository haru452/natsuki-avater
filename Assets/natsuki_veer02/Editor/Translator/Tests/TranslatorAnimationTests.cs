using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using C = TranslatorConfig;

public class TranslatorAnimationTests
{
    const string Dir = TranslatorAssetTests.Dir;

    [SetUp]
    public void SetUp()
    {
        AssetDatabase.DeleteAsset(Dir);
        AssetDatabase.CreateFolder("Assets", "__TranslatorTest");
    }

    [TearDown]
    public void TearDown() => AssetDatabase.DeleteAsset(Dir);

    static IEnumerable<AnimatorState> States(AnimatorController c) =>
        c.layers.SelectMany(l => l.stateMachine.states).Select(s => s.state);

    static AnimatorState State(AnimatorController c, string name) => States(c).Single(s => s.name == name);

    static float ActiveValue(AnimatorState state, string path)
    {
        var clip = (AnimationClip)state.motion;
        var binding = EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive");
        return AnimationUtility.GetEditorCurve(clip, binding).keys[0].value;
    }

    [Test]
    public void Toggle_HasOffAndOnStates_WriteDefaultsOff()
    {
        var c = TranslatorAnimationBuilder.BuildToggleController(Dir);
        Assert.AreEqual(1, c.layers.Length);
        Assert.AreEqual("Off", c.layers[0].stateMachine.defaultState.name);
        CollectionAssert.AreEquivalent(new[] { "Off", "Appear", "On", "CatchUp" }, States(c).Select(s => s.name));
        Assert.IsTrue(States(c).All(s => !s.writeDefaultValues));
    }

    [Test]
    public void Toggle_ClipsDriveBothRoots()
    {
        var c = TranslatorAnimationBuilder.BuildToggleController(Dir);
        foreach (var path in new[] { C.PetRootName, C.SignRootName, C.PetTargetName })
        {
            Assert.AreEqual(1f, ActiveValue(State(c, "On"), path), path);
            Assert.AreEqual(1f, ActiveValue(State(c, "Appear"), path), path);
            Assert.AreEqual(1f, ActiveValue(State(c, "CatchUp"), path), path);
            Assert.AreEqual(0f, ActiveValue(State(c, "Off"), path), path);
        }
    }

    [Test]
    public void Toggle_TransitionsUseActiveParam()
    {
        var c = TranslatorAnimationBuilder.BuildToggleController(Dir);
        var toAppear = State(c, "Off").transitions.Single();
        Assert.AreEqual("Appear", toAppear.destinationState.name);
        var fromAppear = State(c, "Appear").transitions;
        var toOn = fromAppear.Single(t => t.destinationState.name == "On");
        var appearToOff = fromAppear.Single(t => t.destinationState.name == "Off");
        var onToOff = State(c, "On").transitions.Single(t => t.destinationState.name == "Off");

        Assert.AreEqual(AnimatorConditionMode.If, toAppear.conditions.Single().mode);
        Assert.AreEqual(AnimatorConditionMode.IfNot, appearToOff.conditions.Single().mode);
        Assert.AreEqual(AnimatorConditionMode.IfNot, onToOff.conditions.Single().mode);
        foreach (var t in new[] { toAppear, appearToOff, onToOff })
        {
            Assert.AreEqual(C.ActiveParam, t.conditions[0].parameter);
            Assert.IsFalse(t.hasExitTime);
            Assert.AreEqual(0f, t.duration);
        }
        Assert.IsTrue(toOn.hasExitTime, "Appear lasts its clip, then follows normally");
        Assert.AreEqual(1f, toOn.exitTime);
        Assert.AreEqual(0f, toOn.duration);
        Assert.IsEmpty(toOn.conditions);
    }

    [Test]
    public void Toggle_DriversSetKatParams()
    {
        var c = TranslatorAnimationBuilder.BuildToggleController(Dir);
        Assert.IsEmpty(State(c, "On").behaviours, "drivers fire once, on Appear");
        var on = State(c, "Appear").behaviours.OfType<VRCAvatarParameterDriver>().Single().parameters;
        var off = State(c, "Off").behaviours.OfType<VRCAvatarParameterDriver>().Single().parameters;
        Assert.AreEqual(1f, on.Single(p => p.name == C.KatVisibleParam).value);
        Assert.AreEqual(255f, on.Single(p => p.name == C.KatPointerParam).value, "stale text is cleared on enable");
        Assert.AreEqual(0f, off.Single(p => p.name == C.KatVisibleParam).value);
        Assert.AreEqual(255f, off.Single(p => p.name == C.KatPointerParam).value);
    }

    [Test]
    public void Toggle_AppearSnapsThenFollowsSlowly()
    {
        var c = TranslatorAnimationBuilder.BuildToggleController(Dir);
        var binding = EditorCurveBinding.FloatCurve(C.PetRootName + "/" + C.PetFollowName,
            typeof(VRC.SDK3.Dynamics.Constraint.Components.VRCParentConstraint), C.FollowWeightProperty);
        float Weight(string state) =>
            AnimationUtility.GetEditorCurve((AnimationClip)State(c, state).motion, binding).keys[0].value;

        Assert.AreEqual(C.AppearWeight, Weight("Appear"));
        Assert.AreEqual(C.FollowWeight, Weight("On"));
        Assert.AreEqual(C.AppearSeconds, ((AnimationClip)State(c, "Appear").motion).length, 1e-4f);
    }

    [Test]
    public void Toggle_CatchUpSnapsWhenThePetIsFarBehind()
    {
        var c = TranslatorAnimationBuilder.BuildToggleController(Dir);
        Assert.AreEqual(AnimatorControllerParameterType.Bool, c.parameters.Single(p => p.name == C.NearParam).type);

        var toCatchUp = State(c, "On").transitions.Single(t => t.destinationState.name == "CatchUp");
        var back = State(c, "CatchUp").transitions.Single(t => t.destinationState.name == "On");
        var off = State(c, "CatchUp").transitions.Single(t => t.destinationState.name == "Off");
        Assert.AreEqual((AnimatorConditionMode.IfNot, C.NearParam), (toCatchUp.conditions.Single().mode, toCatchUp.conditions[0].parameter));
        Assert.AreEqual((AnimatorConditionMode.If, C.NearParam), (back.conditions.Single().mode, back.conditions[0].parameter));
        Assert.AreEqual((AnimatorConditionMode.IfNot, C.ActiveParam), (off.conditions.Single().mode, off.conditions[0].parameter));
        foreach (var t in new[] { toCatchUp, back, off })
        {
            Assert.IsFalse(t.hasExitTime);
            Assert.AreEqual(0f, t.duration);
        }

        var binding = EditorCurveBinding.FloatCurve(C.PetRootName + "/" + C.PetFollowName,
            typeof(VRC.SDK3.Dynamics.Constraint.Components.VRCParentConstraint), C.FollowWeightProperty);
        var weight = AnimationUtility.GetEditorCurve((AnimationClip)State(c, "CatchUp").motion, binding).keys[0].value;
        Assert.AreEqual(C.AppearWeight, weight);
        Assert.IsEmpty(State(c, "CatchUp").behaviours, "a teleport does not clear the text");
    }

    [Test]
    public void Kat_AllStatesWriteDefaultsOff_AndTargetKatText()
    {
        var c = TranslatorAnimationBuilder.BuildKatController(Dir, 8);
        Assert.IsTrue(States(c).All(s => !s.writeDefaultValues));
        var paths = c.animationClips
            .SelectMany(clip => AnimationUtility.GetCurveBindings(clip))
            .Select(b => b.path).Distinct().ToList();
        CollectionAssert.IsSubsetOf(paths, new[]
        {
            C.KatObjectName,
            C.KatObjectName + "/" + C.KatConstraintName + "/" + C.KatTextName,
        });
        Assert.IsFalse(c.parameters.Any(p => p.name.StartsWith("KAT_Keyboard")), "no keyboard");
    }

    [TestCase(4)]
    [TestCase(8)]
    [TestCase(16)]
    public void Kat_HasCharSyncParamsForSyncCount(int sync)
    {
        var c = TranslatorAnimationBuilder.BuildKatController(Dir, sync);
        Assert.AreEqual(sync, c.parameters.Count(p => p.name.StartsWith(C.KatCharSyncPrefix)));
    }

    [Test]
    public void Kat_RejectsUnsupportedSyncCount()
    {
        Assert.Throws<System.ArgumentException>(() => TranslatorAnimationBuilder.BuildKatController(Dir, 3));
    }
}
