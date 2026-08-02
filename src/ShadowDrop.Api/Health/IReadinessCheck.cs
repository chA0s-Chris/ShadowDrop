// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Health;

using ShadowDrop.Contracts;

internal interface IReadinessCheck
{
    async Task<OperationalReadinessSnapshot> GetStatusAsync(CancellationToken cancellationToken)
    {
        var ready = await IsReadyAsync(cancellationToken);
        return new(ready,
                   ready ? OperationalStatusReasons.None : OperationalStatusReasons.DependencyUnavailable,
                   []);
    }

    Task<Boolean> IsReadyAsync(CancellationToken cancellationToken);
}
