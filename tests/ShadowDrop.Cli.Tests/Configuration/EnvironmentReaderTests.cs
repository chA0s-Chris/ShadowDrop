// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Configuration;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Configuration;

[NonParallelizable]
public sealed class EnvironmentReaderTests
{
    [Test]
    public void GetEnvironmentVariable_ShouldReturnNull_WhenVariableIsNotSet()
    {
        var reader = new EnvironmentReader();

        reader.GetEnvironmentVariable($"SHADOWDROP_TEST_{Guid.NewGuid():N}").Should().BeNull();
    }

    [Test]
    public void GetEnvironmentVariable_ShouldReturnValue_WhenVariableIsSet()
    {
        var name = $"SHADOWDROP_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(name, "expected-value");
        try
        {
            var reader = new EnvironmentReader();

            reader.GetEnvironmentVariable(name).Should().Be("expected-value");
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
