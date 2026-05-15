// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Queue;

/// <summary>
/// Represents a queue file validation failure.
/// </summary>
public sealed class QueueFileValidationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QueueFileValidationException"/> class.
    /// </summary>
    /// <param name="errors">The validation errors.</param>
    public QueueFileValidationException(IReadOnlyList<QueueFileValidationError> errors)
        : base("The queue file is invalid.")
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = errors;
    }

    /// <summary>
    /// Gets the validation errors.
    /// </summary>
    public IReadOnlyList<QueueFileValidationError> Errors { get; }
}
