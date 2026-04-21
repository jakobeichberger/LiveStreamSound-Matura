namespace LiveStreamSound.Shared.Discovery;

public static class DiscoveryConstants
{
    public const string MDnsServiceType = "_livestreamsound._tcp";
    public const string UriScheme = "livestreamsound";

    public const int DefaultControlPort = 5000;
    public const int DefaultAudioPort = 5001;

    /// <summary>TCP port on which an idle client listens for host-initiated invitations.</summary>
    public const int DefaultIdleClientPort = 5002;

    /// <summary>mDNS service type advertised by idle clients waiting for a host invitation.</summary>
    public const string MDnsClientServiceType = "_lssclient._tcp";

    public const int ProtocolVersion = 1;

    public const string TxtVersionKey = "v";
    public const string TxtSessionNameKey = "name";
}
