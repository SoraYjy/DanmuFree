namespace DanmuFree.Core.Login;

public enum QrState { Waiting, Scanned, Expired, Success }

public sealed record QrStatus(QrState State, string? Cookie = null)
{
    public static readonly QrStatus Waiting = new(QrState.Waiting);
    public static readonly QrStatus Scanned = new(QrState.Scanned);
    public static readonly QrStatus Expired = new(QrState.Expired);
    public static QrStatus Success(string cookie) => new(QrState.Success, cookie);
}
