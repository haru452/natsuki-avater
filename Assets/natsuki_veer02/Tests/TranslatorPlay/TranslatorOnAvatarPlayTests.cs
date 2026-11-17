#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// The pet on a real avatar (riko2), driven the way Modular Avatar merges it: each merged controller runs
/// relative to the object it was merged from. Turn the toggle on and check the robot and its text can be seen.
/// </summary>
public class TranslatorOnAvatarPlayTests
{
    const string Dir = "Assets/natsuki_veer02/Translator/";
    const string AvatarPath = "Assets/riko2/Prefab/riko2.prefab";
    GameObject avatar;
    GameObject cam;

    [TearDown]
    public void TearDown()
    {
        Object.Destroy(avatar);
        Object.Destroy(cam);
    }

    static Animator Drive(Transform at, string controller)
    {
        var a = at.gameObject.AddComponent<Animator>();
        a.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(Dir + controller);
        Assert.IsNotNull(a.runtimeAnimatorController, controller);
        return a;
    }

    [UnityTest]
    public IEnumerator ToggleOn_RobotAndTextAreSeenBesideTheAvatar()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarPath);
        if (model == null) Assert.Ignore("riko2 not in project");
        avatar = Object.Instantiate(model);
        avatar.transform.position = new Vector3(5, 0, 5);
        var pet = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "TranslatorPet.prefab"), avatar.transform);
        pet.name = "TranslatorPet";

        var toggle = Drive(pet.transform, "Translator_Toggle.controller");
        var root = pet.transform.Find("Pet_Root");
        Drive(root, "Translator_Motion.controller");
        yield return null;
        toggle.SetBool("TranslatorActive", true);
        for (int i = 0; i < 60; i++) yield return null;

        var robot = root.Find("Pet_Follow/Pet_Hop/Pet_Robot");
        var body = robot.Find("Body").GetComponent<Renderer>();
        var text = root.Find("Pet_Follow/Pet_Text");
        Debug.Log($"[PetDiag] Pet_Root active={root.gameObject.activeInHierarchy} robotPos={robot.position} " +
                  $"avatarPos={avatar.transform.position} lossyScale={robot.lossyScale} bodyEnabled={body.enabled} " +
                  $"bounds={body.bounds} textPos={text.position} state={toggle.GetCurrentAnimatorStateInfo(0).shortNameHash}");
        foreach (var r in robot.GetComponentsInChildren<Renderer>(true))
            Debug.Log($"[PetDiag] {r.name} activeInHierarchy={r.gameObject.activeInHierarchy} enabled={r.enabled} mat={r.sharedMaterial?.shader?.name}");

        Assert.IsTrue(root.gameObject.activeInHierarchy, "toggle On shows Pet_Root");
        Assert.IsTrue(body.gameObject.activeInHierarchy && body.enabled, "robot body drawn");
        Assert.Less(Vector3.Distance(robot.position, avatar.transform.TransformPoint(0.55f, 0, 0.55f)), 0.3f, "beside the avatar");
        Assert.Greater(robot.lossyScale.y, 0.5f, "full size");

        // Look from the avatar's eyes toward the pet.
        cam = new GameObject("eye");
        var c = cam.AddComponent<Camera>();
        cam.transform.position = avatar.transform.position + Vector3.up * 1.4f;
        cam.transform.LookAt(robot.position + Vector3.up * 0.2f);
        c.Render();
        Assert.IsTrue(body.isVisible, "the eye camera sees the robot");
    }
}
#endif
