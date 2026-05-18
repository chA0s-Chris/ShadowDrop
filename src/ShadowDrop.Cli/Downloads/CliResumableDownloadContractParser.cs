// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads;

using ShadowDrop.Contracts;
using System.Text.Json;

/// <summary>
/// Parses and validates CLI resumable-download responses.
/// </summary>
public static class CliResumableDownloadContractParser
{
    /// <summary>
    /// Parses and validates a CLI resumable-download response payload.
    /// </summary>
    /// <param name="json">The response JSON.</param>
    /// <returns>The validated contract.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="json"/> is empty.</exception>
    /// <exception cref="InvalidDataException">Thrown when the payload is missing required resumable-download metadata.</exception>
    public static CliResumableDownloadContract Parse(String json)
    {
        if (String.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("The resumable download payload must not be empty.", nameof(json));
        }

        var contract = JsonSerializer.Deserialize(json, ContractsJsonSerializerContext.Default.CliResumableDownloadContract)
                       ?? throw new InvalidDataException("The resumable download payload could not be parsed.");

        Validate(contract);
        return contract;
    }

    private static void Validate(CliResumableDownloadContract contract)
    {
        if (contract.RequestedRange is null)
        {
            throw new InvalidDataException("The resumable download payload is missing the requested plaintext range.");
        }

        if (String.IsNullOrWhiteSpace(contract.EncryptedPayload))
        {
            throw new InvalidDataException("The resumable download payload is missing encrypted chunk data.");
        }

        if (contract.FirstChunkIndex < 0 || contract.LastChunkIndex < contract.FirstChunkIndex)
        {
            throw new InvalidDataException("The resumable download payload contains an invalid chunk span.");
        }

        if (contract.RequestedRange.Start < 0
            || contract.RequestedRange.End <= contract.RequestedRange.Start
            || contract.RequestedRange.End > contract.TotalPlaintextSize)
        {
            throw new InvalidDataException("The resumable download payload contains an invalid plaintext range.");
        }

        if (contract.ChunkSize <= 0)
        {
            throw new InvalidDataException("The resumable download payload contains an invalid chunk size.");
        }

        if (contract.FinalChunkPlaintextLength <= 0 || contract.FinalChunkPlaintextLength > contract.ChunkSize)
        {
            throw new InvalidDataException("The resumable download payload contains an invalid final chunk length.");
        }

        try
        {
            Convert.FromBase64String(contract.EncryptedPayload);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The resumable download payload contains invalid encrypted chunk data.", exception);
        }
    }
}
