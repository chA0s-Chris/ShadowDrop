// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using ShadowDrop.Cli.Downloads;
using ShadowDrop.Contracts;
using System.Security.Cryptography;

internal static class DirectHttpDownloadUrlFactory
{
    public static String Create(Uri serverUrl, String shareToken, Guid fileId, String shareSecretHex)
    {
        var keyMaterial = Convert.FromHexString(shareSecretHex);
        try
        {
            var keyMaterialBase64 = Convert.ToBase64String(keyMaterial);
            var fileUri = ShareDownloadUriFactory.CreateFileUri(serverUrl, shareToken, fileId);
            return $"{fileUri.AbsoluteUri}?{DownloadKeyConstants.QueryParameterName}={Uri.EscapeDataString(keyMaterialBase64)}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyMaterial);
        }
    }
}
