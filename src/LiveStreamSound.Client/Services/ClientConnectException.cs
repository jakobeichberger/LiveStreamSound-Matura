namespace LiveStreamSound.Client.Services;

public sealed class ClientConnectException : Exception
{
    public string Kind { get; }
    public ClientConnectException(string kind, Exception inner)
        : base(inner.Message, inner)
    {
        Kind = kind;
    }
}
