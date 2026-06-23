// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Queue;

using ShadowDrop.Contracts;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Provides explicit parsing and validation for ShadowDrop queue files.
/// </summary>
public static partial class QueueFileParser
{
    /// <summary>
    /// Deserializes a queue file JSON payload.
    /// </summary>
    /// <param name="json">The queue file JSON.</param>
    /// <returns>The deserialized queue file.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="json"/> is empty.</exception>
    /// <exception cref="JsonException">Thrown when <paramref name="json"/> is not valid queue JSON.</exception>
    public static QueueFile Deserialize(String json)
    {
        if (String.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("The queue file JSON must not be empty.", nameof(json));
        }

        var queueFile = JsonSerializer.Deserialize(json, QueueJsonSerializerContext.Default.QueueFile);
        return queueFile ?? throw new JsonException("The queue file JSON produced no payload.");
    }

    /// <summary>
    /// Parses and validates a queue file JSON payload.
    /// </summary>
    /// <param name="json">The queue file JSON.</param>
    /// <returns>The validated queue file.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="json"/> is empty.</exception>
    /// <exception cref="JsonException">Thrown when <paramref name="json"/> is malformed JSON.</exception>
    /// <exception cref="QueueFileValidationException">Thrown when the queue file content is invalid.</exception>
    public static QueueFile Parse(String json)
    {
        var queueFile = Deserialize(json);
        var errors = Validate(queueFile);

        return errors.Count > 0 ? throw new QueueFileValidationException(errors) : queueFile;
    }

    /// <summary>
    /// Serializes a queue file to JSON.
    /// </summary>
    /// <param name="queueFile">The queue file to serialize.</param>
    /// <returns>The JSON payload.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="queueFile"/> is <see langword="null"/>.</exception>
    public static String Serialize(QueueFile queueFile)
    {
        ArgumentNullException.ThrowIfNull(queueFile);
        return JsonSerializer.Serialize(queueFile, QueueJsonSerializerContext.Default.QueueFile);
    }

    /// <summary>
    /// Validates a queue file instance.
    /// </summary>
    /// <param name="queueFile">The queue file to validate.</param>
    /// <returns>The list of validation errors.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="queueFile"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<QueueFileValidationError> Validate(QueueFile queueFile)
    {
        ArgumentNullException.ThrowIfNull(queueFile);

        List<QueueFileValidationError> errors = [];

        ValidateRequiredString(queueFile.ShadowDrop, "shadowDrop", errors);
        ValidateRequiredString(queueFile.QueueVersion, "queueVersion", errors);

        if (!String.IsNullOrWhiteSpace(queueFile.ShadowDrop) &&
            !String.Equals(queueFile.ShadowDrop, FormatConstants.ShadowDropVersion, StringComparison.Ordinal))
        {
            errors.Add(new("shadowDrop", $"The shadowDrop value must be '{FormatConstants.ShadowDropVersion}'."));
        }

        if (!String.IsNullOrWhiteSpace(queueFile.QueueVersion) &&
            !String.Equals(queueFile.QueueVersion, FormatConstants.QueueVersion, StringComparison.Ordinal))
        {
            errors.Add(new("queueVersion", $"The queueVersion value must be '{FormatConstants.QueueVersion}'."));
        }

        if (queueFile.Credentials is not null)
        {
            ValidateRequiredString(queueFile.Credentials.ShareKey, "credentials.shareKey", errors);
            ValidateShareKeyFormat(queueFile.Credentials.ShareKey, errors);
        }

        if (queueFile.Files is null || queueFile.Files.Count == 0)
        {
            errors.Add(new("files", "The files collection must contain at least one entry."));
            return errors;
        }

        for (var index = 0; index < queueFile.Files.Count; index++)
        {
            ValidateEntry(queueFile.Files[index], index, errors);
        }

        return errors;
    }

    [GeneratedRegex("[a-f0-9]{64}", RegexOptions.CultureInvariant, -1)]
    private static partial Regex Sha256Regex();

    private static void ValidateEntry(QueueFileEntry? entry, Int32 index, List<QueueFileValidationError> errors)
    {
        var prefix = $"files[{index}]";

        if (entry is null)
        {
            errors.Add(new(prefix, "The file entry is required."));
            return;
        }

        ValidateRequiredString(entry.FileId, $"{prefix}.fileId", errors);
        ValidateRequiredString(entry.FileName, $"{prefix}.fileName", errors);
        ValidateRequiredString(entry.OutputPath, $"{prefix}.outputPath", errors);
        ValidateRequiredString(entry.ShareToken, $"{prefix}.shareToken", errors);
        ValidateServerUrl(entry.ServerUrl, $"{prefix}.serverUrl", errors);

        if (entry.Length is null)
        {
            errors.Add(new($"{prefix}.length", "The length value is required."));
        }
        else if (entry.Length < 0)
        {
            errors.Add(new($"{prefix}.length", "The file length must be zero or greater."));
        }

        ValidateOptionalSha256(entry.PlaintextSha256, $"{prefix}.plaintextSha256", errors);
    }

    private static void ValidateOptionalSha256(String? value, String path, List<QueueFileValidationError> errors)
    {
        if (value is null)
        {
            return;
        }

        var match = Sha256Regex().Match(value);
        if (!match.Success || match.Index != 0 || match.Length != value.Length)
        {
            errors.Add(new(path, "The plaintextSha256 value must be a 64-character lowercase hexadecimal SHA-256 digest."));
        }
    }

    private static void ValidateRequiredString(String? value, String path, List<QueueFileValidationError> errors)
    {
        if (String.IsNullOrWhiteSpace(value))
        {
            errors.Add(new(path, $"The {path.Split('.').Last()} value is required."));
        }
    }

    private static void ValidateServerUrl(String? serverUrl, String path, List<QueueFileValidationError> errors)
    {
        if (String.IsNullOrWhiteSpace(serverUrl))
        {
            errors.Add(new(path, $"The {path.Split('.').Last()} value is required."));
            return;
        }

        var isAbsoluteHttpUrl = Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) &&
                                uri is not null &&
                                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        if (!isAbsoluteHttpUrl)
        {
            errors.Add(new(path, "The serverUrl value must be an absolute HTTP or HTTPS URL."));
            return;
        }

        var validatedUri = uri!;
        if (!String.IsNullOrEmpty(validatedUri.UserInfo))
        {
            errors.Add(new(path, "The serverUrl value must not include user information."));
        }

        if (!String.IsNullOrEmpty(validatedUri.Query) || !String.IsNullOrEmpty(validatedUri.Fragment))
        {
            errors.Add(new(path, "The serverUrl value must not include query string or fragment components."));
        }
    }

    private static void ValidateShareKeyFormat(String? value, List<QueueFileValidationError> errors)
    {
        if (String.IsNullOrWhiteSpace(value))
        {
            return;
        }

        // A share key is 32 bytes of key material encoded as 64 lowercase hex characters (the same shape as the SHA-256 pattern).
        var match = Sha256Regex().Match(value);
        if (!match.Success || match.Index != 0 || match.Length != value.Length)
        {
            errors.Add(new("credentials.shareKey", "The shareKey value must be 64-character lowercase hexadecimal share-key material."));
        }
    }
}
