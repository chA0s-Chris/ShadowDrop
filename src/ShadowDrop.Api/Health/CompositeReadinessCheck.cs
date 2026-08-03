// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Health;

using ShadowDrop.Contracts;

internal sealed class CompositeReadinessCheck(IEnumerable<IOperationalDependencyProbe> probes) : IReadinessCheck
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    internal TimeSpan Timeout { get; init; } = DefaultTimeout;

    private static async Task<IReadOnlyList<OperationalComponentSnapshot>> ProbeAsync(
        IOperationalDependencyProbe probe,
        CancellationToken deadline,
        CancellationToken callerCancellation)
    {
        var state = OperationalComponentStates.Ready;
        var reason = OperationalStatusReasons.None;
        try
        {
            await probe.ProbeAsync(deadline);
        }
        catch (OperationCanceledException) when (!callerCancellation.IsCancellationRequested)
        {
            state = OperationalComponentStates.NotReady;
            reason = OperationalStatusReasons.DependencyTimeout;
        }
        catch (Exception) when (!callerCancellation.IsCancellationRequested)
        {
            state = OperationalComponentStates.NotReady;
            reason = OperationalStatusReasons.DependencyUnavailable;
        }

        callerCancellation.ThrowIfCancellationRequested();
        return [.. probe.Components.Select(component => new OperationalComponentSnapshot(component, state, reason))];
    }

    public async Task<OperationalReadinessSnapshot> GetStatusAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);

        var results = await Task.WhenAll(probes.Select(probe => ProbeAsync(probe, deadline.Token, cancellationToken)));
        var components = results.SelectMany(static result => result).OrderBy(static component => component.Name, StringComparer.Ordinal).ToArray();
        var ready = components.All(component => component.State == OperationalComponentStates.Ready);
        return new(ready, OperationalReadinessSnapshot.SelectReason(components), components);
    }

    public async Task<Boolean> IsReadyAsync(CancellationToken cancellationToken) =>
        (await GetStatusAsync(cancellationToken)).Ready;
}
