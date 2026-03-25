using Microsoft.Maui.Storage;

namespace OFFline_Spotify;

public static class ServerEndpointSettings
{
    private const string ServerHostPreferenceKey = "server_host";
    public const string DefaultHost = "192.168.1.49";
    public const int ServerPort = 9999;

    public static string GetHost()
    {
        var host = Preferences.Default.Get(ServerHostPreferenceKey, DefaultHost);
        return string.IsNullOrWhiteSpace(host) ? DefaultHost : host.Trim();
    }

    public static void SetHost(string host)
    {
        Preferences.Default.Set(ServerHostPreferenceKey, host.Trim());
    }

    public static bool IsValidHost(string host)
    {
        return System.Net.IPAddress.TryParse(host, out _)
               || Uri.CheckHostName(host) != UriHostNameType.Unknown;
    }
}