using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Roadmapper.Web;

/// <summary>Start-up helpers for the packaged Roadmapper.exe: picking a port, spotting a running copy, opening the browser.</summary>
public static class Launcher
{
    public const int PreferredPort = 5210;

    /// <summary>A tiny endpoint that lets a second copy of the app recognise the first one.</summary>
    public const string ProbePath = "/_roadmapper";
    public const string ProbeReply = "Roadmapper";

    public sealed record Address(string Url, bool AlreadyRunning);

    /// <summary>
    /// Port 5210 if it's free. If Roadmapper is already listening there, says so. If something else has it,
    /// any free port, so the app still starts.
    /// </summary>
    public static async Task<Address> ChooseAddressAsync()
    {
        var preferred = $"http://localhost:{PreferredPort}";
        if (IsFree(PreferredPort)) return new Address(preferred, false);
        if (await IsRoadmapperAsync(preferred)) return new Address(preferred, true);
        return new Address($"http://localhost:{FreePort()}", false);
    }

    public static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.WriteLine($"Couldn't open a browser ({ex.Message}). Open {url} yourself.");
        }
    }

    public static void ShowBanner(string url, string dbPath)
    {
        try { Console.Title = "Roadmapper"; } catch (IOException) { } // No console window (e.g. output redirected).
        Console.WriteLine();
        Console.WriteLine("  Roadmapper is running.");
        Console.WriteLine($"  Open {url} in your browser.");
        Console.WriteLine($"  Your data: {dbPath}");
        Console.WriteLine("  Close this window (or press Ctrl+C) to stop Roadmapper.");
        Console.WriteLine();
    }

    private static bool IsFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException) { return false; }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<bool> IsRoadmapperAsync(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            return await http.GetStringAsync(url + ProbePath) == ProbeReply;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
}
