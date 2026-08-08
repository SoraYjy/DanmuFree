namespace DanmuFree.Core.Client;

/// <summary>
/// Lifecycle states of a <see cref="BilibiliDanmuClient"/> connection. Transitions are
/// surfaced via <see cref="BilibiliDanmuClient.ConnectionStateChanged"/>.
/// </summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
}
