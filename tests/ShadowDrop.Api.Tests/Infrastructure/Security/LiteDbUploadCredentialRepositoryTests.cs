// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Infrastructure.Security;

using NUnit.Framework;
using ShadowDrop.Api.Infrastructure.Security;

public sealed class LiteDbUploadCredentialRepositoryTests
{
    private String _rootDirectory;

    [SetUp]
    public void CreateRootDirectory()
    {
        _rootDirectory = Path.Combine(AppContext.BaseDirectory, "artifacts", $"upload-credentials-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_rootDirectory, "metadata"));
    }

    [TearDown]
    public void DeleteRootDirectory()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, true);
        }
    }

    [Test]
    public async Task Repository_ShouldPassSharedContract()
    {
        using var repository = CreateRepository();

        await UploadCredentialRepositoryContract.AssertContractAsync(repository);
    }

    [Test]
    public async Task Repository_ShouldPassSharedListPaginationContract()
    {
        using var repository = CreateRepository();

        await UploadCredentialRepositoryContract.AssertListPaginationContractAsync(repository);
    }

    private LiteDbUploadCredentialRepository CreateRepository() => new(new()
    {
        Metadata = new()
        {
            LiteDbPath = Path.Combine(_rootDirectory, "metadata", "shadowdrop.db")
        }
    });
}
