using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Dynamics.Constraint.Components;
using nadena.dev.modular_avatar.core;
using C = TranslatorConfig;
using G = TranslatorPetGaze;

/// <summary>
/// 視線調節: while the toggle is on her eyes turn as the wearer's head turns (relative to the wearer's body, relative
/// to her head); turned off, they keep that direction.
/// </summary>
public class TranslatorPetGazeTests
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

    Transform Head => TranslatorPetModel.Head(prefab.transform.Find(C.PetRootName));

    [Test]
    public void TheWearersLook_IsCopiedRelativeToTheirBody()
    {
        var look = prefab.transform.Find(G.LookName);
        Assert.IsNotNull(look, "on the prefab root: turns with the wearer's body");
        Assert.AreEqual(Quaternion.identity, look.localRotation);
        var rot = look.GetComponent<VRCRotationConstraint>();
        Assert.AreEqual(prefab.transform.Find(C.GazeName), rot.Sources.Single().SourceTransform, "the wearer's head (Pet_Gaze)");
        Assert.IsTrue(rot.IsActive && rot.Locked);
        Assert.IsFalse(rot.SolveInLocalSpace);
    }

    [Test]
    public void HerEyes_FollowTheAim_WhichCopiesTheLook_OnlyWhileAdjusting()
    {
        var head = Head;
        var aim = head.Find(G.AimName);
        Assert.IsNotNull(aim, "on her head: the kept direction moves with her head");
        Assert.AreEqual(Quaternion.identity, aim.localRotation, "looks ahead until adjusted");
        var rot = aim.GetComponent<VRCRotationConstraint>();
        Assert.AreEqual(prefab.transform.Find(G.LookName), rot.Sources.Single().SourceTransform);
        Assert.IsTrue(rot.SolveInLocalSpace, "the wearer's turn relative to their body, applied relative to her head");
        Assert.IsFalse(rot.IsActive, "off: it keeps where it was left");
        Assert.IsTrue(rot.Locked);

        foreach (var eyeName in new[] { C.Pet.LeftEyeBone, C.Pet.RightEyeBone })
        {
            var eye = TranslatorPetModel.FindDeep(head, eyeName);
            var restEye = TranslatorPetModel.FindDeep(model.transform, eyeName);
            var target = aim.Find(G.EyeTargetName(eyeName));
            Assert.AreEqual(Quaternion.Angle(restEye.localRotation, target.localRotation), 0f, 0.01f, "at rest the eye looks as modelled");
            var eyeRot = eye.GetComponent<VRCRotationConstraint>();
            Assert.AreEqual(target, eyeRot.Sources.Single().SourceTransform);
            Assert.IsTrue(eyeRot.IsActive && eyeRot.Locked);
            Assert.IsFalse(eyeRot.SolveInLocalSpace);
            Assert.AreEqual(Vector3.zero, eyeRot.RotationOffset);
        }
    }

    [Test]
    public void Toggle_TurnsTheAimOnAndOff()
    {
        var sm = c.layers.Single(l => l.name == G.LayerName).stateMachine;
        Assert.AreEqual("Hold", sm.defaultState.name);
        var states = sm.states.Select(s => s.state).ToDictionary(s => s.name);
        string aimPath = AnimationUtility.CalculateTransformPath(Head.Find(G.AimName), prefab.transform.Find(C.PetRootName));
        float Active(string state) => AnimationUtility.GetEditorCurve((AnimationClip)states[state].motion,
            EditorCurveBinding.FloatCurve(aimPath, typeof(VRCRotationConstraint), "IsActive")).Evaluate(0f);
        Assert.AreEqual(0f, Active("Hold"));
        Assert.AreEqual(1f, Active("Adjust"));
        Assert.AreEqual((AnimatorConditionMode.If, C.GazeAdjustParam), (states["Hold"].transitions.Single().conditions.Single().mode,
            states["Hold"].transitions.Single().conditions.Single().parameter));
        Assert.AreEqual(AnimatorConditionMode.IfNot, states["Adjust"].transitions.Single().conditions.Single().mode);
        Assert.IsTrue(states.Values.All(s => !s.writeDefaultValues));
    }

    // こっち向いて (the PC app sets FaceParam): she keeps looking at the wearer after the smile, until she sets off
    // after them (then her own client clears it) or is told to face front or turn round.
    [Test]
    public void Facing_KeepsLookingAtTheWearer_UntilSheSetsOff()
    {
        var sm = c.layers.Single(l => l.name == TranslatorMotionBuilder.LookLayerName).stateMachine;
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
        var toggle = sub.controls.Single(t => t.name == C.GazeAdjustLabel);
        Assert.AreEqual(C.GazeAdjustParam, toggle.parameter.name);
        var p = prefab.GetComponent<ModularAvatarParameters>().parameters.Single(x => x.nameOrPrefix == C.GazeAdjustParam);
        Assert.IsFalse(p.localOnly, "everyone sees her eyes move");
        Assert.IsFalse(p.saved);
    }
}
