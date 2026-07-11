// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

internal sealed class UploadAttemptTimeout : IDisposable
{
    internal static readonly TimeSpan InactivityTimeout = TimeSpan.FromMinutes(10);

    private readonly CancellationTokenSource _inactivityCancellation;
    private readonly CancellationTokenSource _linkedCancellation;

    public UploadAttemptTimeout(CancellationToken callerCancellation, TimeProvider timeProvider)
    {
        _inactivityCancellation = new(InactivityTimeout, timeProvider);
        _linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(callerCancellation, _inactivityCancellation.Token);
    }

    public CancellationToken Token => _linkedCancellation.Token;

    public void Reset() => _inactivityCancellation.CancelAfter(InactivityTimeout);

    public void Dispose()
    {
        _linkedCancellation.Dispose();
        _inactivityCancellation.Dispose();
    }
}
