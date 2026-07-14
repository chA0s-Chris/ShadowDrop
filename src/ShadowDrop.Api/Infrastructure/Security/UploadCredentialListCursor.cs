// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

using System.Buffers.Text;
using System.Globalization;
using System.Text;

/// <summary>
/// Opaque continuation point for newest-first credential listing: the creation timestamp plus the management
/// id of the last returned credential. Contains no selector or secret material.
/// </summary>
public sealed record UploadCredentialListCursor(Int64 CreatedAtUnixTimeMilliseconds, Guid CredentialId)
{
    public static Boolean TryDecode(String? encoded, out UploadCredentialListCursor? cursor)
    {
        cursor = null;
        if (String.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        if (!Base64Url.IsValid(encoded))
        {
            return false;
        }

        var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(encoded)).Split(':');
        if (parts.Length != 2
            || !Int64.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var createdAtMilliseconds)
            || !Guid.TryParseExact(parts[1], "N", out var credentialId))
        {
            return false;
        }

        cursor = new(createdAtMilliseconds, credentialId);
        return true;
    }

    public String Encode() =>
        Base64Url.EncodeToString(
            Encoding.UTF8.GetBytes($"{CreatedAtUnixTimeMilliseconds.ToString(CultureInfo.InvariantCulture)}:{CredentialId:N}"));
}
