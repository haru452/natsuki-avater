using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Dynamics.Constraint.Components;
using C = TranslatorConfig;
using M = TranslatorMotionBuilder;

public class TranslatorMotionTests
{
    const string Dir = TranslatorAssetTests.Dir;
    AnimatorController c;

    [SetUp]
    public void SetUp()
    {
        AssetDatabase.DeleteAsset(Dir);
        AssetDatabase.CreateFolder("Assets", "__TranslatorTest");
        c = M.BuildMotionController(Dir);
    }

    [TearDown]
    public void TearDown() => AssetDatabase.DeleteAsset(Dir);

    AnimatorStateMachine Layer(string name) => c.layers.Single(l => l.name == name).stateMachine;

    AnimatorState State(string layer, string name) => Layer(layer).states.Select(s => s.state).Single(s => s.name == name);

    static AnimationCurve Curve(AnimatorState state, string path, System.Type type, string property) =>
        AnimationUtility.GetEditorCurve((AnimationClip)state.motion, EditorCurveBinding.FloatCurve(path, type, property));

    static AnimationCurve Hop(AnimatorState state, string property) => Curve(state, M.HopPath, typeof(Transform), property);

    [Test]
    public void HasFourLayers_AllWriteDefaultsOff()
    {
        CollectionAssert.AreEqual(new[] { M.HopLayerName, M.LookLayerName, M.PatLayerName, TranslatorSleepBuilder.LayerName },
            c.layers.Select(l => l.name));
        Assert.IsTrue(c.layers.All(l => l.defaultWeight == 1f));
        Assert.IsTrue(c.layers.SelectMany(l => l.stateMachine.states).All(s => !s.state.writeDefaultValues));
        Assert.AreEqual(AnimatorControllerParameterType.Float, c.parameters.Single(p => p.name == C.VelocityParam).type);
        Assert.AreEqual(AnimatorControllerParameterType.Bool, c.parameters.Single(p => p.name == C.LookedParam).type);
        Assert.AreEqual(AnimatorControllerParameterType.Bool, c.parameters.Single(p => p.name == C.PatParam).type);
    }

    [Test]
    public void Hop_StartsWhenMovingAndStopsAfterLanding()
    {
        Assert.AreEqual("Idle", Layer(M.HopLayerName).defaultState.name);
        var start = State(M.HopLayerName, "Idle").transitions.Single();
        var stop = State(M.HopLayerName, "Hop").transitions.Single();

        Assert.AreEqual("Hop", start.destinationState.name);
        Assert.IsFalse(start.hasExitTime);
        Assert.AreEqual((AnimatorConditionMode.Greater, C.VelocityParam, C.HopStartSpeed),
            (start.conditions.Single().mode, start.conditions[0].parameter, start.conditions[0].threshold));

        Assert.AreEqual("Idle", stop.destinationState.name);
        Assert.AreEqual((AnimatorConditionMode.Less, C.HopStopSpeed), (stop.conditions.Single().mode, stop.conditions[0].threshold));
        Assert.IsTrue(stop.hasExitTime, "finishes the hop before standing still");
        Assert.Less(stop.exitTime, 1f, "below 1 so it is checked on every loop");
        Assert.Greater(stop.exitTime, 0.9f, "near the landing");
    }

    [Test]
    public void Hop_ClipLoopsAndPeaksMidway()
    {
        var hop = State(M.HopLayerName, "Hop");
        var clip = (AnimationClip)hop.motion;
        Assert.IsTrue(AnimationUtility.GetAnimationClipSettings(clip).loopTime);
        Assert.AreEqual(C.HopSeconds, clip.length, 1e-4f);

        var y = Hop(hop, "m_LocalPosition.y");
        Assert.AreEqual(0f, y.Evaluate(0f), 1e-4f, "takes off from the ground");
        Assert.AreEqual(C.HopHeight, y.Evaluate(C.HopSeconds / 2), 1e-3f, "top of the hop");
        Assert.AreEqual(0f, y.Evaluate(C.HopSeconds), 1e-4f, "lands");
        Assert.AreEqual(C.HopSquash.y, Hop(hop, "m_LocalScale.y").Evaluate(0f), 1e-4f, "squashed on landing");
        Assert.AreEqual(C.HopStretch.y, Hop(hop, "m_LocalScale.y").Evaluate(C.HopSeconds / 2), 1e-3f);
    }

    [Test]
    public void WaitingSpotTurnsOnlyWhileWalking()
    {
        float Weight(string state) =>
            Curve(State(M.HopLayerName, state), C.PetHeadingName, typeof(VRCRotationConstraint), C.HeadingWeightProperty).Evaluate(0f);
        Assert.AreEqual(C.MovingHeadingWeight, Weight("Hop"), "walking: the spot turns with the wearer");
        Assert.AreEqual(0f, Weight("Idle"), "standing: turning on the spot leaves the pet alone");
    }

