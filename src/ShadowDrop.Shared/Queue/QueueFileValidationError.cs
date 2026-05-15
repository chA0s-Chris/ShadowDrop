// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Queue;

/// <summary>
/// Describes a queue file validation error.
/// </summary>
/// <param name="Path">The logical property path that failed validation.</param>
/// <param name="Message">The user-displayable validation message.</param>
public sealed record QueueFileValidationError(String Path, String Message);
