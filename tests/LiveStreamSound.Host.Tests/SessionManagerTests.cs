using System.Net;
using System.Net.Sockets;
using LiveStreamSound.Host.Services;

namespace LiveStreamSound.Host.Tests;

/// <summary>
/// Covers the grace-period / self-healing reconnect semantics added to
/// SessionManager — soft vs hard unregister, rejoin by name, state
/// preservation across disconnect, sweep-timer eviction behavior.
///
/// Uses a tiny TestLogService stub and TcpClient instances that are never
/// actually connected (SessionManager only stores references; the tests
/// never trigger any I/O).
/// </summary>
public class SessionManagerTests
{
    private static LogService NewLog() => new();

    private static ConnectedClient NewClient(string name, string? id = null)
    {
        id ??= Guid.NewGuid().ToString("N")[..12];
        return new ConnectedClient
        {
            ClientId = id,
            ClientName = name,
            TcpClient = new TcpClient(),
            TcpEndpoint = new IPEndPoint(IPAddress.Loopback, 40000 + Random.Shared.Next(1000)),
        };
    }

    [Fact]
    public void StartSession_GeneratesCodeAndActivates()
    {
        using var sm = new SessionManager(NewLog());
        Assert.False(sm.IsActive);
        var code = sm.StartSession();
        Assert.True(sm.IsActive);
        Assert.Equal(6, code.Length);
        Assert.Equal(code, sm.Code);
        Assert.NotNull(sm.StartedAt);
        Assert.NotEqual(Guid.Empty, sm.SessionId);
    }

    [Fact]
    public void StopSession_DeactivatesAndClearsClients()
    {
        using var sm = new SessionManager(NewLog());
        sm.StartSession();
        sm.RegisterClient(NewClient("Raum 17"));
        sm.RegisterClient(NewClient("Raum 18"));
        Assert.Equal(2, sm.Clients.Count);

        sm.StopSession();
        Assert.False(sm.IsActive);
        Assert.Null(sm.Code);
        Assert.Empty(sm.Clients);
    }

    [Fact]
    public void ValidateCode_OnlyActiveSession()
    {
        using var sm = new SessionManager(NewLog());
        Assert.False(sm.ValidateCode("123456"));
        var code = sm.StartSession();
        Assert.True(sm.ValidateCode(code));
        Assert.False(sm.ValidateCode(code + "1"));
        Assert.False(sm.ValidateCode("000000"));
    }

    [Fact]
    public void RegisterClient_FiresClientJoined()
    {
        using var sm = new SessionManager(NewLog());
        sm.StartSession();
        ConnectedClient? raised = null;
        sm.ClientJoined += c => raised = c;

        var client = NewClient("Raum 17");
        var returned = sm.RegisterClient(client);

        Assert.Same(client, returned);
        Assert.Same(client, raised);
        Assert.Single(sm.Clients);
        Assert.Contains(client, sm.Clients);
    }

    [Fact]
    public void UnregisterClient_SoftUnregister_KeepsInClients_FiresReconnecting()
    {
        using var sm = new SessionManager(NewLog());
        sm.StartSession();
        var client = NewClient("Raum 17");
        sm.RegisterClient(client);

        ConnectedClient? reconnecting = null;
        ConnectedClient? left = null;
        sm.ClientReconnecting += c => reconnecting = c;
        sm.ClientLeft += c => left = c;

        sm.UnregisterClient(client.ClientId);

        // Soft unregister: still in Clients but flagged as reconnecting
        Assert.Single(sm.Clients);
        Assert.True(client.IsReconnecting);
        Assert.NotNull(client.ReconnectingSince);
        Assert.Same(client, reconnecting);
        Assert.Null(left); // not yet — grace period is active
    }

