using System.Net;
using System.Net.Sockets;
using LiveStreamSound.Shared.Discovery;
using LiveStreamSound.Shared.Protocol;

namespace LiveStreamSound.Host.Services;

public enum InviteOutcome
{
    Accepted,
    Declined,
    Unreachable,
    InvalidResponse,
}

public sealed record InviteResult(InviteOutcome Outcome, string? Reason);

/// <summary>
/// Sends an <see cref="Invitation"/> to an idle client listening on TCP 5002.
/// Single-shot: opens connection, writes Invitation, reads InvitationResponse, closes.
/// </summary>
public sealed class InviteClientService
{
    private readonly LogService _log;

    public InviteClientService(LogService log) { _log = log; }

    public async Task<InviteResult> SendInviteAsync(
        IPAddress target,
        int targetPort,
        string sessionCode,
        string hostAddress,
        int hostControlPort,
        string hostDisplayName,
        CancellationToken ct = default)
    {
        try
        {
            using var tcp = new TcpClient { NoDelay = true };
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(30));

            await tcp.ConnectAsync(target, targetPort, linkedCts.Token).ConfigureAwait(false);
            var stream = tcp.GetStream();

            var invite = new Invitation(sessionCode, hostAddress, hostControlPort, hostDisplayName);
            await MessageJson.WriteFrameAsync(stream, invite, linkedCts.Token).ConfigureAwait(false);

            var reply = await MessageJson.ReadFrameAsync(stream, linkedCts.Token).ConfigureAwait(false);
            if (reply is not InvitationResponse ir)
            {
                _log.Warn("InviteClient", $"{target}:{targetPort} returned unexpected message: {reply?.GetType().Name ?? "null"}");
                return new InviteResult(InviteOutcome.InvalidResponse, null);
            }
            _log.Info("InviteClient", $"{target}:{targetPort} responded Accepted={ir.Accepted} reason={ir.Reason}");
            return new InviteResult(ir.Accepted ? InviteOutcome.Accepted : InviteOutcome.Declined, ir.Reason);
        }
        catch (SocketException ex)
        {
            _log.Warn("InviteClient", $"{target}:{targetPort} unreachable", ex);
            return new InviteResult(InviteOutcome.Unreachable, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return new InviteResult(InviteOutcome.Unreachable, "timeout");
        }
        catch (Exception ex)
        {
            _log.Warn("InviteClient", $"{target}:{targetPort} invite failed", ex);
            return new InviteResult(InviteOutcome.Unreachable, ex.Message);
        }
    }
}
