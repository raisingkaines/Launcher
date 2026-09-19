using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using Mono.Nat;

using NLog;

namespace Launcher.Services;

/// <summary>
/// The outcome of trying to open the ports a hosted server needs.
/// </summary>
/// <param name="Mapped">Whether every required port was opened.</param>
/// <param name="ExternalAddress">The address players would connect to, if it could be found.</param>
/// <param name="Reachable">
/// False when the address is one the internet cannot route to, which is the carrier-grade
/// NAT case: the router accepts the mapping and it still will not work.
/// </param>
/// <param name="Notes">What happened, in the order it happened, for showing the host.</param>
public sealed record PortForwardResult(
    bool Mapped,
    string? ExternalAddress,
    bool Reachable,
    IReadOnlyList<string> Notes);

/// <summary>
/// Opens the ports a hosted server needs on the host's router, over UPnP or NAT-PMP.
///
/// Only the two ports players actually connect to are opened. The login/gateway link on
/// 20041 is internal, and the web API on 20040 maps /login and /register - forwarding
/// either of those would put parts of the server on the internet that have no business
/// being there.
/// </summary>
public static class PortForwarding
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>What players connect to, and nothing else.</summary>
    public static readonly (int Port, string Name)[] RequiredPorts =
    [
        (20042, "login"),
        (20260, "gateway")
    ];

    private static INatDevice? _device;
    private static readonly List<Mapping> _mappings = [];

    /// <summary>
    /// Finds the router and opens the ports. Never throws: a host whose router will not
    /// cooperate needs to be told that, not shown a crash.
    /// </summary>
    public static async Task<PortForwardResult> TryOpenAsync(
        TimeSpan discoveryTimeout, CancellationToken cancellationToken = default)
    {
        var notes = new List<string>();

        try
        {
            var devices = await FindDevicesAsync(discoveryTimeout, cancellationToken);

            if (devices.Count == 0)
            {
                notes.Add("No router that supports automatic port forwarding answered. "
                          + "UPnP may be switched off in its settings.");

                return new PortForwardResult(false, null, false, notes);
            }

            notes.Add(devices.Count == 1
                ? "Router found."
                : $"{devices.Count} routers answered; trying each until one accepts.");

            IPAddress? external = null;
            var reachable = false;
            var mapped = false;

            foreach (var device in devices)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _device = device;
                _mappings.Clear();

                var opened = 0;

                foreach (var (port, name) in RequiredPorts)
                {
                    if (await TryMapAsync(device, port, name))
                        opened++;
                }

                if (opened == RequiredPorts.Length)
                {
                    mapped = true;

                    foreach (var (port, name) in RequiredPorts)
                        notes.Add($"Opened UDP {port} ({name}).");

                    external = await TryGetExternalAddressAsync(device);

                    break;
                }

                // This one would not do it; undo whatever it did accept before moving on.
                await CloseAsync();
            }

            if (!mapped)
            {
                notes.Add("No router would open the ports. Forwarding UDP "
                          + string.Join(" and ", RequiredPorts.Select(p => p.Port))
                          + " by hand in the router's settings does the same job.");
            }

            if (external is null)
            {
                if (mapped)
                    notes.Add("The router would not say what its public address is.");
            }
            else
            {
                notes.Add($"Public address: {external}");

                reachable = IsRoutable(external);

                if (!reachable)
                {
                    notes.Add($"{external} is not an address the internet can reach, which usually means "
                              + "the connection is behind carrier-grade NAT. Opening ports cannot help with "
                              + "that - players outside the network will not be able to connect.");
                }
            }

            return new PortForwardResult(mapped, external?.ToString(), reachable, notes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Could not set up port forwarding.");
            notes.Add("Something went wrong talking to the router: " + ex.Message);

            return new PortForwardResult(false, null, false, notes);
        }
    }

    /// <summary>
    /// Closes the ports again. A host who stops hosting should not be left with holes in
    /// their router.
    /// </summary>
    public static async Task CloseAsync()
    {
        var device = _device;

        if (device is null)
            return;

        foreach (var mapping in _mappings.ToArray())
        {
            try
            {
                await device.DeletePortMapAsync(mapping);

                _logger.Info("Closed UDP {Port}.", mapping.PublicPort);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not close UDP {Port}; it will expire on its own.", mapping.PublicPort);
            }
        }

        _mappings.Clear();
        _device = null;
    }

    /// <summary>
    /// Collects every router that answers within the window, rather than taking the first.
    /// More than one commonly replies - a router often answers on both UPnP and NAT-PMP,
    /// and there may be a second device on the network - and the first to speak is not
    /// necessarily the one that will accept a mapping.
    /// </summary>
    private static async Task<IReadOnlyList<INatDevice>> FindDevicesAsync(
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        var devices = new List<INatDevice>();

        void OnDeviceFound(object? sender, DeviceEventArgs e)
        {
            lock (devices)
            {
                if (!devices.Any(d => Equals(d.DeviceEndpoint, e.Device.DeviceEndpoint)))
                    devices.Add(e.Device);
            }
        }

        NatUtility.DeviceFound += OnDeviceFound;

        try
        {
            NatUtility.StartDiscovery();

            await Task.Delay(timeout, cancellationToken);
        }
        finally
        {
            NatUtility.DeviceFound -= OnDeviceFound;
            NatUtility.StopDiscovery();
        }

        lock (devices)
        {
            return devices.ToArray();
        }
    }

    private static async Task<IPAddress?> TryGetExternalAddressAsync(INatDevice device)
    {
        try
        {
            return await device.GetExternalIPAsync();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Could not read the router's public address.");

            return null;
        }
    }

    private static async Task<bool> TryMapAsync(INatDevice device, int port, string name)
    {
        try
        {
            // Same port inside and out, so the address a host shares is the one in their
            // own config. Lifetime 0 asks for it to stand until deleted; routers that
            // refuse that hand back one of their own, which the library honours.
            var mapping = new Mapping(Protocol.Udp, port, port, 0, $"Free Realms Legacy ({name})");

            await device.CreatePortMapAsync(mapping);

            _mappings.Add(mapping);

            return true;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Could not open UDP {Port}.", port);

            return false;
        }
    }

    /// <summary>
    /// Whether an address is one the wider internet can route to. A private or
    /// carrier-grade NAT address means the router is itself behind another one, and no
    /// amount of forwarding on this router will let anyone in.
    /// </summary>
    private static bool IsRoutable(IPAddress address)
    {
        var b = address.GetAddressBytes();

        if (b.Length != 4)
            return true;

        // 10/8, 172.16/12, 192.168/16 - private. 100.64/10 - carrier-grade NAT.
        // 169.254/16 - link local, which means it never got an address at all.
        return !(b[0] == 10
                 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                 || (b[0] == 192 && b[1] == 168)
                 || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                 || (b[0] == 169 && b[1] == 254)
                 || b[0] == 127);
    }
}
