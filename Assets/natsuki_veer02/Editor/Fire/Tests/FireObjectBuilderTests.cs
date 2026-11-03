using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class FireObjectBuilderTests
{
    const string Dir = "Assets/natsuki_veer02/Fire/_test_objects";

    Texture2D sheet;
    Texture2D ember;
    GameObject avatar;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        AssetDatabase.DeleteAsset(Dir);
        Directory.CreateDirectory(Dir);
        sheet = FireSpriteGenerator.Generate(Dir);
        ember = FireSpriteGenerator.GenerateEmber(Dir);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        AssetDatabase.DeleteAsset(Dir);
    }

    [SetUp]
    public void SetUp()
    {
        avatar = TestAvatar.Create();
    }

    [TearDown]
    public void TearDown()
    {
        if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
    }

    int Build() => FireObjectBuilder.Build(avatar, sheet, ember, Dir);

    ParticleSystem Site(string name)
    {
        return FireObjectBuilder.FindAll(avatar).First(g => g.name == FireConfig.SitePrefix + name).GetComponent<ParticleSystem>();
    }

    [Test]
    public void Build_CreatesFlamePerBodyMeshPlusEmbers_AllInactive()
    {
        int n = Build();
        var names = FireObjectBuilder.FindAll(avatar).Select(g => g.name).OrderBy(x => x).ToArray();
        Assert.AreEqual(3, n);
        CollectionAssert.AreEqual(new[] { "Fire_Body", "Fire_Cloth", "Fire_Embers" }, names);
        Assert.IsTrue(FireObjectBuilder.FindAll(avatar).All(g => !g.activeSelf));
    }

    [Test]
    public void Build_DoesNotBurnFaceOrHair()
    {
        Build();
        var meshes = FireObjectBuilder.FindAll(avatar)
            .Select(g => g.GetComponent<ParticleSystem>().shape.skinnedMeshRenderer)
            .Select(r => r.name).ToList();
        CollectionAssert.DoesNotContain(meshes, "Face");
        CollectionAssert.DoesNotContain(meshes, "Hair");
    }

    [Test]
    public void Build_EmitsFromTheSkinnedMeshSurface()
    {
        Build();
        var shape = Site("Body").shape;
        Assert.AreEqual(ParticleSystemShapeType.SkinnedMeshRenderer, shape.shapeType);
        Assert.AreSame(avatar.transform.Find("Body").GetComponent<SkinnedMeshRenderer>(), shape.skinnedMeshRenderer);
    }

    [Test]
    public void Build_ParticleCountsWithinLimits()
    {
        Build();
        var flames = new[] { Site("Body"), Site("Cloth") };
        Assert.LessOrEqual(flames.Sum(p => p.main.maxParticles), FireConfig.FlameBudget);
        Assert.LessOrEqual(Site("Embers").main.maxParticles, FireConfig.EmberMax);
        Assert.LessOrEqual(FireObjectBuilder.FindAll(avatar).Sum(g => g.GetComponent<ParticleSystem>().main.maxParticles), 350);
    }

    [Test]
    public void Build_NoCollisionLightsOrSubEmitters()
    {
        Build();
        foreach (var g in FireObjectBuilder.FindAll(avatar))
        {
            var p = g.GetComponent<ParticleSystem>();
            Assert.IsFalse(p.collision.enabled);
            Assert.IsFalse(p.subEmitters.enabled);
            Assert.IsFalse(p.lights.enabled);
            Assert.AreEqual(0, g.GetComponentsInChildren<Light>(true).Length);
        }
    }

    [Test]
    public void Build_UsesRealisticSettings()
    {
        Build();
        var p = Site("Body");
        Assert.AreEqual(ParticleSystemSimulationSpace.World, p.main.simulationSpace);
        Assert.IsTrue(p.noise.enabled);
        Assert.IsTrue(p.colorOverLifetime.enabled);
        var tsa = p.textureSheetAnimation;
        Assert.IsTrue(tsa.enabled);
        Assert.AreEqual(FireConfig.SpriteTilesX, tsa.numTilesX);
        Assert.AreEqual(FireConfig.SpriteTilesY, tsa.numTilesY);
        var mat = p.GetComponent<ParticleSystemRenderer>().sharedMaterial;
        StringAssert.Contains("Additive", mat.shader.name);
        Assert.AreSame(sheet, mat.mainTexture);
        Assert.AreSame(ember, Site("Embers").GetComponent<ParticleSystemRenderer>().sharedMaterial.mainTexture);
    }

    [Test]
    public void Build_IsIdempotent()
    {
        Build();
        Build();
        Assert.AreEqual(3, FireObjectBuilder.FindAll(avatar).Count);
    }

    [Test]
    public void Preflight_NoBurnableMesh_Throws()
    {
        UnityEngine.Object.DestroyImmediate(avatar);
        avatar = TestAvatar.Create(includeMeshes: false);
        Assert.Throws<InvalidOperationException>(() => FireObjectBuilder.Preflight(avatar));
    }

    [Test]
    public void PathOf_ReturnsPathRelativeToAvatarRoot()
    {
        Build();
        var body = FireObjectBuilder.FindAll(avatar).First(g => g.name == "Fire_Body");
        Assert.AreEqual("Fire_Body", FireObjectBuilder.PathOf(avatar, body));
    }
}
