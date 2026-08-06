// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Configuration;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using ShadowDrop.Api.Configuration;

[TestFixture]
public sealed class CleanupConfigurationTests
{
    [Test]
    public void BindAndValidate_ShouldBindUnreferencedUploadRetention()
    {
        using var root = new TemporaryDirectory();
        var values = Values(root.Path);
        values["ShadowDrop:Cleanup:UnreferencedUploadRetention"] = "30.12:00:00";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var options = ShadowDropOptionsBinding.BindAndValidate(configuration, root.Path);

        options.Cleanup.UnreferencedUploadRetention.Should().Be(TimeSpan.FromDays(30) + TimeSpan.FromHours(12));
    }

    [Test]
    public void BindAndValidate_ShouldDefaultUnreferencedUploadRetentionToSevenDays()
    {
        using var root = new TemporaryDirectory();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(Values(root.Path)).Build();

        var options = ShadowDropOptionsBinding.BindAndValidate(configuration, root.Path);

        options.Cleanup.UnreferencedUploadRetention.Should().Be(TimeSpan.FromDays(7));
    }

    [TestCase("00:00:00")]
    [TestCase("-1.00:00:00")]
    public void BindAndValidate_ShouldRejectNonPositiveUnreferencedUploadRetention(String retention)
    {
        using var root = new TemporaryDirectory();
        var values = Values(root.Path);
        values["ShadowDrop:Cleanup:UnreferencedUploadRetention"] = retention;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        // ReSharper disable once AccessToDisposedClosure
        var act = () => ShadowDropOptionsBinding.BindAndValidate(configuration, root.Path);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Cleanup:UnreferencedUploadRetention*");
    }

    private static Dictionary<String, String?> Values(String root) => new()
    {
        ["ShadowDrop:Metadata:Provider"] = nameof(MetadataProvider.LiteDb),
        ["ShadowDrop:Metadata:LiteDbPath"] = Path.Combine(root, "metadata", "shadowdrop.db"),
        ["ShadowDrop:Storage:Provider"] = nameof(BlobStorageProvider.FileSystem),
        ["ShadowDrop:Storage:LocalRoot"] = Path.Combine(root, "blobs"),
        ["ShadowDrop:Cleanup:CronExpression"] = "0 * * * *"
    };

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"shadowdrop-cleanup-config-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public String Path { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
