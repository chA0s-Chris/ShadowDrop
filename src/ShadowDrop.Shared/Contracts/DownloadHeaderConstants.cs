// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Contracts;

/// <summary>
/// Defines stable HTTP header names used by public download responses.
/// </summary>
public static class DownloadHeaderConstants
{
    public const String ChunkSizeHeaderName = "X-ShadowDrop-Chunk-Size";
    public const String CliDownloadContentType = "application/vnd.shadowdrop.cli-download";
    public const String FileContentTypeHeaderName = "X-ShadowDrop-File-Content-Type";
    public const String FileNameHeaderName = "X-ShadowDrop-File-Name";
    public const String FinalChunkPlaintextLengthHeaderName = "X-ShadowDrop-Final-Chunk-Plaintext-Length";
    public const String FirstChunkIndexHeaderName = "X-ShadowDrop-First-Chunk-Index";
    public const String LastChunkIndexHeaderName = "X-ShadowDrop-Last-Chunk-Index";
    public const String ModeHeaderName = "X-ShadowDrop-Download-Mode";
    public const String ModeQueryParameterName = "mode";
    public const String PlaintextRangeEndHeaderName = "X-ShadowDrop-Plaintext-Range-End";
    public const String PlaintextRangeStartHeaderName = "X-ShadowDrop-Plaintext-Range-Start";
    public const String StreamedCliMode = "cli";
    public const String TotalPlaintextSizeHeaderName = "X-ShadowDrop-Total-Plaintext-Size";
}
