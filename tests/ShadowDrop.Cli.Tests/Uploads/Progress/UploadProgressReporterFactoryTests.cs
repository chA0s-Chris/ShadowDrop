// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Uploads.Progress;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Terminals;
using ShadowDrop.Cli.Uploads.Progress;
using ShadowDrop.Tests.Fakes;

[TestFixture]
public sealed class UploadProgressReporterFactoryTests
{
    [Test]
    public void Create_ShouldSuppressProgress_WhenJsonIsRequested()
    {
        var factory = new UploadProgressReporterFactory(new StringWriter(), TimeProvider.System, FixedTerminalCapabilityProvider.Plain);

        var reporter = factory.Create(true);

        reporter.Should().BeSameAs(NullUploadProgressReporter.Instance);
    }

    [Test]
    public void Create_ShouldUsePlainReporter_WhenStandardErrorIsRedirected()
    {
        var factory = new UploadProgressReporterFactory(new StringWriter(), TimeProvider.System, FixedTerminalCapabilityProvider.Plain);

        var reporter = factory.Create(false);

        reporter.Should().BeOfType<PlainTextUploadProgressReporter>();
    }

    [Test]
    public void Create_ShouldUseRichReporter_WhenStandardErrorSupportsRichOutput()
    {
        var capabilities = new TerminalCapabilities(IsRedirected: false, IsCiEnvironment: false, SupportsRichOutput: true);
        var factory = new UploadProgressReporterFactory(new StringWriter(), TimeProvider.System, new FixedTerminalCapabilityProvider(capabilities));

        var reporter = factory.Create(false);

        reporter.Should().BeOfType<SpectreUploadProgressReporter>();
    }
}
