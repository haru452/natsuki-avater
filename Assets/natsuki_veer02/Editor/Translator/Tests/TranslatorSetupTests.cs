using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

public class TranslatorSetupTests
{
    const string Dir = TranslatorAssetTests.Dir;
    GameObject prefab;
    GameObject avatar;
    VRCExpressionParameters parameters;

    [OneTimeSetUp]
    public void BuildOnce()
    {
        AssetDatabase.DeleteAsset(Dir);
        AssetDatabase.CreateFolder("Assets", "__TranslatorTest");
        prefab = TranslatorPrefabBuilder.Build(Dir);
    }

    [OneTimeTearDown]
    public void CleanUp() => AssetDatabase.DeleteAsset(Dir);

    [SetUp]
    public void SetUp()
    {
        avatar = TestAvatar.Create();
        var d = avatar.AddComponent<VRCAvatarDescriptor>();
        parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
        parameters.parameters = new[]
        {
            new VRCExpressionParameters.Parameter { name = "Fire", valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
        };
        AssetDatabase.CreateAsset(parameters, Dir + "/avatar_params.asset");
        d.expressionParameters = parameters;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(avatar);
        AssetDatabase.DeleteAsset(Dir + "/avatar_params.asset");
    }

    [Test]
    public void Place_AddsInstanceUnderAvatarRoot()
    {
        var placed = TranslatorSetup.Place(avatar, prefab);
        Assert.AreEqual(avatar.transform, placed.transform.parent);
        Assert.AreEqual(prefab, PrefabUtility.GetCorrespondingObjectFromSource(placed));
    }

    [Test]
    public void Place_Again_DropsStaleOverrides()
    {
        // An older build lifted the subtitle 1.1 m in the scene; once the prefab put it on her head, that leftover
        // override lifted it 1.1 m above her head in VRChat.
        var placed = TranslatorSetup.Place(avatar, prefab);
        var text = placed.GetComponentsInChildren<Transform>(true).First(t => t.name == TranslatorConfig.TextName);
        var source = PrefabUtility.GetCorrespondingObjectFromSource(text);
        text.localPosition = source.localPosition + Vector3.up * 1.1f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(text);
        TranslatorSetup.Place(avatar, prefab);
        Assert.AreEqual(source.localPosition, text.localPosition);
        Assert.AreEqual(Vector3.zero, placed.transform.localPosition);
    }

    [Test]
    public void Place_Twice_DoesNotDuplicate()
    {
        var first = TranslatorSetup.Place(avatar, prefab);
        var second = TranslatorSetup.Place(avatar, prefab);
        Assert.AreSame(first, second);
        Assert.AreEqual(1, avatar.transform.Cast<Transform>().Count(t => t.name == TranslatorConfig.RootName));
    }

    [Test]
    public void Place_DoesNotTouchAvatarAssets()
    {
        AssetDatabase.SaveAssets();
        var before = File.ReadAllBytes(Dir + "/avatar_params.asset");
        TranslatorSetup.Place(avatar, prefab);
        AssetDatabase.SaveAssets();
        CollectionAssert.AreEqual(before, File.ReadAllBytes(Dir + "/avatar_params.asset"));
        Assert.AreEqual(1, parameters.parameters.Length);
    }

    [Test]
    public void Place_Throws_WhenBudgetExceeded()
    {
        parameters.parameters = Enumerable.Range(0, 25).Select(i => new VRCExpressionParameters.Parameter
        {
            name = "P" + i, valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true,
        }).ToArray(); // 200 bits

        var e = Assert.Throws<InvalidOperationException>(() => TranslatorSetup.Place(avatar, prefab));
        StringAssert.Contains("275", e.Message);
        StringAssert.Contains("同期設定を下げる", e.Message);
        Assert.AreEqual(0, avatar.transform.Cast<Transform>().Count(t => t.name == TranslatorConfig.RootName));
    }

    [Test]
    public void Place_DoesNotDoubleCountParamsTheAvatarAlreadyHas()
    {
        // 181 bits of others + TranslatorActive and TranslatorLocalAI already declared.
        parameters.parameters = Enumerable.Range(0, 22).Select(i => new VRCExpressionParameters.Parameter
            {
                name = "P" + i, valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true,
            })
            .Concat(Enumerable.Range(0, 5).Select(i => new VRCExpressionParameters.Parameter
            {
                name = "B" + i, valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true,
            }))
            .Concat(new[]
            {
                new VRCExpressionParameters.Parameter { name = TranslatorConfig.ActiveParam, valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = TranslatorConfig.LocalAIParam, valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
            }).ToArray(); // 176 + 5 + 2 = 183 bits. Counted twice: 183 + 75 = 258 (refused). Correctly: 181 + 75 = 256 (fits).

        Assert.IsNotNull(TranslatorSetup.Place(avatar, prefab));
    }

    [Test]
    public void Place_Throws_WhenAvatarAlreadyHasKatParams()
    {
        parameters.parameters = new[]
        {
            new VRCExpressionParameters.Parameter { name = "KAT_Pointer", valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true },
        };
        var e = Assert.Throws<InvalidOperationException>(() => TranslatorSetup.Place(avatar, prefab));
        StringAssert.Contains("KAT", e.Message);
    }

    [Test]
    public void Place_Throws_WhenAvatarAlreadyHasKatObject()
    {
        new GameObject("KillFrenzyAvatarText").transform.SetParent(avatar.transform);
        Assert.Throws<InvalidOperationException>(() => TranslatorSetup.Place(avatar, prefab));
        Assert.AreEqual(0, avatar.transform.Cast<Transform>().Count(t => t.name == TranslatorConfig.RootName));
    }

    [Test]
    public void Place_Throws_WhenNoAvatarDescriptor()
    {
        var plain = new GameObject("NotAnAvatar");
        try
        {
            Assert.Throws<InvalidOperationException>(() => TranslatorSetup.Place(plain, prefab));
        }
        finally
        {
            Object.DestroyImmediate(plain);
        }
    }

    [Test]
    public void UsedBits_IgnoresUnnamedAndUnsyncedSlots()
    {
        parameters.parameters = new[]
        {
            new VRCExpressionParameters.Parameter { name = "", valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true },
            new VRCExpressionParameters.Parameter { name = "Local", valueType = VRCExpressionParameters.ValueType.Float, networkSynced = false },
            new VRCExpressionParameters.Parameter { name = "F", valueType = VRCExpressionParameters.ValueType.Float, networkSynced = true },
            new VRCExpressionParameters.Parameter { name = "B", valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
        };
        Assert.AreEqual(9, TranslatorBudget.UsedBits(parameters));
        Assert.AreEqual(0, TranslatorBudget.UsedBits(null));
    }
}
