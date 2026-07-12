// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Updates;

using ShadowDrop.Cli.Configuration;
using System.Text.Json;

/// <summary>
/// Queries the official GitHub releases API for the latest stable ShadowDrop release. The
/// <c>releases/latest</c> endpoint already excludes drafts and prereleases; both flags are still validated
/// so an unexpected payload is treated as a failed check rather than advertised to the user.
/// </summary>
internal sealed class GitHubUpdateReleaseClient : IUpdateReleaseClient
{
    /// <summary>
    /// The per-request deadline. Update checks are a courtesy, never worth a long wait: the automatic check
    /// must stay unobtrusive and the explicit command should fail fast with a diagnostic.
    /// </summary>
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private const String LatestReleaseUrl = "https://api.github.com/repos/chA0s-Chris/ShadowDrop/releases/latest";
    private const String ReleasesPageUrl = "https://github.com/chA0s-Chris/ShadowDrop/releases";

    private readonly Lazy<HttpClient> _httpClient;
    private readonly TimeProvider _timeProvider;

    public GitHubUpdateReleaseClient()
        : this(new Lazy<HttpClient>(static () => new()), TimeProvider.System) { }

    public GitHubUpdateReleaseClient(HttpClient httpClient, TimeProvider? timeProvider = null)
        : this(new Lazy<HttpClient>(() => httpClient), timeProvider ?? TimeProvider.System) { }

    private GitHubUpdateReleaseClient(Lazy<HttpClient> httpClient, TimeProvider timeProvider)
    {
        _httpClient = httpClient;
        _timeProvider = timeProvider;
    }

    public async Task<CliSemanticVersion> GetLatestStableVersionAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(RequestTimeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", $"ShadowDrop-CLI/{CliVersion.Current}");
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");

            using var response = await _httpClient.Value.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateCheckException(
                    $"The release service responded with HTTP {(Int32)response.StatusCode}. Try again later or check {ReleasesPageUrl} manually.");
            }

            var release = await JsonSerializer.DeserializeAsync(await response.Content.ReadAsStreamAsync(linked.Token),
                                                                CliJsonSerializerContext.Default.GitHubReleaseContract,
                                                                linked.Token);
            if (release is null || release.Draft || release.Prerelease || !CliSemanticVersion.TryParse(release.TagName, out var version))
            {
                throw new UpdateCheckException(
                    $"The release service returned unexpected release information. Check {ReleasesPageUrl} manually for the latest release.");
            }

            return version;
        }
        catch (OperationCanceledException exception) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new UpdateCheckException(
                $"The update check timed out after {RequestTimeout.TotalSeconds:0} seconds. Check your network connection or visit {ReleasesPageUrl} manually.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new UpdateCheckException(
                $"The update check could not reach the release service: {exception.Message} Check your network connection or visit {ReleasesPageUrl} manually.",
                exception);
        }
        catch (JsonException exception)
        {
            throw new UpdateCheckException(
                $"The release service returned a malformed response. Check {ReleasesPageUrl} manually for the latest release.",
                exception);
        }
    }
}
