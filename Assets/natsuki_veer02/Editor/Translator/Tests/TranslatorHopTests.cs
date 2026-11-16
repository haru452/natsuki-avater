using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using C = TranslatorConfig;

public class TranslatorHopTests
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

    static AnimatorState State(AnimatorController c, string name) =>
        c.layers.SelectMany(l => l.stateMachine.states).Select(s => s.state).Single(s => s.name == name);

    static AnimationCurve Curve(AnimatorState state, string property) =>
        AnimationUtility.GetEditorCurve((AnimationClip)state.motion,
            EditorCurveBinding.FloatCurve(C.PetHopName, typeof(Transform), property));

    [Test]
    public void Hop_HasIdleAndHop_WriteDefaultsOff()
    {
        var c = TranslatorMotionBuilder.BuildHopController(Dir);
        Assert.AreEqual(1, c.layers.Length);
        Assert.AreEqual("Idle", c.layers[0].stateMachine.defaultState.name);
        CollectionAssert.AreEquivalent(new[] { "Idle", "Hop" },
            c.layers[0].stateMachine.states.Select(s => s.state.name));
        Assert.IsTrue(c.layers[0].stateMachine.states.All(s => !s.state.writeDefaultValues));
        Assert.AreEqual(AnimatorControllerParameterType.Float,
            c.parameters.Single(p => p.name == C.VelocityParam).type);
    }

    [Test]
    public void Hop_StartsWhenMovingAndStopsAfterLanding()
    {
        var c = TranslatorMotionBuilder.BuildHopController(Dir);
        var start = State(c, "Idle").transitions.Single();
        var stop = State(c, "Hop").transitions.Single();

        Assert.AreEqual("Hop", start.destinationState.name);
        Assert.IsFalse(start.hasExitTime);
        Assert.AreEqual(AnimatorConditionMode.Greater, start.conditions.Single().mode);
        Assert.AreEqual(C.VelocityParam, start.conditions[0].parameter);
        Assert.AreEqual(C.HopStartSpeed, start.conditions[0].threshold);

        Assert.AreEqual("Idle", stop.destinationState.name);
        Assert.AreEqual(AnimatorConditionMode.Less, stop.conditions.Single().mode);
        Assert.AreEqual(C.HopStopSpeed, stop.conditions[0].threshold);
        Assert.IsTrue(stop.hasExitTime, "finishes the hop before standing still");
        Assert.Less(stop.exitTime, 1f, "below 1 so it is checked on every loop");
        Assert.Greater(stop.exitTime, 0.9f, "near the landing");
    }

    [Test]
    public void Hop_ClipLoopsAndPeaksMidway()
    {
        var c = TranslatorMotionBuilder.BuildHopController(Dir);
        var hop = State(c, "Hop");
        var clip = (AnimationClip)hop.motion;
        Assert.IsTrue(AnimationUtility.GetAnimationClipSettings(clip).loopTime);
        Assert.AreEqual(C.HopSeconds, clip.length, 1e-4f);

        var y = Curve(hop, "m_LocalPosition.y");
        Assert.AreEqual(0f, y.Evaluate(0f), 1e-4f, "takes off from the ground");
        Assert.AreEqual(C.HopHeight, y.Evaluate(C.HopSeconds / 2), 1e-3f, "top of the hop");
        Assert.AreEqual(0f, y.Evaluate(C.HopSeconds), 1e-4f, "lands");
        Assert.AreEqual(C.HopSquash.y, Curve(hop, "m_LocalScale.y").Evaluate(0f), 1e-4f, "squashed on landing");
        Assert.AreEqual(C.HopStretch.y, Curve(hop, "m_LocalScale.y").Evaluate(C.HopSeconds / 2), 1e-3f);
    }

    [Test]
    public void Idle_RestoresRestPose()
    {
        var c = TranslatorMotionBuilder.BuildHopController(Dir);
        var idle = State(c, "Idle");
        Assert.AreEqual(0f, Curve(idle, "m_LocalPosition.y").Evaluate(0f));
        foreach (var axis in new[] { "x", "y", "z" })
            Assert.AreEqual(1f, Curve(idle, "m_LocalScale." + axis).Evaluate(0f), axis);
    }
}
