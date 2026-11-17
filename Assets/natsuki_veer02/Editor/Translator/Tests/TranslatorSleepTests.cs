using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using C = TranslatorConfig;
using M = TranslatorMotionBuilder;
using S = TranslatorSleepBuilder;

/// <summary>The pet falls asleep after a minute of the wearer standing still and silent, and shakes itself awake.</summary>
public class TranslatorSleepTests
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

    AnimatorStateMachine Layer => c.layers.Single(l => l.name == S.LayerName).stateMachine;
    AnimatorState State(string name) => Layer.states.Select(s => s.state).Single(s => s.name == name);
    static AnimationClip Clip(AnimatorState s) => (AnimationClip)s.motion;

    static AnimationCurve Curve(AnimatorState s, string path, System.Type type, string property) =>
        AnimationUtility.GetEditorCurve(Clip(s), EditorCurveBinding.FloatCurve(path, type, property));

    static float Active(AnimatorState s, string path, float time) =>
        Curve(s, path, typeof(GameObject), "m_IsActive").Evaluate(time);

    static string Transitions(AnimatorState s) => string.Join(" | ", s.transitions.Select(t =>
        t.destinationState.name + ":" + string.Join("&", t.conditions.Select(k => k.parameter + k.mode + k.threshold))
        + (t.hasExitTime ? "@" + t.exitTime : "")));

    [Test]
    public void SleepLayer_UsesVoiceAndVelocity_WriteDefaultsOff()
    {
        Assert.AreEqual(AnimatorControllerParameterType.Float, c.parameters.Single(p => p.name == C.VoiceParam).type);
        Assert.AreEqual(1f, c.layers.Single(l => l.name == S.LayerName).defaultWeight);
        Assert.AreEqual("Awake", Layer.defaultState.name);
        Assert.IsTrue(Layer.states.All(s => !s.state.writeDefaultValues));
    }

    [Test]
    public void Drowsy_AfterOneMinuteStillAndSilent_FallsAsleep()
    {
        Assert.AreEqual(60f, C.SleepAfterSeconds);
        Assert.AreEqual($"Drowsy:{C.VelocityParam}Less{C.SleepStillSpeed}&{C.VoiceParam}Less{C.SleepVoiceLevel}",
            Transitions(State("Awake")));
        Assert.AreEqual(C.SleepAfterSeconds, Clip(State("Drowsy")).length, 1e-3f);
        Assert.AreEqual($"Awake:{C.VelocityParam}Greater{C.SleepStillSpeed} | Awake:{C.VoiceParam}Greater{C.SleepVoiceLevel} | Sleep:@1",
            Transitions(State("Drowsy")));
    }

    [Test]
    public void Sleep_WakesOnMovingOrTalking_ThenShakesAndOpensEyes()
    {
        Assert.AreEqual($"Wake:{C.VelocityParam}Greater{C.SleepStillSpeed} | Wake:{C.VoiceParam}Greater{C.SleepVoiceLevel}",
            Transitions(State("Sleep")));
        Assert.AreEqual("Awake:@1", Transitions(State("Wake")));
    }

    [Test]
    public void Sleep_ClosedEyesAndZzz()
    {
        var sleep = State("Sleep");
        foreach (var eye in new[] { S.EyeLPath, S.EyeRPath }) Assert.AreEqual(0f, Active(sleep, eye, 0f), eye);
        foreach (var eye in new[] { S.SleepEyeLPath, S.SleepEyeRPath }) Assert.AreEqual(1f, Active(sleep, eye, 0f), eye);
        Assert.AreEqual(1f, Curve(sleep, S.ZzzPath, typeof(ParticleSystem), "EmissionModule.enabled").Evaluate(0f));
        Assert.IsTrue(AnimationUtility.GetAnimationClipSettings(Clip(sleep)).loopTime);
    }

    [TestCase("Awake")]
    [TestCase("Drowsy")]
    public void Awake_OpenEyesNoZzzHeadStraight(string name)
    {
        var s = State(name);
        foreach (var eye in new[] { S.EyeLPath, S.EyeRPath }) Assert.AreEqual(1f, Active(s, eye, 0f), eye);
        foreach (var eye in new[] { S.SleepEyeLPath, S.SleepEyeRPath }) Assert.AreEqual(0f, Active(s, eye, 0f), eye);
        Assert.AreEqual(0f, Curve(s, S.ZzzPath, typeof(ParticleSystem), "EmissionModule.enabled").Evaluate(0f));
        Assert.AreEqual(0f, Curve(s, S.HeadPivotPath, typeof(Transform), "localEulerAnglesRaw.y").Evaluate(0f));
    }

    [Test]
    public void Wake_ShakesHeadThreeTimesWithEyesClosed_ThenOpensThem()
    {
        var wake = State("Wake");
        float len = Clip(wake).length;
        Assert.AreEqual(C.WakeShakeSeconds, len, 1e-3f);

        var yaw = Curve(wake, S.HeadPivotPath, typeof(Transform), "localEulerAnglesRaw.y");
        int turns = 0;
        float last = 0f;
        for (float t = 0; t <= len; t += len / 200f)
        {
            float v = yaw.Evaluate(t);
            if (Mathf.Abs(v) > C.WakeShakeAngle * 0.8f && Mathf.Sign(v) != Mathf.Sign(last)) { turns++; last = v; }
        }
        Assert.GreaterOrEqual(turns, 6, "left-right three times");
        Assert.AreEqual(0f, yaw.Evaluate(len), 1e-3f, "ends facing forward");

        Assert.AreEqual(1f, Active(wake, S.SleepEyeLPath, len * 0.5f), "eyes stay shut while shaking");
        Assert.AreEqual(0f, Active(wake, S.EyeLPath, len * 0.5f));
        Assert.AreEqual(1f, Active(wake, S.EyeLPath, len), "then open");
        Assert.AreEqual(0f, Active(wake, S.SleepEyeLPath, len));
        Assert.AreEqual(0f, Curve(wake, S.ZzzPath, typeof(ParticleSystem), "EmissionModule.enabled").Evaluate(0f), "Zzz stop at once");
    }

    [Test]
    public void Textures_SleepEyeIsAFlatBar_ZIsDrawn()
    {
        var bar = S.DrawSleepEye();
        Assert.Greater(bar.GetPixel(64, 64).a, 0.9f, "centre of the bar");
        Assert.Less(bar.GetPixel(64, 100).a, 0.1f, "nothing above it");
        Assert.Less(bar.GetPixel(64, 28).a, 0.1f, "nothing below it");
        var z = S.DrawZ();
        Assert.Greater(Enumerable.Range(0, 128).Count(x => z.GetPixel(x, 110).a > 0.5f), 40, "top stroke");
        Assert.Greater(Enumerable.Range(0, 128).Count(x => z.GetPixel(x, 18).a > 0.5f), 40, "bottom stroke");
    }
}
