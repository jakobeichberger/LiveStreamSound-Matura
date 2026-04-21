namespace LiveStreamSound.Shared.Discovery;

public static class DiscoveryConstants
{
    public const string MDnsServiceType = "_livestreamsound._tcp";
    public const string UriScheme = "livestreamsound";

    public const int DefaultControlPort = 5000;
    public const int DefaultAudioPort = 5001;

    public const int ProtocolVersion = 1;

    public const string TxtVersionKey = "v";
    public const string TxtSessionNameKey = "name";
}
