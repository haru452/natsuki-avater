using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Dynamics.Constraint.Components;
using nadena.dev.modular_avatar.core;
using C = TranslatorConfig;
using M = TranslatorMotionBuilder;
using P = TranslatorPetPose;

/// <summary>
/// 向き合わせ: while the toggle is on her body faces the way the wearer's body faces; turned off, she keeps that way
/// until she sets off. Also こっち向いて (FaceParam).
/// </summary>
public class TranslatorPetPoseTests
{
    const string Dir = TranslatorAssetTests.Dir;
    GameObject model;
    GameObject prefab;
    AnimatorController c;

    [OneTimeSetUp]
    public void BuildOnce()
    {
        model = AssetDatabase.LoadAssetAtPath<GameObject>(C.PetModelPath);
        if (model == null) Assert.Ignore("pet model not in this project");
        AssetDatabase.DeleteAsset(Dir);
        AssetDatabase.CreateFolder("Assets", "__TranslatorTest");
        prefab = TranslatorPrefabBuilder.Build(Dir, petModel: model);
        c = AssetDatabase.LoadAssetAtPath<AnimatorController>(Dir + "/" + TranslatorMotionBuilder.ControllerName);
    }

    [OneTimeTearDown]
    public void CleanUp() => AssetDatabase.DeleteAsset(Dir);

    Transform Hop => prefab.transform.Find(C.PetRootName + "/" + M.HopPath);

    [Test]
    public void TheKeptWay_StaysPut_UnlessTheWearersBodyIsWeightedIn()
    {
        var dir = prefab.transform.Find(C.PetRootName + "/" + P.DirName);
        Assert.IsNotNull(dir, "on Pet_Root");
        Assert.AreEqual(Hop, dir.GetComponent<VRCPositionConstraint>().Sources.Single().SourceTransform, "kept at her");
        var rot = dir.GetComponent<VRCRotationConstraint>();
        Assert.AreEqual(dir, rot.Sources[0].SourceTransform, "keeps its own way");
        Assert.AreEqual(1f, rot.Sources[0].Weight);
        Assert.AreEqual(prefab.transform, rot.Sources[1].SourceTransform, "the wearer's body");
        Assert.AreEqual(0f, rot.Sources[1].Weight, "not until the toggle is on");
        Assert.IsTrue(!rot.AffectsRotationX && rot.AffectsRotationY && !rot.AffectsRotationZ, "upright");
        Assert.IsTrue(rot.IsActive && rot.Locked);

        var ahead = dir.Find(P.AheadName);
        Assert.AreEqual(Vector3.forward, ahead.localPosition);
        var aim = Hop.GetComponent<VRCAimConstraint>();
        Assert.AreEqual(3, aim.Sources.Count);
        Assert.AreEqual(ahead, aim.Sources[P.AheadSource].SourceTransform, "her body can aim the kept way");
        Assert.AreEqual(0f, aim.Sources[P.AheadSource].Weight);
    }

