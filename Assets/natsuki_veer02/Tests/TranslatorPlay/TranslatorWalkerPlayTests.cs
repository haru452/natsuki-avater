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
        var target = root.Find("Pet_Heading/Pet_Side/Pet_Target");

        // The contacts, worked out from the distances as VRChat does: "arrived" (near its spot), "near" (far off =
        // a teleport, and the pet is snapped over) and "close" (the wearer is near the pet).
        float Radius(Transform on, string param) => on.GetComponents<ContactReceiver>().Single(r => r.parameter == param).radius;
        float nearRadius = Radius(target, "TranslatorPetNear"), arriveRadius = Radius(target, "TranslatorPetArrived");
        float closeRadius = Radius(follow, "TranslatorPetClose");
        var sides = pet.GetComponentsInChildren<ContactReceiver>(true)
            .Where(r => r.parameter == "TranslatorPetLeft" || r.parameter == "TranslatorPetRight").ToList();
        bool landed = false;
        void Arrived()
        {
            float d = Vector3.Distance(follow.position, target.position);
            motion.SetBool("TranslatorPetArrived", d < arriveRadius);
            toggle.SetBool("TranslatorPetNear", d < nearRadius);
            motion.SetBool("TranslatorPetClose", Vector3.Distance(follow.position, avatar.transform.position) < closeRadius);
            foreach (var r in sides) motion.SetBool(r.parameter, InCapsule(follow.position, r));
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
                biggestStep = Mathf.Max(biggestStep, moved.magnitude);
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
                  $"mean facing {facing.Average():0.00}, samples {facing.Count}, biggest step {biggestStep:0.000} m");
        // Per frame, not per second: the constraints move a share of the gap each frame, so a short frame in the
        // test run would read as a burst of speed. 0.1 m a frame is 6 m/s at 60 fps; the old teleport was 3.6 m.
        Assert.Less(biggestStep, 0.1f, "never jumps");
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
            biggestStep = Mathf.Max(biggestStep, (follow.position - last).magnitude);
            last = follow.position;
        }
        Assert.Less(biggestStep, 0.1f, "walks round to the spot without jumping (the landing jump included)");
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

        // The wearer crouches and looks at her: their face is low, a little in front of her. Her head tips down to it.
        var head = hop.GetComponentsInChildren<Transform>(true).First(t => t.name == "Head");
        var aim = head.GetComponent<VRC.SDK3.Dynamics.Constraint.Components.VRCAimConstraint>();
        var gaze = pet.transform.Find("Pet_Gaze");
        gaze.position = follow.position + hop.forward * 0.8f + Vector3.up * 0.25f;
        motion.SetBool("TranslatorPetLooked", true);
        for (int i = 0; i < 60; i++) { Arrived(); yield return null; }
        var faceDir = head.TransformDirection(aim.AimAxis);
        var toFace = (gaze.position - head.position).normalized;
        Debug.Log($"[WalkDiag] crouch: head looks {faceDir}, face is {toFace}");
        Assert.Less(faceDir.y, -0.2f, "looks down at the crouching wearer");
        Assert.Greater(Vector3.Dot(faceDir, toFace), 0.85f, "toward their face (most of the way)");
        motion.SetBool("TranslatorPetLooked", false);

        // The wearer turns round on the spot (she stays: now behind them, on their left), then walks off: she goes to
        // the front LEFT (nearer), not round to the right.
        avatar.transform.rotation *= Quaternion.Euler(0, 180, 0);
        for (int i = 0; i < 30; i++) { Arrived(); yield return null; }
        for (float walked = 0; walked < 3f; walked += 1.5f * Time.deltaTime)
        {
            avatar.transform.position += avatar.transform.forward * 1.5f * Time.deltaTime;
            motion.SetFloat("VelocityMagnitude", 1.5f);
            Arrived();
            yield return null;
        }
        motion.SetFloat("VelocityMagnitude", 0f);
        for (int i = 0; i < 600; i++) { Arrived(); yield return null; }
        Assert.Less(Vector3.Distance(follow.position, avatar.transform.TransformPoint(-0.55f, 0, 0.55f)), 0.35f, "waits at the front left");

        // Mirror mode: she comes back level with the wearer (right beside them), so she is not inside the mirror.
        motion.SetBool("TranslatorPetMirror", true);
        for (int i = 0; i < 600; i++) { Arrived(); yield return null; }
        Assert.Less(Vector3.Distance(follow.position, avatar.transform.TransformPoint(-0.55f, 0, 0f)), 0.35f, "right beside the wearer");
        Assert.Greater(Vector3.Dot(hop.forward, avatar.transform.forward), 0.9f, "facing the mirror, as the wearer does");
    }

    /// <summary>Inside a capsule contact (it runs along its local Y), as VRChat works it out.</summary>
    static bool InCapsule(Vector3 p, ContactReceiver r)
    {
        var t = r.transform;
        float half = Mathf.Max(0, r.height / 2 - r.radius);
        var a = t.position - t.up * half;
        var b = t.position + t.up * half;
        var ab = b - a;
        float k = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector3.Distance(p, a + ab * k) < r.radius;
    }
}
#endif
