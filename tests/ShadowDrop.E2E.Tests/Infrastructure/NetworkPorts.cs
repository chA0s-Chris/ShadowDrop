// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Infrastructure;

using System.Net;
using System.Net.Sockets;

/// <summary>
/// Allocates a free loopback TCP port so the API can bind a dynamic address instead of a fixed port that
/// might collide with other processes or with a parallel test run.
/// </summary>
internal static class NetworkPorts
{
    public static Int32 FindFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