    [Test]
    public void Toggle_FollowsTheWearersWay_ThenKeepsIt_UntilSheSetsOff()
    {
        Assert.AreEqual(P.LayerName, c.layers.Last().name, "after the Body layer, so its weights win");
        var sm = c.layers.Single(l => l.name == P.LayerName).stateMachine;
        Assert.AreEqual("Free", sm.defaultState.name);
        var states = sm.states.Select(s => s.state).ToDictionary(s => s.name);
        float Weight(string state, string path, System.Type type, int source)
        {
            var curve = AnimationUtility.GetEditorCurve((AnimationClip)states[state].motion,
                EditorCurveBinding.FloatCurve(path, type, "Sources.source" + source + ".Weight"));
            return curve == null ? float.NaN : curve.Evaluate(0f);
        }
        float Follows(string s) => Weight(s, P.DirName, typeof(VRCRotationConstraint), 1);
        float Aim(string s, int source) => Weight(s, M.HopPath, typeof(VRCAimConstraint), source);
        Assert.AreEqual((0f, C.MovingHeadingWeight, 0f), (Follows("Free"), Follows("Follow"), Follows("Keep")));
        Assert.AreEqual((0f, 1f, 1f), (Aim("Free", P.AheadSource), Aim("Follow", P.AheadSource), Aim("Keep", P.AheadSource)));
        Assert.IsNaN(Aim("Free", 0), "free: the Body layer turns her");
        Assert.IsNaN(Aim("Free", 1));
        foreach (var s in new[] { "Follow", "Keep" })
            Assert.AreEqual((0f, 0f), (Aim(s, 0), Aim(s, 1)), s + ": the spot and the point ahead give way");

        string Transitions(string name) => string.Join(" | ", states[name].transitions.Select(t =>
            t.destinationState.name + ":" + string.Join("&", t.conditions.Select(k => k.parameter + k.mode + k.threshold))));
        string setOff = $"Free:{C.CloseParam}IfNot0&{C.VelocityParam}Greater{C.HopStartSpeed}&{C.WaitParam}IfNot0";
        Assert.AreEqual($"Follow:{C.PoseParam}If0&{C.VelocityParam}Less{C.HopStopSpeed}", Transitions("Free"));
        Assert.AreEqual($"Keep:{C.PoseParam}IfNot0 | {setOff}", Transitions("Follow"));
        Assert.AreEqual($"Follow:{C.PoseParam}If0 | {setOff}", Transitions("Keep"));
        Assert.IsTrue(states.Values.All(s => !s.writeDefaultValues && s.motion != null));
    }

    // こっち向いて (the PC app sets FaceParam): she keeps looking at the wearer after the smile, until she sets off
    // after them (then her own client clears it) or is told to face front or turn round.
    [Test]
    public void Facing_KeepsLookingAtTheWearer_UntilSheSetsOff()
    {
        var sm = c.layers.Single(l => l.name == M.LookLayerName).stateMachine;
        var states = sm.states.Select(s => s.state).ToDictionary(s => s.name);
        string Transitions(string name) => string.Join(" | ", states[name].transitions.Select(t =>
            t.destinationState.name + ":" + string.Join("&", t.conditions.Select(k => k.parameter + k.mode + k.threshold))));
        foreach (var from in new[] { "Away", "Look", "Called" })
            StringAssert.Contains($"Facing:{C.FaceParam}If0&{C.TurnParam}IfNot0", Transitions(from), from);
        Assert.AreSame(states["Look"].motion, states["Facing"].motion, "looks at the wearer like a look, and holds it");
        Assert.AreEqual($"Away:{C.FaceParam}IfNot0 | FaceEnd:{C.CloseParam}IfNot0&{C.VelocityParam}Greater{C.HopStartSpeed}&{C.WaitParam}IfNot0"
            + $" | Away:{C.TurnParam}If0", Transitions("Facing"));
        var driver = states["FaceEnd"].behaviours.OfType<VRC.SDK3.Avatars.Components.VRCAvatarParameterDriver>().Single();
        Assert.IsTrue(driver.localOnly, "the wearer's client clears it; the sync tells the rest");
        Assert.AreEqual((C.FaceParam, 0f), (driver.parameters.Single().name, driver.parameters.Single().value));
        Assert.AreEqual($"Away:{C.FaceParam}IfNot0", Transitions("FaceEnd"));
        Assert.IsNotNull(states["FaceEnd"].motion, "no motionless states (they froze the avatar)");

        var p = prefab.GetComponent<ModularAvatarParameters>().parameters.Single(x => x.nameOrPrefix == C.FaceParam);
        Assert.IsFalse(p.localOnly);
        Assert.IsFalse(p.saved);
    }

    [Test]
    public void Menu_HasTheToggle_Synced()
    {
        var sub = prefab.GetComponent<ModularAvatarMenuInstaller>().menuToAppend.controls.Single().subMenu;
        var toggle = sub.controls.Single(t => t.name == C.PoseLabel);
        Assert.AreEqual(C.PoseParam, toggle.parameter.name);
        Assert.IsFalse(sub.controls.Any(t => t.name == "視線調節"), "replaced");
        var p = prefab.GetComponent<ModularAvatarParameters>().parameters.Single(x => x.nameOrPrefix == C.PoseParam);
        Assert.IsFalse(p.localOnly, "everyone sees her turn");
        Assert.IsFalse(p.saved);
    }
}
