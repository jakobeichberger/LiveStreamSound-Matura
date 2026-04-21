using Makaretu.Dns;
using LiveStreamSound.Shared.Discovery;

namespace LiveStreamSound.Host.Services;

public sealed class MDnsAdvertiseService : IDisposable
{
    private readonly LogService _log;
    private ServiceDiscovery? _sd;

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

            _sd = new ServiceDiscovery();
            _sd.Advertise(profile);
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
        _sd = null;
    }
}
