// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Contracts;

using System.Buffers.Text;
using System.Globalization;
using System.Text;

/// <summary>Stable share-list lifecycle status values in their wire-order.</summary>
public static class ShareListStatuses
{
    public const String Active = "active";
    public const String CleanupCompleted = "cleanup-completed";
    public const String CleanupFailed = "cleanup-failed";
    public const String CleanupPending = "cleanup-pending";
    public const String Expired = "expired";
    public const String Revoked = "revoked";

    /// <summary>Gets every supported status in canonical order.</summary>
    public static IReadOnlyList<String> CanonicalOrder { get; } =
    [
        Active,
        Expired,
        Revoked,
        CleanupPending,
        CleanupFailed,
        CleanupCompleted
    ];
}

/// <summary>Stable normalized cleanup-state values.</summary>
public static class ShareListCleanupStates
{
    public const String Completed = "completed";
    public const String Failed = "failed";
    public const String Pending = "pending";
}

/// <summary>Stable sanitized cleanup failure categories in their wire-order.</summary>
public static class ShareCleanupFailureCategories
{
    public const String BlobDeleteFailed = "blob-delete-failed";
    public const String MetadataUnavailable = "metadata-unavailable";
    public const String Unknown = "unknown";
    public const String UploadMetadataMissing = "upload-metadata-missing";

    /// <summary>Gets every supported category in canonical order.</summary>
    public static IReadOnlyList<String> CanonicalOrder { get; } =
    [
        UploadMetadataMissing,
        MetadataUnavailable,
        BlobDeleteFailed,
        Unknown
    ];
}

/// <summary>Stable share-list pagination limits.</summary>
public static class ShareListPagination
{
    public const Int32 DefaultPageSize = 50;
    public const Int32 MaximumPageSize = 200;
}

/// <summary>Stable operational error reasons used by the administrative share-list endpoint.</summary>
public static class OperationalErrorReasons
{
    public const String InvalidCursor = "invalid-cursor";
    public const String InvalidRequest = "invalid-request";
    public const String OperationFailed = "operation-failed";
    public const String Unauthorized = "unauthorized";
}

/// <summary>Contains one allow-listed administrative share summary.</summary>
public sealed record ShareListItemContract(
    String ShareId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc,
    String[] Statuses,
    String CleanupState,
    DateTimeOffset? LastCleanupAttemptAtUtc,
    String[] CleanupFailureCategories,
    Int64 FileCount,
    Int64 CiphertextBytes);

/// <summary>Contains one page from the operational-protocol-v1 share listing.</summary>
public sealed record ShareListPageContract(
    Int32 ProtocolVersion,
    ShareListItemContract[] Items,
    String? NextCursor,
    Int64 TotalMatching);

/// <summary>Contains a stable reason for an operational request failure.</summary>
public sealed record OperationalErrorContract(String Reason);

/// <summary>
/// Represents the opaque, query-bound continuation position used by administrative share listing.
/// </summary>
public sealed record ShareListCursor(
    Int32 ProtocolVersion,
    String[] Statuses,
    Int64 CreatedAtUnixTimeMilliseconds,
    Guid ShareId)
{
    /// <summary>Attempts to decode a protocol-v1 share-list cursor.</summary>
    public static Boolean TryDecode(String? encoded, out ShareListCursor? cursor)
    {
        cursor = null;
        if (String.IsNullOrWhiteSpace(encoded) || !Base64Url.IsValid(encoded))
        {
            return false;
        }

        var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(encoded)).Split('|');
        if (parts.Length != 4
            || !Int32.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var protocolVersion)
            || protocolVersion != OperationalStatusProtocol.CurrentVersion
            || !Int64.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var createdAtMilliseconds)
            || createdAtMilliseconds < DateTimeOffset.MinValue.ToUnixTimeMilliseconds()
            || createdAtMilliseconds > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()
            || !Guid.TryParseExact(parts[3], "D", out var shareId)
            || !String.Equals(parts[3], shareId.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant(), StringComparison.Ordinal)
            || shareId == Guid.Empty)
        {
            return false;
        }

        var statuses = parts[1].Length == 0 ? [] : parts[1].Split(',');
        if (!IsCanonicalStatusSet(statuses))
        {
            return false;
        }

        cursor = new(protocolVersion, statuses, createdAtMilliseconds, shareId);
        return true;
    }

    /// <summary>Encodes this cursor as an opaque URL-safe value.</summary>
    public String Encode()
    {
        var filters = String.Join(',', Statuses);
        var payload = String.Join('|',
                                  ProtocolVersion.ToString(CultureInfo.InvariantCulture),
                                  filters,
                                  CreatedAtUnixTimeMilliseconds.ToString(CultureInfo.InvariantCulture),
                                  ShareId.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant());
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
    }

    private static Boolean IsCanonicalStatusSet(IReadOnlyList<String> statuses)
    {
        var canonicalIndex = -1;
        foreach (var status in statuses)
        {
            var index = -1;
            for (var candidateIndex = 0; candidateIndex < ShareListStatuses.CanonicalOrder.Count; candidateIndex++)
            {
                if (String.Equals(ShareListStatuses.CanonicalOrder[candidateIndex], status, StringComparison.Ordinal))
                {
                    index = candidateIndex;
                    break;
                }
            }

            if (index <= canonicalIndex)
            {
                return false;
            }

            canonicalIndex = index;
        }

        return true;
    }
}
