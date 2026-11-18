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
