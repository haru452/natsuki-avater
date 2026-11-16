using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class TranslatorAssetTests
{
    public const string Dir = "Assets/__TranslatorTest";

    [SetUp]
    public void SetUp()
    {
        AssetDatabase.DeleteAsset(Dir);
        AssetDatabase.CreateFolder("Assets", "__TranslatorTest");
    }

    [TearDown]
    public void TearDown() => AssetDatabase.DeleteAsset(Dir);

    static int FileCount() => Directory.GetFiles(Dir).Length;

    [Test]
    public void RobotMaterials_AreSavedAndEyesGlowAsRings()
    {
        var m = TranslatorRobotBuilder.CreateMaterials(Dir);
        foreach (var mat in new[] { m.White, m.Dark, m.Grey })
        {
            Assert.AreEqual("Standard", mat.shader.name);
            StringAssert.StartsWith(Dir + "/Robot_", AssetDatabase.GetAssetPath(mat));
        }
        Assert.AreEqual("Unlit/Transparent", m.Eye.shader.name, "eyes look lit up in any world");
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(m.Eye.mainTexture)));
        Assert.AreEqual(0f, tex.GetPixel(64, 64).a, "hollow in the middle");
        Assert.AreEqual(1f, tex.GetPixel(64, 64 + 48).a, 0.01f, "solid on the ring");
        Assert.AreEqual(0f, tex.GetPixel(2, 2).a, "transparent outside");
        Object.DestroyImmediate(tex);
    }

    [Test]
    public void SignMaterial_IsUnlitAndTextureHasVisiblePixels()
    {
        var mat = TranslatorAssetGenerator.SignMaterial(Dir);
        Assert.AreEqual("Unlit/Transparent", mat.shader.name);
        var px = ReadPixels((Texture2D)mat.mainTexture);
        Assert.IsTrue(px.Any(c => c.a > 0f));
        Assert.IsTrue(px.Any(c => c.a == 0f), "rounded corners are transparent");
        Assert.IsTrue(px.Any(c => c.r > 0.9f && c.a > 0.9f), "white letters are drawn");
    }

    [Test]
    public void SignMaterial_KeepsReplacedImage()
    {
        TranslatorAssetGenerator.SignMaterial(Dir);
        string png = Dir + "/Sign.png";
        var custom = new Texture2D(4, 4);
        File.WriteAllBytes(png, custom.EncodeToPNG());
        Object.DestroyImmediate(custom);
        AssetDatabase.ImportAsset(png);
        var before = File.ReadAllBytes(png);

        TranslatorAssetGenerator.SignMaterial(Dir);

        CollectionAssert.AreEqual(before, File.ReadAllBytes(png));
    }

    [Test]
    public void Generators_AreIdempotent()
    {
        TranslatorRobotBuilder.CreateMaterials(Dir);
        var sign = TranslatorAssetGenerator.SignMaterial(Dir);
        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sign));
        int count = FileCount();

        TranslatorRobotBuilder.CreateMaterials(Dir);
        var again = TranslatorAssetGenerator.SignMaterial(Dir);

        Assert.AreEqual(count, FileCount());
        Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(again)), "material keeps its GUID");
    }

    /// <summary>Reads the PNG from disk so the test does not depend on the import's read/write flag.</summary>
    static Color[] ReadPixels(Texture2D tex)
    {
        var copy = new Texture2D(2, 2);
        copy.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(tex)));
        var px = copy.GetPixels();
        Object.DestroyImmediate(copy);
        return px;
    }
}
