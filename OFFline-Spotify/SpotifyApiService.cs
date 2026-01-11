using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;

public class SpotifyPlaylistInfo
{
    public string name { get; set; }
    public List<Image> images { get; set; }
    public class Image { public string url { get; set; } }
}

public static class SpotifyApiService
{
    // 1. Get a Spotify access token using Client Credentials Flow
    public static async Task<string?> GetSpotifyTokenAsync(string clientId, string clientSecret)
    {
        using var http = new HttpClient();
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);

        var body = new Dictionary<string, string>
        {
            { "grant_type", "client_credentials" }
        };
        var response = await http.PostAsync("https://accounts.spotify.com/api/token", new FormUrlEncodedContent(body));
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return json?["access_token"]?.ToString();
    }

    // 2. Get playlist info using the access token
    public static async Task<SpotifyPlaylistInfo?> GetPlaylistInfoAsync(string playlistId, string accessToken)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var url = $"https://api.spotify.com/v1/playlists/{playlistId}";
        var response = await http.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<SpotifyPlaylistInfo>();
    }
}
