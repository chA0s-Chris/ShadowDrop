// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Health;

using ShadowDrop.Contracts;

internal sealed record OperationalComponentSnapshot(String Name, String State, String Reason);

internal sealed record OperationalReadinessSnapshot(
    Boolean Ready,
    String Reason,
    IReadOnlyList<OperationalComponentSnapshot> Components)
{
    public static String SelectReason(IEnumerable<OperationalComponentSnapshot> components)
    {
        var reasons = components.Select(component => component.Reason).ToArray();
        if (reasons.Contains(OperationalStatusReasons.DependencyTimeout, StringComparer.Ordinal))
        {
            return OperationalStatusReasons.DependencyTimeout;
        }

        return reasons.Contains(OperationalStatusReasons.DependencyUnavailable, StringComparer.Ordinal)
            ? OperationalStatusReasons.DependencyUnavailable
            : OperationalStatusReasons.None;
    }

    public OperationalReadinessSnapshot WithComponentFailure(String componentName, String reason)
    {
        var components = Components
                         .Select(component => String.Equals(component.Name, componentName, StringComparison.Ordinal)
                                     ? component with
                                     {
                                         State = OperationalComponentStates.NotReady,
                                         Reason = reason
                                     }
                                     : component)
                         .ToArray();
        return new(false, SelectReason(components), components);
    }
}
