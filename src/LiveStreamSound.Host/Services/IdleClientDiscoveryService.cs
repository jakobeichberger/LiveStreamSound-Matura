using System.Net;
using Makaretu.Dns;
using LiveStreamSound.Shared.Discovery;

namespace LiveStreamSound.Host.Services;

public sealed record DiscoveredIdleClient(
    string InstanceName,
    IPAddress Address,
    int IdlePort,
    string? FriendlyName,
    DateTimeOffset LastSeen);

/// <summary>
/// Host-side browser for idle clients that advertise <c>_lssclient._tcp</c>.
/// Used by the "Add Client" / invite dialog.
/// </summary>
public sealed class IdleClientDiscoveryService : IDisposable
{
    private readonly LogService _log;
    private ServiceDiscovery? _sd;
    private readonly Dictionary<string, DiscoveredIdleClient> _clients = new();
    private readonly object _lock = new();

    public event Action<IReadOnlyList<DiscoveredIdleClient>>? ClientsChanged;

    public IdleClientDiscoveryService(LogService log) { _log = log; }

    public IReadOnlyList<DiscoveredIdleClient> CurrentClients
    {
        get { lock (_lock) return _clients.Values.ToList(); }
    }

    public void Start()
    {
        try
        {
            _sd = new ServiceDiscovery();
            _sd.ServiceInstanceDiscovered += OnInstanceDiscovered;
            _sd.ServiceInstanceShutdown += OnInstanceShutdown;
            _sd.QueryServiceInstances(DiscoveryConstants.MDnsClientServiceType);
            _log.Info("IdleClientDiscovery", $"Browsing for {DiscoveryConstants.MDnsClientServiceType}");
        }
        catch (Exception ex)
        {
            _log.Warn("IdleClientDiscovery", "Browse failed (manual IP still works)", ex);
        }
    }

    private void OnInstanceDiscovered(object? sender, ServiceInstanceDiscoveryEventArgs e)
    {
        try
        {
            var instance = e.ServiceInstanceName.ToString();
            var records = e.Message.Answers.Concat(e.Message.AdditionalRecords).ToList();

            var srv = records.OfType<SRVRecord>()
                .FirstOrDefault(r => string.Equals(r.Name?.ToString(), instance, StringComparison.OrdinalIgnoreCase));
            if (srv is null) return;

            var target = srv.Target.ToString();
            var addr = records.OfType<AddressRecord>()
                .FirstOrDefault(r => string.Equals(r.Name?.ToString(), target, StringComparison.OrdinalIgnoreCase));
            if (addr?.Address is null) return;

            string? friendly = null;
            var txt = records.OfType<TXTRecord>()
                .FirstOrDefault(r => string.Equals(r.Name?.ToString(), instance, StringComparison.OrdinalIgnoreCase));
            if (txt is not null)
            {
                foreach (var kv in txt.Strings)
                {
                    var eq = kv.IndexOf('=');
                    if (eq < 0) continue;
                    if (string.Equals(kv[..eq], DiscoveryConstants.TxtSessionNameKey, StringComparison.OrdinalIgnoreCase))
                        friendly = kv[(eq + 1)..];
                }
            }

            var client = new DiscoveredIdleClient(instance, addr.Address, srv.Port, friendly, DateTimeOffset.Now);

            bool changed;
            lock (_lock)
            {
                changed = !_clients.TryGetValue(instance, out var prev) || !Equals(prev, client);
                _clients[instance] = client;
            }
            if (changed) Notify();
        }
        catch (Exception ex)
        {
            _log.Warn("IdleClientDiscovery", "Parsing answer failed", ex);
        }
    }

    private void OnInstanceShutdown(object? sender, ServiceInstanceShutdownEventArgs e)
    {
        var instance = e.ServiceInstanceName.ToString();
        bool removed;
        lock (_lock) removed = _clients.Remove(instance);
        if (removed) Notify();
    }

    private void Notify()
    {
        IReadOnlyList<DiscoveredIdleClient> snapshot;
        lock (_lock) snapshot = _clients.Values.ToList();
        ClientsChanged?.Invoke(snapshot);
    }

    public void Dispose()
    {
        try { _sd?.Dispose(); } catch { }
        _sd = null;
    }
}