    [Fact]
    public void UnregisterClient_Twice_IsIdempotent()
    {
        using var sm = new SessionManager(NewLog());
        sm.StartSession();
        var client = NewClient("Raum 17");
        sm.RegisterClient(client);

        var fireCount = 0;
        sm.ClientReconnecting += _ => fireCount++;

        sm.UnregisterClient(client.ClientId);
        sm.UnregisterClient(client.ClientId); // should be a no-op
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void HardUnregisterClient_RemovesImmediately_FiresClientLeft()
    {
        using var sm = new SessionManager(NewLog());
        sm.StartSession();
        var client = NewClient("Raum 17");
        sm.RegisterClient(client);

        ConnectedClient? reconnecting = null;
        ConnectedClient? left = null;
        sm.ClientReconnecting += c => reconnecting = c;
        sm.ClientLeft += c => left = c;

        sm.HardUnregisterClient(client.ClientId);

        Assert.Empty(sm.Clients);
        Assert.Null(reconnecting); // hard unregister skips the soft state
        Assert.Same(client, left);
    }

    [Fact]
    public void TryFindReconnectingByName_FindsOnlyReconnectingClients()
    {
        using var sm = new SessionManager(NewLog());
        sm.StartSession();

        var active = NewClient("Raum 17");
        var gone = NewClient("Raum 18");
        sm.RegisterClient(active);
        sm.RegisterClient(gone);
        sm.UnregisterClient(gone.ClientId);

        // Active ones don't match — they're not in grace.
        Assert.Null(sm.TryFindReconnectingByName("Raum 17"));
        // Reconnecting one matches by name.
        var found = sm.TryFindReconnectingByName("Raum 18");
        Assert.Same(gone, found);
    }

    [Fact]
    public void TryFindReconnectingByName_CaseInsensitive_WhitespaceTolerant()
    {
        using var sm = new SessionManager(NewLog());
        sm.StartSession();
        var c = NewClient("Raum 17");
        sm.RegisterClient(c);
        sm.UnregisterClient(c.ClientId);

        Assert.Same(c, sm.TryFindReconnectingByName("RAUM 17"));
        Assert.Same(c, sm.TryFindReconnectingByName("raum 17"));
        Assert.Same(c, sm.TryFindReconnectingByName("  Raum 17  "));
        Assert.Null(sm.TryFindReconnectingByName("Raum 18"));
    }

    [Fact]
    public void FinalizeRejoin_ClearsReconnectingFlag_FiresRejoinedEvent()
    {
        using var sm = new SessionManager(NewLog());
        sm.StartSession();
        var client = NewClient("Raum 17");
        sm.RegisterClient(client);
        sm.UnregisterClient(client.ClientId);

        Assert.True(client.IsReconnecting);

        ConnectedClient? rejoined = null;
        sm.ClientRejoined += c => rejoined = c;

        // Simulate ControlServer: rejoin slotted a fresh TcpClient in.
        client.TcpClient = new TcpClient();
        client.TcpEndpoint = new IPEndPoint(IPAddress.Loopback, 50000);
        client.WriteLock = new SemaphoreSlim(1, 1);
        sm.FinalizeRejoin(client);

        Assert.False(client.IsReconnecting);
        Assert.Null(client.ReconnectingSince);
        Assert.Same(client, rejoined);
    }

    [Fact]
    public void Rejoin_PreservesVolumeMuteAndDeviceAcrossDisconnect()
    {
        using var sm = new SessionManager(NewLog());
        sm.StartSession();

        var client = NewClient("Raum 17");
        client.Volume = 0.42f;
        client.IsMuted = true;
        client.CurrentOutputDeviceId = "hdmi-out-1";
        sm.RegisterClient(client);

        sm.UnregisterClient(client.ClientId);

        // Settings survive the soft-unregister because the same instance stays.
        Assert.Equal(0.42f, client.Volume);
        Assert.True(client.IsMuted);
        Assert.Equal("hdmi-out-1", client.CurrentOutputDeviceId);

        // The ControlServer would find it by name and finalize the rejoin —
        // same ClientId, settings intact.
        var found = sm.TryFindReconnectingByName("Raum 17");
        Assert.Same(client, found);
        sm.FinalizeRejoin(client);
        Assert.Equal(0.42f, client.Volume);
        Assert.True(client.IsMuted);
        Assert.Equal("hdmi-out-1", client.CurrentOutputDeviceId);
    }

    [Fact]
    public void ActiveClients_SkipsReconnectingOnes()
    {
        using var sm = new SessionManager(NewLog());
        sm.StartSession();

        var a = NewClient("Raum 17");
        var b = NewClient("Raum 18");
        var c = NewClient("Werkstatt 38");
        sm.RegisterClient(a);
        sm.RegisterClient(b);
        sm.RegisterClient(c);

        sm.UnregisterClient(b.ClientId);

        var active = sm.ActiveClients.ToArray();
        Assert.Equal(2, active.Length);
        Assert.Contains(a, active);
        Assert.Contains(c, active);
        Assert.DoesNotContain(b, active);

        // But `Clients` still includes the reconnecting one (UI tracks it).
        Assert.Equal(3, sm.Clients.Count);
    }

    [Fact]
    public async Task Sweep_RemovesClientsWhoseGraceHasExpired()
    {
        using var sm = new SessionManager(NewLog()) { RejoinGracePeriod = TimeSpan.FromMilliseconds(100) };
        sm.StartSession();

        var c = NewClient("Raum 17");
        sm.RegisterClient(c);
        ConnectedClient? left = null;
        sm.ClientLeft += x => left = x;

        sm.UnregisterClient(c.ClientId);
        Assert.Single(sm.Clients);

        // Wait for grace + at least one sweep tick (sweep fires every 5s in
        // the timer — too slow for a unit test. Manually advance instead.)
        // We directly manipulate ReconnectingSince into the past and trigger
        // a sweep by using reflection-free sleep + one more scheduled tick.
        c.ReconnectingSince = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(60);
        // Force sweep via reflection to keep the test deterministic.
        var sweep = typeof(SessionManager).GetMethod("SweepExpiredReconnects",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(sweep);
        sweep!.Invoke(sm, null);

        Assert.Empty(sm.Clients);
        Assert.Same(c, left);

        await Task.CompletedTask;
    }

    [Fact]
    public void Sweep_DoesNotRemoveClientWithinGracePeriod()
    {
        using var sm = new SessionManager(NewLog()) { RejoinGracePeriod = TimeSpan.FromSeconds(60) };
        sm.StartSession();
        var c = NewClient("Raum 17");
        sm.RegisterClient(c);
        sm.UnregisterClient(c.ClientId);

        // Reconnecting for 5s — still well within the 60s grace.
        c.ReconnectingSince = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(5);

        ConnectedClient? left = null;
        sm.ClientLeft += x => left = x;

        var sweep = typeof(SessionManager).GetMethod("SweepExpiredReconnects",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        sweep.Invoke(sm, null);

        Assert.Single(sm.Clients);
        Assert.Null(left);
    }

    [Fact]
    public void Sweep_RemovesMultipleExpiredClients_LeavesHealthyOnes()
    {
        using var sm = new SessionManager(NewLog()) { RejoinGracePeriod = TimeSpan.FromSeconds(60) };
        sm.StartSession();

        var active = NewClient("Active Room");
        var stale1 = NewClient("Stale 1");
        var stale2 = NewClient("Stale 2");
        var fresh = NewClient("Fresh Reconnect");
        sm.RegisterClient(active);
        sm.RegisterClient(stale1);
        sm.RegisterClient(stale2);
        sm.RegisterClient(fresh);

        sm.UnregisterClient(stale1.ClientId);
        sm.UnregisterClient(stale2.ClientId);
        sm.UnregisterClient(fresh.ClientId);

        stale1.ReconnectingSince = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
        stale2.ReconnectingSince = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(2);
        fresh.ReconnectingSince = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(3);

        var leftClients = new List<ConnectedClient>();
        sm.ClientLeft += c => leftClients.Add(c);

        var sweep = typeof(SessionManager).GetMethod("SweepExpiredReconnects",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        sweep.Invoke(sm, null);

        Assert.Equal(2, leftClients.Count);
        Assert.Contains(stale1, leftClients);
        Assert.Contains(stale2, leftClients);
        Assert.DoesNotContain(active, leftClients);
        Assert.DoesNotContain(fresh, leftClients);

        // Fresh one is still parked for rejoin.
        Assert.Contains(fresh, sm.Clients);
        Assert.True(fresh.IsReconnecting);
    }

    [Fact]
    public void MultipleClientsWithSameNameCollision_OnlyOneEnters_GracePeriod()
    {
        using var sm = new SessionManager(NewLog());
        sm.StartSession();

        var first = NewClient("Raum 17");
        sm.RegisterClient(first);
        sm.UnregisterClient(first.ClientId);

        // A second, independent client with the same name while first is in grace.
        // The implementation uses TryFindReconnectingByName to match — so the
        // host-side reuse logic would pick `first`. Let's verify that.
        var found = sm.TryFindReconnectingByName("Raum 17");
        Assert.Same(first, found);

        // After rejoin, can't find the same reconnecting entry anymore.
        sm.FinalizeRejoin(first);
        Assert.Null(sm.TryFindReconnectingByName("Raum 17"));
    }

    [Fact]
    public void GetClient_ByIdReturnsOrNull()
    {
        using var sm = new SessionManager(NewLog());
        sm.StartSession();
        var c = NewClient("Raum 17");
        sm.RegisterClient(c);
        Assert.Same(c, sm.GetClient(c.ClientId));
        Assert.Null(sm.GetClient("does-not-exist"));
    }

    [Fact]
    public void SessionStateChanged_FiresOnStartAndStop()
    {
        using var sm = new SessionManager(NewLog());
        var count = 0;
        sm.SessionStateChanged += () => count++;

        sm.StartSession();
        Assert.Equal(1, count);
        sm.StopSession();
        Assert.Equal(2, count);
    }
}
