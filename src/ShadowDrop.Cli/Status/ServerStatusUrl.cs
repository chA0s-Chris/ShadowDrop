// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Status;

internal static class ServerStatusUrl
{
    public static String? GetSafeDisplayValue(String? value) =>
        TryCreate(value, out var uri) ? uri.ToString() : null;

    public static Boolean TryCreate(String? value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            && String.IsNullOrEmpty(parsed.UserInfo)
            && String.IsNullOrEmpty(parsed.Query)
            && String.IsNullOrEmpty(parsed.Fragment))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }
}
