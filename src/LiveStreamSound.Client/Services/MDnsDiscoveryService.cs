using System.Net;
using Makaretu.Dns;
using LiveStreamSound.Shared.Discovery;

namespace LiveStreamSound.Client.Services;

public sealed record DiscoveredHost(
    string InstanceName,
    IPAddress Address,
    int ControlPort,
    string? SessionName,
    DateTimeOffset LastSeen);

public sealed class MDnsDiscoveryService : IDisposable
{
    private readonly LogService _log;
    private ServiceDiscovery? _sd;
    private MulticastService? _mc;
    private readonly Dictionary<string, DiscoveredHost> _hosts = new();
    private readonly object _lock = new();

    public event Action<IReadOnlyList<DiscoveredHost>>? HostsChanged;

    public MDnsDiscoveryService(LogService log) { _log = log; }

    public bool IsRunning => _sd is not null;

    public void Start()
    {
        if (IsRunning) return;
        try
        {
            _mc = new MulticastService(nics => nics.Where(NetworkInterfaceFilter.IsRealLan).ToList());
            _sd = new ServiceDiscovery(_mc);
            _sd.ServiceInstanceDiscovered += OnServiceInstanceDiscovered;
            _sd.ServiceInstanceShutdown += OnServiceInstanceShutdown;
            _mc.Start();
            _sd.QueryServiceInstances(DiscoveryConstants.MDnsServiceType);
            _log.Info("mDNS", $"Browsing for {DiscoveryConstants.MDnsServiceType}");
        }
        catch (Exception ex)
        {
            _log.Warn("mDNS", "Discovery failed (manual IP entry still works)", ex);
        }
    }

    /// <summary>Stop browsing. Service is reusable — call Start() again to resume.</summary>
    public void Stop()
    {
        try { _sd?.Dispose(); } catch { }
        try { _mc?.Dispose(); } catch { }
        _sd = null;
        _mc = null;
        lock (_lock) _hosts.Clear();
        Notify();
        _log.Info("mDNS", "Discovery stopped");
    }

    private void OnServiceInstanceDiscovered(object? sender, ServiceInstanceDiscoveryEventArgs e)
    {
        try
        {
            var instanceName = e.ServiceInstanceName.ToString();
            // Makaretu fires this for every mDNS instance on the network regardless
            // of our specific Query. Skip anything that isn't ours.
            if (!instanceName.Contains(DiscoveryConstants.MDnsServiceType, StringComparison.OrdinalIgnoreCase))
                return;

            var records = e.Message.Answers.Concat(e.Message.AdditionalRecords).ToList();

            var srv = records.OfType<SRVRecord>()
                .FirstOrDefault(r => string.Equals(r.Name?.ToString(), instanceName, StringComparison.OrdinalIgnoreCase));
            if (srv is null) return;

            var target = srv.Target.ToString();
            var addr = records.OfType<AddressRecord>()
                .FirstOrDefault(r => string.Equals(r.Name?.ToString(), target, StringComparison.OrdinalIgnoreCase));
            if (addr?.Address is null) return;

            var txt = records.OfType<TXTRecord>()
                .FirstOrDefault(r => string.Equals(r.Name?.ToString(), instanceName, StringComparison.OrdinalIgnoreCase));
            string? sessionName = null;
            if (txt is not null)
            {
                foreach (var kv in txt.Strings)
                {
                    var eqIdx = kv.IndexOf('=');
                    if (eqIdx < 0) continue;
                    var key = kv[..eqIdx];
                    var value = kv[(eqIdx + 1)..];
                    if (string.Equals(key, DiscoveryConstants.TxtSessionNameKey, StringComparison.OrdinalIgnoreCase))
                        sessionName = value;
                }
            }

            var host = new DiscoveredHost(instanceName, addr.Address, srv.Port, sessionName, DateTimeOffset.Now);

            bool changed;
            lock (_lock)
            {
                changed = !_hosts.TryGetValue(instanceName, out var existing) || !HostEquals(existing, host);
                _hosts[instanceName] = host;
            }
            if (changed)
            {
                _log.Info("mDNS", $"Discovered {instanceName} at {host.Address}:{host.ControlPort}");
                Notify();
            }
        }
        catch (Exception ex)
        {
            _log.Warn("mDNS", "Parsing service instance answer failed", ex);
        }
    }

    private static bool HostEquals(DiscoveredHost a, DiscoveredHost b) =>
        a.Address.Equals(b.Address) &&
        a.ControlPort == b.ControlPort &&
        a.SessionName == b.SessionName;

    private void OnServiceInstanceShutdown(object? sender, ServiceInstanceShutdownEventArgs e)
    {
        var instance = e.ServiceInstanceName.ToString();
        bool removed;
        lock (_lock) { removed = _hosts.Remove(instance); }
        if (removed)
        {
            _log.Info("mDNS", $"Lost {instance}");
            Notify();
        }
    }

    public IReadOnlyList<DiscoveredHost> CurrentHosts
    {
        get { lock (_lock) return _hosts.Values.ToList(); }
    }

    private void Notify()
    {
        IReadOnlyList<DiscoveredHost> snapshot;
        lock (_lock) snapshot = _hosts.Values.ToList();
        HostsChanged?.Invoke(snapshot);
    }

    public void Dispose()
    {
        try { _sd?.Dispose(); } catch { }
        try { _mc?.Dispose(); } catch { }
        _sd = null;
        _mc = null;
    }
}
