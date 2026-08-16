using ChatApp.Core.Models;
using ChatApp.UI.ViewModels;
using ChatApp.UI.Services;

namespace ChatApp.Tests;

public sealed class RoleAvatarMatchingTests
{
    [Fact]
    public void ExactCharacterNameOutranksGenericHigherVectorScore()
    {
        var candidates = new[]
        {
            new KnowledgeImageHit
            {
                DocumentId = 1,
                Title = "通用罗德岛立绘",
                FileName = "generic.png",
                Description = "一名角色",
                Score = 0.91
            },
            new KnowledgeImageHit
            {
                DocumentId = 2,
                Title = "阿米娅",
                FileName = "阿米娅_精英二.png",
                Description = "阿米娅的正面立绘",
                Tags = "阿米娅,立绘",
                Score = 0.56
            }
        };

        var selected = CreateRoleViewModel.SelectBestAvatarCandidate("阿米娅", candidates);

        Assert.NotNull(selected);
        Assert.Equal(2, selected!.DocumentId);
    }

    [Fact]
    public void AvatarQueryIncludesRoleIdentityAndIsBounded()
    {
        var query = CreateRoleViewModel.BuildAvatarQuery(
            "凯尔希",
            new string('简', 2500),
            "罗德岛",
            "冷静");

        Assert.Contains("凯尔希", query);
        Assert.Contains("头像、肖像或立绘", query);
        Assert.True(query.Length <= 1800);
    }

    [Fact]
    public void BundledFolderMatchFindsExactRoleDirectoryWithoutVectorSearch()
    {
        var root = Path.Combine(Path.GetTempPath(), $"chatapp-avatar-folders-{Guid.NewGuid():N}");
        try
        {
            var direct = Path.Combine(root, "立绘", "K", "凯尔希", "JPEG");
            var variant = Path.Combine(root, "立绘", "K", "凯尔希", "凯尔希·思衡托", "JPEG");
            Directory.CreateDirectory(direct);
            Directory.CreateDirectory(variant);
            File.WriteAllBytes(Path.Combine(direct, "立绘-凯尔希-精一.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(direct, "立绘-凯尔希-精二.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(variant, "立绘-凯尔希-凯尔希·思衡托-精二.png"), new byte[] { 1 });

            var candidate = BundledKnowledgeService.FindBestRoleAvatarFile(root, " 凯尔希 ");

            Assert.NotNull(candidate);
            Assert.Equal("立绘-凯尔希-精二", candidate!.Title);
            Assert.Contains("立绘/K/凯尔希/JPEG", candidate.RelativePath);
            Assert.Null(BundledKnowledgeService.FindBestRoleAvatarFile(root, "不存在的角色"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ShippedCorpusFindsKaltSitEliteTwoIllustration()
    {
        var bundle = Path.Combine(AppContext.BaseDirectory, "BundledKnowledge");

        var candidate = BundledKnowledgeService.FindBestRoleAvatarFile(bundle, "凯尔希");

        Assert.NotNull(candidate);
        Assert.EndsWith("立绘/K/凯尔希/JPEG/立绘-凯尔希-精二.png", candidate!.RelativePath);
    }
}
