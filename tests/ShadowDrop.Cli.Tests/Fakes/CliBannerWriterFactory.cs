// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Fakes;

using ShadowDrop.Cli;

/// <summary>
/// Shared suppressed <see cref="CliBannerWriter"/> for handler tests that are not about banner behavior, so their
/// existing exact-match assertions on stdout/stderr keep holding.
/// </summary>
internal static class CliBannerWriterFactory
{
    public static CliBannerWriter Suppressed { get; } = new(true, default, default);
}
