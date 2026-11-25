using System.Net.Http.Json;
using System.Text.Json;

record FrontendRelease(Version Version, string Source);
sealed class FrontendCatalog(IHttpClientFactory clients, IConfiguration config)
{
    static readonly Dictionary<string, Version> Reviewed = new(StringComparer.OrdinalIgnoreCase) {
        ["jquery"] = new(4, 1, 0), ["bootstrap"] = new(5, 3, 9)
    };
    readonly SemaphoreSlim gate = new(1);
    Dictionary<string, FrontendRelease> cached = Reviewed.ToDictionary(x => x.Key, x => new FrontendRelease(x.Value, "Official release reviewed 2026-11-25"));
    DateTime refreshAfter;

    public async Task<Dictionary<string, FrontendRelease>> Get(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (DateTime.UtcNow < refreshAfter || config.GetValue<bool>("Frontend:Offline")) return cached;
            using var client = clients.CreateClient("frontend-releases");
            // Only these two public package names leave the server. Never resolve uploaded URLs.
            async Task<KeyValuePair<string, FrontendRelease>> Lookup(string name)
            {
                try
                {
                    using var json = await client.GetFromJsonAsync<JsonDocument>($"https://registry.npmjs.org/{name}/latest", token);
                    string? raw = json?.RootElement.GetProperty("version").GetString();
                    if (Version.TryParse(raw, out var version) && version.Build >= 0 && version.Revision == -1 && version >= Reviewed[name])
                        return new(name, new(version, $"npm latest checked {DateTime.UtcNow:yyyy-MM-dd}"));
                }
                catch (Exception e) when (!token.IsCancellationRequested && e is (HttpRequestException or OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)) { }
                return new(name, cached[name]);
            }
            var results = await Task.WhenAll(Reviewed.Keys.Select(Lookup));
            cached = results.ToDictionary(x => x.Key, x => x.Value);
            refreshAfter = DateTime.UtcNow.AddHours(6);
            return cached;
        }
        finally { gate.Release(); }
    }
}
