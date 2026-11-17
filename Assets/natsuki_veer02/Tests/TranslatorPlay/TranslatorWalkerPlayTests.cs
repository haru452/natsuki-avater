#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// A humanoid pet (when the built prefab has one) on riko2: walks the wearer's path facing its own way of travel —
/// also when the wearer walks backwards — then goes round to the waiting spot and faces the wearer.
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
    public IEnumerator Model_WalksThePathFacingForward_EvenWhenTheWearerWalksBackwards_ThenWaitsAndFacesThem()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "TranslatorPet.prefab");
        if (prefab.transform.Find("Pet_Root/Pet_Trail") == null) Assert.Ignore("the robot build has no trail");
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

        void Arrived() => motion.SetBool("TranslatorPetArrived", Vector3.Distance(follow.position, target.position) < 0.3f);
        yield return null;
        toggle.SetBool("TranslatorActive", true);
        toggle.SetBool("TranslatorPetNear", true);
        for (int i = 0; i < 120; i++) { Arrived(); yield return null; }

        // Walk an L: 4 m forward, then 4 m to the right — the second leg backwards (facing the way we came).
        var path = new List<Vector3> { avatar.transform.position };
        var legs = new[] { (Vector3.forward, Quaternion.identity), (Vector3.right, Quaternion.Euler(0, -90, 0)) };
        var offPath = new List<float>();
        var last = follow.position; // read after each frame: mid-frame the pet still turns with the wearer until its constraints run
        var facing = new List<float>();
        foreach (var (dir, turn) in legs)
        {
            avatar.transform.rotation = turn; // an instant snap turn, as in VR
            for (float walked = 0; walked < 4f; walked += 1.5f * Time.deltaTime)
            {
                avatar.transform.position += dir * 1.5f * Time.deltaTime;
                path.Add(avatar.transform.position);
                motion.SetFloat("VelocityMagnitude", 1.5f);
                Arrived();
                yield return null;
                var moved = follow.position - last;
                last = follow.position;
                if (path.Count > 90 && moved.magnitude > 0.005f)
                {
                    offPath.Add(DistanceToPath(follow.position, path));
                    facing.Add(Vector3.Dot(hop.forward, moved.normalized));
                    if (facing[facing.Count - 1] < 0.7f)
                        Debug.Log($"[WalkDiag] bad facing {facing[facing.Count - 1]:0.00} at sample {facing.Count} leg {dir} walked {walked:0.00} " +
                                  $"state {motion.GetCurrentAnimatorStateInfo(0).shortNameHash} pet {follow.position} moved {moved}");
                }
            }
        }
        Debug.Log($"[WalkDiag] max off path {offPath.Max():0.00} m, worst facing {facing.Min():0.00}, " +
                  $"mean facing {facing.Average():0.00}, samples {facing.Count}");
        Assert.Less(offPath.Max(), 0.35f, "walks where the wearer walked");
        Assert.Greater(facing.Where((_, i) => i > facing.Count / 10).Min(), 0.7f, "faces the way it goes, never backwards");

        // Stop: round to the front right, then face the wearer.
        motion.SetFloat("VelocityMagnitude", 0f);
        for (int i = 0; i < 600; i++) { Arrived(); yield return null; }
        Assert.Less(Vector3.Distance(follow.position, avatar.transform.TransformPoint(1.6f, 0, 1.6f)), 0.35f, "waits at the front right");
        var toWearer = avatar.transform.position - hop.position;
        toWearer.y = 0;
        Assert.Greater(Vector3.Dot(hop.forward, toWearer.normalized), 0.9f, "faces the wearer");
    }

    static float DistanceToPath(Vector3 p, List<Vector3> path)
    {
        float best = float.MaxValue;
        for (int i = 1; i < path.Count; i++)
        {
            var a = path[i - 1];
            var b = path[i];
            var ab = b - a;
            float t = ab.sqrMagnitude > 0 ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0;
            var q = a + ab * t;
            best = Mathf.Min(best, Vector2.Distance(new Vector2(p.x, p.z), new Vector2(q.x, q.z)));
        }
        return best;
    }
}
#endif
