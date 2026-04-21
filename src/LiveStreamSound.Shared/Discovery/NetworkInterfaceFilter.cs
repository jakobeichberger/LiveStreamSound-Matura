using System.Net.NetworkInformation;

namespace LiveStreamSound.Shared.Discovery;

/// <summary>
/// Central heuristic for "is this a real LAN/Wi-Fi adapter we want to use".
/// Shared by the mDNS advertise + browse code on both host and client sides so
/// Hyper-V, WSL2, VirtualBox etc. virtual adapters never get the Matura session
/// advertised on them (which confuses clients picking the wrong IP).
/// </summary>
public static class NetworkInterfaceFilter
{
    public static bool IsRealLan(NetworkInterface nic)
    {
        if (nic.OperationalStatus != OperationalStatus.Up) return false;

        switch (nic.NetworkInterfaceType)
        {
            case NetworkInterfaceType.Loopback:
            case NetworkInterfaceType.Tunnel:
                return false;
        }

        var name = nic.Name ?? "";
        var desc = nic.Description ?? "";

        // Windows Hyper-V and WSL2 surface as virtual Ethernet adapters that
        // confuse local-LAN clients. Filter them out.
        if (Contains(name, "vEthernet") || Contains(desc, "Hyper-V")) return false;
        if (Contains(name, "WSL") || Contains(desc, "WSL")) return false;
        if (Contains(desc, "VirtualBox")) return false;
        if (Contains(desc, "VMware")) return false;
        // macOS Parallels, Docker, Tailscale — also noisy for our scenario
        if (Contains(desc, "Parallels")) return false;
        if (Contains(name, "docker") || Contains(desc, "Docker")) return false;
        if (Contains(name, "utun") || Contains(name, "tailscale")) return false;

        return true;
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
