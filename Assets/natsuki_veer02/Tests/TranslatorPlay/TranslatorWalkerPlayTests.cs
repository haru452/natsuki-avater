#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.Dynamics;

/// <summary>
/// A humanoid pet (when the built prefab has one) on riko2: when the wearer moves, walks toward its spot facing its own way of travel —
/// also when the wearer walks backwards — and once there faces the wearer.
/// The motion controller runs on Pet_Root as Modular Avatar merges it; "arrived" is set from the distance
/// (the contact does the same in VRChat).
/// </summary>
public class TranslatorWalkerPlayTests
{
    const string Dir = "Assets/natsuki_veer02/Translator/";
    GameObject avatar;

    [TearDown]
    public void TearDown() => Object.Destroy(avatar);

    [UnityTest]
    public IEnumerator Model_WalksTowardItsSpotFacingForward_EvenWhenTheWearerWalksBackwards_ThenWaitsAndFacesThem()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "TranslatorPet.prefab");
        if (prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) Assert.Ignore("the robot hops instead");
        Application.targetFrameRate = 60;

        avatar = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/riko2/Prefab/riko2.prefab"));
        var pet = Object.Instantiate(prefab, avatar.transform);
        var toggle = pet.AddComponent<Animator>();
        toggle.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(Dir + "Translator_Toggle.controller");
        var root = pet.transform.Find("Pet_Root");
        var motion = root.gameObject.AddComponent<Animator>();
        motion.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(Dir + "Translator_Motion.controller");
        var follow = root.Find("Pet_Follow");
        var hop = follow.Find("Pet_Hop");
        var target = root.Find("Pet_Heading/Pet_Target");

        // The contacts, worked out from the distances as VRChat does: "arrived" (near its spot), "near" (far off =
        // a teleport, and the pet is snapped over) and "close" (the wearer is near the pet).
        float Radius(Transform on, string param) => on.GetComponents<ContactReceiver>().Single(r => r.parameter == param).radius;
        float nearRadius = Radius(target, "TranslatorPetNear"), arriveRadius = Radius(target, "TranslatorPetArrived");
        float closeRadius = Radius(follow, "TranslatorPetClose");
        bool landed = false;
        void Arrived()
        {
            float d = Vector3.Distance(follow.position, target.position);
            motion.SetBool("TranslatorPetArrived", d < arriveRadius);
            toggle.SetBool("TranslatorPetNear", d < nearRadius);
            motion.SetBool("TranslatorPetClose", Vector3.Distance(follow.position, avatar.transform.position) < closeRadius);
            landed |= motion.GetCurrentAnimatorStateInfo(0).IsName("Land");
        }
        yield return null;
        toggle.SetBool("TranslatorActive", true);
        for (int i = 0; i < 120; i++) { Arrived(); yield return null; }

        // Walk an L: 4 m forward, then 4 m to the right — the second leg backwards (facing the way we came) — then
        // run on 8 m. Speeds are VRChat's walk and run.
        var path = new List<Vector3> { avatar.transform.position };
        var legs = new[]
        {
            (Vector3.forward, Quaternion.identity, 4f, 1.5f),
            (Vector3.right, Quaternion.Euler(0, -90, 0), 4f, 1.5f),
            (Vector3.right, Quaternion.Euler(0, 90, 0), 8f, 4f),
        };
        float biggestStep = 0;
        var offPath = new List<float>();
        var last = follow.position; // read after each frame: mid-frame the pet still turns with the wearer until its constraints run
        var facing = new List<float>();
        foreach (var (dir, turn, length, speed) in legs)
        {
            avatar.transform.rotation = turn; // an instant snap turn, as in VR
            for (float walked = 0; walked < length; walked += speed * Time.deltaTime)
            {
                avatar.transform.position += dir * speed * Time.deltaTime;
                path.Add(avatar.transform.position);
                motion.SetFloat("VelocityMagnitude", speed);
                Arrived();
                yield return null;
                var moved = follow.position - last;
                last = follow.position;
                biggestStep = Mathf.Max(biggestStep, moved.magnitude / Time.deltaTime);
                if (path.Count > 60 && moved.magnitude > 0.005f)
                {
                    offPath.Add(Vector3.Distance(follow.position, target.position));
                    facing.Add(Vector3.Dot(hop.forward, moved.normalized));
                    if (facing[facing.Count - 1] < 0.7f)
                        Debug.Log($"[WalkDiag] bad facing {facing[facing.Count - 1]:0.00} at sample {facing.Count} leg {dir} walked {walked:0.00} " +
                                  $"state {motion.GetCurrentAnimatorStateInfo(0).shortNameHash} pet {follow.position} moved {moved}");
                }
            }
        }
        Debug.Log($"[WalkDiag] farthest from its spot {offPath.Max():0.00} m, worst facing {facing.Min():0.00}, " +
                  $"mean facing {facing.Average():0.00}, samples {facing.Count}, fastest {biggestStep:0.0} m/s");
        Assert.Less(biggestStep, 6f, "never jumps: no faster than a little over the wearer's run");
        Assert.Less(offPath.Max(), 2.5f, "heads for its spot beside the wearer instead of trailing behind");
        Assert.Greater(facing.Where((_, i) => i > facing.Count / 10).Min(), 0.7f, "faces the way it goes, never backwards");

        // Stop: round to the front right, jump the last bit, then face the way the wearer faces.
        motion.SetFloat("VelocityMagnitude", 0f);
        biggestStep = 0;
        last = follow.position;
        for (int i = 0; i < 600; i++)
        {
            Arrived();
            yield return null;
            biggestStep = Mathf.Max(biggestStep, (follow.position - last).magnitude / Time.deltaTime);
            last = follow.position;
        }
        Assert.Less(biggestStep, 6f, "walks round to the spot without jumping");
        Assert.IsTrue(landed, "jumps into its spot instead of sliding");
        Assert.Less(Vector3.Distance(follow.position, avatar.transform.TransformPoint(0.55f, 0, 0.55f)), 0.35f, "waits where the robot does");
        Assert.Greater(Vector3.Dot(hop.forward, avatar.transform.forward), 0.9f, "faces the way the wearer faced");

        // Looking around without moving: one full turn on the spot. The pet stays where it is.
        var before = follow.position;
        for (float turned = 0; turned < 360f; turned += 120f * Time.deltaTime)
        {
            avatar.transform.rotation *= Quaternion.Euler(0, 120f * Time.deltaTime, 0);
            Arrived();
            yield return null;
        }
        for (int i = 0; i < 60; i++) { Arrived(); yield return null; }
        Assert.Less(Vector3.Distance(follow.position, before), 0.05f, "stays put while the wearer only looks around");
        Assert.Greater(Vector3.Dot(hop.forward, Quaternion.Euler(0, 90, 0) * Vector3.forward), 0.9f, "still faces the first direction");

        // Walking up to the pet: it does not back away.
        var toPet = follow.position - avatar.transform.position;
        toPet.y = 0;
        for (float walked = 0; walked < 0.6f; walked += 1f * Time.deltaTime)
        {
            avatar.transform.position += toPet.normalized * 1f * Time.deltaTime;
            motion.SetFloat("VelocityMagnitude", 1f);
            Arrived();
            yield return null;
        }
        motion.SetFloat("VelocityMagnitude", 0f);
        for (int i = 0; i < 120; i++) { Arrived(); yield return null; }
        Assert.Less(Vector3.Distance(follow.position, before), 0.05f, "stays when the wearer comes close");

        // Walking off past the close radius: it goes to its new spot.
        avatar.transform.rotation = Quaternion.identity;
        for (float walked = 0; walked < 4f; walked += 1.5f * Time.deltaTime)
        {
            avatar.transform.position += Vector3.forward * 1.5f * Time.deltaTime;
            motion.SetFloat("VelocityMagnitude", 1.5f);
            Arrived();
            yield return null;
        }
        motion.SetFloat("VelocityMagnitude", 0f);
        for (int i = 0; i < 600; i++) { Arrived(); yield return null; }
        Assert.Less(Vector3.Distance(follow.position, avatar.transform.TransformPoint(0.55f, 0, 0.55f)), 0.35f, "follows once the wearer is away");
        Assert.Greater(Vector3.Dot(hop.forward, Vector3.forward), 0.9f, "and faces the wearer's new direction");
    }
}
#endif
