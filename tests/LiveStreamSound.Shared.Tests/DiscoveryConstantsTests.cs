using System.Net.NetworkInformation;
using LiveStreamSound.Shared.Discovery;

namespace LiveStreamSound.Shared.Tests;

/// <summary>
/// Freezes the wire contract. A change to any of these breaks all older clients
/// in the field, so the test is a tripwire for accidental port/name renames.
/// </summary>
public class DiscoveryConstantsTests
{
    [Fact]
    public void DefaultPorts_MatchDocumentedValues()
    {
        Assert.Equal(5000, DiscoveryConstants.DefaultControlPort);
        Assert.Equal(5001, DiscoveryConstants.DefaultAudioPort);
        Assert.Equal(5002, DiscoveryConstants.DefaultIdleClientPort);
    }

    [Fact]
    public void ProtocolVersion_IsCurrent()
    {
        Assert.Equal(1, DiscoveryConstants.ProtocolVersion);
    }

    [Fact]
    public void MDnsServiceTypes_FollowRFC2782_Format()
    {
        // Service types must be "_name._proto" per RFC 2782. Also checked by
        // Makaretu.Dns — mis-named types get rejected silently.
        Assert.Equal("_livestreamsound._tcp", DiscoveryConstants.MDnsServiceType);
        Assert.Equal("_lssclient._tcp", DiscoveryConstants.MDnsClientServiceType);
        Assert.StartsWith("_", DiscoveryConstants.MDnsServiceType);
        Assert.EndsWith("._tcp", DiscoveryConstants.MDnsServiceType);
    }

    [Fact]
    public void UriScheme_MatchesLegacyDeepLink()
    {
        // Used by the QR-code flow (removed in v2) and still reserved for
        // any future deep-link / share-code feature.
        Assert.Equal("livestreamsound", DiscoveryConstants.UriScheme);
    }

    [Fact]
    public void TxtKeys_AreStable()
    {
        Assert.Equal("v", DiscoveryConstants.TxtVersionKey);
        Assert.Equal("name", DiscoveryConstants.TxtSessionNameKey);
    }

    [Fact]
    public void NetworkInterfaceFilter_Loopback_Rejected()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                Assert.False(NetworkInterfaceFilter.IsRealLan(nic),
                    $"Loopback {nic.Name} should be filtered out");
            }
        }
    }

    [Fact]
    public void NetworkInterfaceFilter_DownAdapters_Rejected()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                Assert.False(NetworkInterfaceFilter.IsRealLan(nic),
                    $"Non-Up NIC {nic.Name} should be filtered out");
            }
        }
    }
}