    [Test]
    public void Idle_RestsAndAmblesForwardAndBack()
    {
        var idle = State(M.HopLayerName, "Idle");
        Assert.AreEqual(0f, Hop(idle, "m_LocalPosition.y").Evaluate(0f));
        foreach (var axis in new[] { "x", "y", "z" })
            Assert.AreEqual(1f, Hop(idle, "m_LocalScale." + axis).Evaluate(0f), axis);

        var clip = (AnimationClip)idle.motion;
        Assert.IsTrue(AnimationUtility.GetAnimationClipSettings(clip).loopTime);
        Assert.AreEqual(C.WanderSeconds, clip.length, 1e-3f);
        // The animator writes a Transform's whole position: an axis the clip leaves out becomes 0, which put
        // the pet inside the wearer's feet. Every state must keep the waiting spot at PetOffset on all axes.
        var z = Curve(idle, M.TargetPath, typeof(Transform), "m_LocalPosition.z");
        Assert.AreEqual(C.PetOffset.z, z.Evaluate(0f), 1e-4f, "starts where walking left it");
        Assert.AreEqual(C.PetOffset.z + C.WanderDistance, z.Evaluate(C.WanderSeconds / 4), 1e-3f, "a little forward");
        Assert.AreEqual(C.PetOffset.z - C.WanderDistance, z.Evaluate(C.WanderSeconds * 3 / 4), 1e-3f, "a little back");
        var hop = State(M.HopLayerName, "Hop");
        Assert.AreEqual(C.PetOffset.z, Curve(hop, M.TargetPath, typeof(Transform), "m_LocalPosition.z").Evaluate(0f),
            "no ambling while walking");
        foreach (var s in new[] { idle, hop })
        {
            Assert.AreEqual(C.PetOffset.x, Curve(s, M.TargetPath, typeof(Transform), "m_LocalPosition.x").Evaluate(0f), 1e-4f, s.name + " keeps the spot at the side");
            Assert.AreEqual(C.PetOffset.y, Curve(s, M.TargetPath, typeof(Transform), "m_LocalPosition.y").Evaluate(0f), 1e-4f, s.name);
            Assert.AreEqual(0f, Hop(s, "m_LocalPosition.x").Evaluate(0f), 1e-4f, s.name + " hop x");
            Assert.AreEqual(0f, Hop(s, "m_LocalPosition.z").Evaluate(0f), 1e-4f, s.name + " hop z");
        }
    }

    [Test]
    public void Look_FadesTheFacingConstraintInAndOut()
    {
        Assert.AreEqual("Away", Layer(M.LookLayerName).defaultState.name);
        float Weight(string state) =>
            Curve(State(M.LookLayerName, state), M.RobotPath, typeof(VRCAimConstraint), "GlobalWeight").Evaluate(0f);
        Assert.AreEqual(0f, Weight("Away"));
        Assert.AreEqual(1f, Weight("Look"));

        var toLook = State(M.LookLayerName, "Away").transitions.Single();
        var back = State(M.LookLayerName, "Look").transitions.Single();
        Assert.AreEqual((AnimatorConditionMode.If, C.LookedParam), (toLook.conditions.Single().mode, toLook.conditions[0].parameter));
        Assert.AreEqual((AnimatorConditionMode.IfNot, C.LookedParam), (back.conditions.Single().mode, back.conditions[0].parameter));
        foreach (var t in new[] { toLook, back })
        {
            Assert.IsFalse(t.hasExitTime);
            Assert.AreEqual(C.LookTurnSeconds, t.duration, 1e-5f, "turns smoothly");
        }
    }

    [Test]
    public void Pat_ShowsHeartsForAtLeastASecond()
    {
        Assert.AreEqual("Calm", Layer(M.PatLayerName).defaultState.name);
        float Emission(string state) =>
            Curve(State(M.PatLayerName, state), M.HeartsPath, typeof(ParticleSystem), "EmissionModule.enabled").Evaluate(0f);
        Assert.AreEqual(0f, Emission("Calm"));
        Assert.AreEqual(1f, Emission("Hearts"));

        var start = State(M.PatLayerName, "Calm").transitions.Single();
        var end = State(M.PatLayerName, "Hearts").transitions.Single();
        Assert.AreEqual((AnimatorConditionMode.If, C.PatParam), (start.conditions.Single().mode, start.conditions[0].parameter));
        Assert.IsFalse(start.hasExitTime, "hearts start right away");
        Assert.AreEqual((AnimatorConditionMode.IfNot, C.PatParam), (end.conditions.Single().mode, end.conditions[0].parameter));
        Assert.IsTrue(end.hasExitTime);
        Assert.AreEqual(C.HeartsMinSeconds, ((AnimationClip)State(M.PatLayerName, "Hearts").motion).length, 1e-4f);
    }
}
