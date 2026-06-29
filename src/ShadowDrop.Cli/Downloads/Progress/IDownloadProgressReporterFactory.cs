// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads.Progress;

/// <summary>
/// Creates the download progress reporter appropriate for the current environment.
/// </summary>
internal interface IDownloadProgressReporterFactory
{
    /// <summary>
    /// Creates a reporter, selecting rich or plain output based on terminal capabilities.
    /// </summary>
    IDownloadProgressReporter Create();
}
