using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class FireSpriteGeneratorTests
{
    const string Dir = "Assets/natsuki_veer02/Fire/_test";

    Texture2D sheet;
    Color32[] pixels;

    static Color32[] Decode(string path)
    {
        var t = new Texture2D(2, 2);
        t.LoadImage(File.ReadAllBytes(path));
        return t.GetPixels32();
    }

    [SetUp]
    public void SetUp()
    {
        AssetDatabase.DeleteAsset(Dir);
        Directory.CreateDirectory(Dir);
        sheet = FireSpriteGenerator.Generate(Dir);
        pixels = Decode(Path.Combine(Dir, "FireSheet.png"));
    }

    [TearDown]
    public void TearDown()
    {
        AssetDatabase.DeleteAsset(Dir);
    }

    [Test]
    public void Generate_WritesFlipbookGrid()
    {
        Assert.AreEqual(FireConfig.SpriteSize * FireConfig.SpriteTilesX, sheet.width);
        Assert.AreEqual(FireConfig.SpriteSize * FireConfig.SpriteTilesY, sheet.height);
    }

    [Test]
    public void Generate_HasSoftEdges()
    {
        int soft = pixels.Count(p => p.a > 0 && p.a < 255);
        Assert.Greater(soft, 1000, "realistic flames need feathered alpha, not hard cut-outs");
    }

    [Test]
    public void Generate_HasBrightCoreAndVisibleBody()
    {
        Assert.GreaterOrEqual(pixels.Max(p => p.a), 230, "flame core must be near full intensity");
        int visible = pixels.Count(p => p.a >= 128);
        Assert.Greater(visible, pixels.Length / 20, "flame tongues must cover a meaningful part of each frame");
    }

    [Test]
    public void Generate_IsGrayscaleSoColourComesFromTheParticle()
    {
        Assert.IsTrue(pixels.All(p => p.r == p.g && p.g == p.b));
    }

    [Test]
    public void Generate_FramesDiffer()
    {
        int s = FireConfig.SpriteSize;
        int w = sheet.width;
        bool differ = false;
        for (int y = 0; y < s && !differ; y++)
            for (int x = 0; x < s; x++)
                if (!pixels[y * w + x].Equals(pixels[y * w + s + x])) { differ = true; break; }
        Assert.IsTrue(differ, "neighbouring frames are identical");
    }

    [Test]
    public void GenerateEmber_IsSoftDotBrightInTheCentre()
    {
        var ember = FireSpriteGenerator.GenerateEmber(Dir);
        Assert.AreEqual(FireConfig.EmberSize, ember.width);
        var px = Decode(Path.Combine(Dir, "Ember.png"));
        int s = FireConfig.EmberSize;
        Assert.Greater(px[(s / 2) * s + s / 2].a, 200);
        Assert.AreEqual(0, px[0].a);
    }
}
