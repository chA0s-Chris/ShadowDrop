// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads.Progress;

/// <summary>
/// Creates upload progress reporters for the command output mode.
/// </summary>
internal interface IUploadProgressReporterFactory
{
    IUploadProgressReporter Create(Boolean json);
}
