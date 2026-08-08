using System.Net.Http;
using System.Text.Json;
namespace DanmuFree.Core.Protocol;

public sealed class StatsService
{
    private readonly HttpClient _http;

    public StatsService(HttpClient http, string? cookie)
    {
        _http = http;
        if (cookie is not null)
            _http.DefaultRequestHeaders.Add("Cookie", cookie);
    }

    public event Action<RoomStats>? Updated;

    public async Task<RoomStats?> GetAsync(string roomIdInput, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(
                $"https://api.live.bilibili.com/xlive/web-room/v1/index/getInfoByRoom?room_id={roomIdInput}", ct);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            if (root.GetProperty("code").GetInt32() != 0) return null;
            var data = root.GetProperty("data");
            return new RoomStats(
                data.GetProperty("room_info").GetProperty("online").GetInt32(),
                data.GetProperty("watched_show").GetProperty("num").GetInt32(),
                data.GetProperty("like_info_v3").GetProperty("total_likes").GetInt32());
        }
        catch { return null; }
    }

    public async Task StartAsync(string roomIdInput, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var stats = await GetAsync(roomIdInput, ct);
            if (stats is not null) Updated?.Invoke(stats);
            try { await Task.Delay(60_000, ct); }
            catch (OperationCanceledException) { return; }
        }
    }
}
