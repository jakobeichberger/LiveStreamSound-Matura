using System.IO;
using System.IO.Compression;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LiveStreamSound.App.Services;

/// <summary>
/// One-click diagnostics bundle — used by a panicked teacher post-incident:
/// gather all log files + settings + a system snapshot into a single ZIP on
/// the Desktop. The teacher can email it to the developer without
/// command-line gymnastics.
/// </summary>
public static class DiagnosticsBundleService
{
    /// <summary>Build a diagnostics ZIP. Returns the absolute path of the file written.</summary>
    public static string Build(string roleLabel)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var safeRole = string.Concat((roleLabel ?? "session").Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
        var fileName = $"LiveStreamSound-Diagnose-{DateTime.Now:yyyy-MM-dd-HHmm}-{safeRole}.zip";
        var path = Path.Combine(desktop, fileName);

        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);

        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiveStreamSound");

        // Add log + crash files.
        if (Directory.Exists(appDataRoot))
        {
            foreach (var file in Directory.EnumerateFiles(appDataRoot, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var rel = Path.GetRelativePath(appDataRoot, file);
                    var entry = zip.CreateEntry(Path.Combine("LocalAppData", rel), CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    using var src = File.OpenRead(file);
                    src.CopyTo(entryStream);
                }
                catch { /* best-effort per file */ }
            }
        }

        // Add a system snapshot.
        try
        {
            var infoEntry = zip.CreateEntry("system.txt", CompressionLevel.Optimal);
            using var w = new StreamWriter(infoEntry.Open());
            w.WriteLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
            w.WriteLine($"OS: {Environment.OSVersion}");
            w.WriteLine($"Machine: {Environment.MachineName}");
            w.WriteLine($"User: {Environment.UserName}");
            w.WriteLine($".NET: {Environment.Version}");
            w.WriteLine($"Role at bundle time: {roleLabel}");
            w.WriteLine($"Working dir: {Environment.CurrentDirectory}");
            w.WriteLine();
            w.WriteLine("=== Network adapters ===");
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    w.WriteLine($"- {nic.Name} ({nic.NetworkInterfaceType})");
                    foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                            w.WriteLine($"    {ua.Address}/{ua.PrefixLength}");
                    }
                }
            }
            catch (Exception ex) { w.WriteLine($"  (network enumeration failed: {ex.Message})"); }
        }
        catch { /* best-effort */ }

        return path;
    }
}
