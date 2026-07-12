// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Fakes;

using FluentAssertions;
using ShadowDrop.Cli.Updates;

/// <summary>
/// Returns a preconfigured latest version or throws a preconfigured failure, and counts requests so tests
/// can assert how often the release source was contacted.
/// </summary>
internal sealed class StubUpdateReleaseClient : IUpdateReleaseClient
{
    private readonly UpdateCheckException? _exception;
    private readonly CliSemanticVersion? _latestVersion;

    public StubUpdateReleaseClient(String latestVersion)
    {
        CliSemanticVersion.TryParse(latestVersion, out var parsed).Should().BeTrue();
        _latestVersion = parsed;
    }

    public StubUpdateReleaseClient(UpdateCheckException exception) => _exception = exception;

    public Int32 RequestCount { get; private set; }

    public Task<CliSemanticVersion> GetLatestStableVersionAsync(CancellationToken cancellationToken)
    {
        RequestCount++;
        return _exception is null ? Task.FromResult(_latestVersion!) : Task.FromException<CliSemanticVersion>(_exception);
    }
}

/// <summary>
/// Holds the update-check record in memory so cache freshness and persistence can be asserted without
/// touching the real cache location.
/// </summary>
internal sealed class InMemoryUpdateCheckCache(UpdateCheckRecord? record = null) : IUpdateCheckCache
{
    public UpdateCheckRecord? Record { get; private set; } = record;
    public Int32 WriteCount { get; private set; }

    public UpdateCheckRecord? Read() => Record;

    public void Write(UpdateCheckRecord record)
    {
        Record = record;
        WriteCount++;
    }
}

internal static class FakeUpdateServices
{
    public static CliUpdateServices Create(IUpdateReleaseClient? releaseClient = null,
                                           IUpdateCheckCache? cache = null,
                                           Boolean isWindows = false,
                                           IReadOnlyDictionary<String, String?>? environment = null,
                                           String? executableDirectory = null) =>
        new(releaseClient ?? new StubUpdateReleaseClient(new UpdateCheckException("The release source must not be contacted in this test.")),
            cache ?? new InMemoryUpdateCheckCache(),
            new InstallationGuidanceProvider(isWindows, executableDirectory),
            environment is null ? new FakeEnvironmentReader() : new FakeEnvironmentReader(environment));
}
