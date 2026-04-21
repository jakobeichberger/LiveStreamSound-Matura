using Makaretu.Dns;
using LiveStreamSound.Shared.Discovery;

namespace LiveStreamSound.Host.Services;

public sealed class MDnsAdvertiseService : IDisposable
{
    private readonly LogService _log;
    private ServiceDiscovery? _sd;
    private MulticastService? _mc;

    public MDnsAdvertiseService(LogService log) { _log = log; }

    public void Advertise(string instanceName, int controlPort, string sessionName)
    {
        try
        {
            var profile = new ServiceProfile(
                instanceName: instanceName,
                serviceName: DiscoveryConstants.MDnsServiceType,
                port: (ushort)controlPort);

            profile.AddProperty(DiscoveryConstants.TxtVersionKey, DiscoveryConstants.ProtocolVersion.ToString());
            profile.AddProperty(DiscoveryConstants.TxtSessionNameKey, sessionName);

            // Restrict mDNS advertising to real Wi-Fi / Ethernet adapters — skip
            // Hyper-V, WSL, VMware etc. virtual adapters so clients never see a
            // stale 172.x.x.x / 10.x.x.x virtual-switch IP for this session.
            _mc = new MulticastService(nics => nics.Where(NetworkInterfaceFilter.IsRealLan).ToList());
            _sd = new ServiceDiscovery(_mc);
            _sd.Advertise(profile);
            _mc.Start();
            _log.Info("mDNS", $"Advertising '{instanceName}' on {controlPort} ({DiscoveryConstants.MDnsServiceType})");
        }
        catch (Exception ex)
        {
            _log.Warn("mDNS", "Failed to advertise (clients can still connect manually)", ex);
        }
    }

    public void Dispose()
    {
        try { _sd?.Unadvertise(); } catch { }
        try { _sd?.Dispose(); } catch { }
        try { _mc?.Dispose(); } catch { }
        _sd = null;
        _mc = null;
    }
}
