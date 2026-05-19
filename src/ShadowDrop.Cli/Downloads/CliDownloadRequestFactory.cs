// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads;

using ShadowDrop.Contracts;

/// <summary>
/// Builds HTTP requests for streamed CLI downloads.
/// </summary>
public static class CliDownloadRequestFactory
{
    /// <summary>
    /// Creates a GET request that explicitly negotiates the CLI download contract.
    /// </summary>
    /// <param name="downloadUri">The public download URI.</param>
    /// <param name="requestedRange">The optional plaintext range to request.</param>
    /// <returns>The configured HTTP request.</returns>
    public static HttpRequestMessage CreateGetRequest(Uri downloadUri, RequestedPlaintextRangeContract? requestedRange = null)
    {
        ArgumentNullException.ThrowIfNull(downloadUri);

        var request = new HttpRequestMessage(HttpMethod.Get, AppendCliMode(downloadUri));
        if (requestedRange is not null)
        {
            if (requestedRange.Start < 0 || requestedRange.End <= requestedRange.Start)
            {
                throw new ArgumentOutOfRangeException(nameof(requestedRange), "The requested plaintext range must be non-empty and non-negative.");
            }

            request.Headers.Range = new(requestedRange.Start, checked(requestedRange.End - 1));
        }

        return request;
    }

    private static Uri AppendCliMode(Uri downloadUri)
    {
        var builder = new UriBuilder(downloadUri);
        var normalizedParameters = builder.Query
                                          .TrimStart('?')
                                          .Split('&', StringSplitOptions.RemoveEmptyEntries)
                                          .Where(static parameter => !IsModeParameter(parameter))
                                          .Concat([$"{DownloadHeaderConstants.ModeQueryParameterName}={DownloadHeaderConstants.StreamedCliMode}"]);
        builder.Query = String.Join("&", normalizedParameters);
        return builder.Uri;
    }

    private static Boolean IsModeParameter(String parameter)
    {
        var separatorIndex = parameter.IndexOf('=');
        var parameterName = separatorIndex >= 0
            ? parameter[..separatorIndex]
            : parameter;
        return String.Equals(parameterName, DownloadHeaderConstants.ModeQueryParameterName, StringComparison.OrdinalIgnoreCase);
    }
}
