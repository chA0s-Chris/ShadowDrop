// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests;

using NUnit.Framework;
using ShadowDrop.Tests.Infrastructure;

/// <summary>
/// Builds the API and CLI artifacts once before any smoke scenario runs, and disposes them afterwards. This
/// lives in the assembly root namespace (<c>ShadowDrop.Tests</c>) on purpose: an NUnit <see cref="SetUpFixtureAttribute"/>
/// only applies to tests in its own namespace and sub-namespaces, and the smoke tests sit at the project
/// root, so the fixture must sit there too (not under <c>Infrastructure</c>) to run for them. Because the
/// only tests in this assembly are <c>[Category("E2E")]</c>, this one-time build is skipped entirely when the
/// default test target filters the category out.
/// </summary>
[SetUpFixture]
public sealed class ProductArtifactsFixture
{
    private static ProductArtifacts? _artifacts;

    internal static ProductArtifacts Artifacts =>
        _artifacts ?? throw new InvalidOperationException("The product artifacts have not been built. Did the E2E one-time setup run?");

    [OneTimeSetUp]
    public async Task BuildArtifactsAsync() => _artifacts = await ProductArtifacts.BuildAsync(CancellationToken.None);

    [OneTimeTearDown]
    public void DisposeArtifacts()
    {
        _artifacts?.Dispose();
        _artifacts = null;
    }
}
