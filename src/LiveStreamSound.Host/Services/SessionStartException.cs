namespace LiveStreamSound.Host.Services;

/// <summary>
/// Thrown when <see cref="HostOrchestrator.StartSession"/> fails to bring up all services.
/// <see cref="Kind"/> is a stable identifier used by the ViewModel to pick a localized message.
/// </summary>
public sealed class SessionStartException : Exception
{
    public string Kind { get; }
    public SessionStartException(string kind, Exception inner)
        : base(inner.Message, inner)
    {
        Kind = kind;
    }
}
