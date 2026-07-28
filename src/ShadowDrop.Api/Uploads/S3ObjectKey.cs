// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

internal static class S3ObjectKey
{
    public static String Build(Guid fileId, String keyPrefix) => Build(fileId.ToString("N"), keyPrefix);

    public static String Build(String blobKey, String keyPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobKey);
        if (blobKey.Length != 32 || !Guid.TryParseExact(blobKey, "N", out _))
        {
            throw new ArgumentException("The S3 blob key is malformed.", nameof(blobKey));
        }

        return String.IsNullOrEmpty(keyPrefix) ? blobKey : $"{keyPrefix}/{blobKey}";
    }

    public static String NormalizePrefix(String? keyPrefix)
    {
        if (String.IsNullOrWhiteSpace(keyPrefix))
        {
            return String.Empty;
        }

        var normalized = keyPrefix.Trim().Trim('/');
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException(
                "The configuration value 'ShadowDrop:Storage:S3:KeyPrefix' must contain characters other than '/'.");
        }

        return normalized;
    }
}
