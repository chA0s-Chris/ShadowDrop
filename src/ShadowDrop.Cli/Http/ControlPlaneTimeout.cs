// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Http;

/// <summary>
/// Applies the fixed 100-second total deadline for non-streaming control-plane requests, replacing the
/// implicit <see cref="HttpClient.Timeout"/> that is disabled on the shared CLI client so streaming
/// transfers can run for any duration.
/// </summary>
internal sealed class ControlPlaneTimeout : IDisposable
{
    internal static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(100);

    private readonly CancellationTokenSource _deadlineCancellation;
    private readonly CancellationTokenSource _linkedCancellation;

    public ControlPlaneTimeout(CancellationToken callerCancellation, TimeProvider? timeProvider = null)
    {
        _deadlineCancellation = new(TotalTimeout, timeProvider ?? TimeProvider.System);
        _linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(callerCancellation, _deadlineCancellation.Token);
    }

    public CancellationToken Token => _linkedCancellation.Token;

    public void Dispose()
    {
        _linkedCancellation.Dispose();
        _deadlineCancellation.Dispose();
    }
}
