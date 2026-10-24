using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SlayerTools
{
    // ======================================================================
    // Self-update from the GitHub releases of leovvx/SlayerTools.
    //
    // - On startup (if enabled) the latest release is checked in the background.
    // - Nothing is installed without a click on "Install".
    // - The zip must come from this repository over HTTPS, and its SHA-256 must match
    //   the "SHA256: <hex>" line of the release notes (written by scripts/publish-releases.ps1).
    // - Windows cannot overwrite a loaded DLL but can rename it: SlayerTools.dll becomes
    //   SlayerTools.dll.old, the new one takes its place, and is used from the next launch.
    // ======================================================================
    public partial class ToolsBehaviour
    {
        internal const string UpdateRepo = "leovvx/SlayerTools";

        internal enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Installed, Failed }
        internal static volatile UpdateState Upd = UpdateState.Idle;
        internal static string UpdVersion, UpdNotes, UpdZipUrl, UpdSha, UpdError;
        private bool updStarted;

        private static string PluginPath => Assembly.GetExecutingAssembly().Location;

        /// <summary>Called from Plugin.Load: removes the previous DLL left by an update.</summary>
        internal static void CleanupOldDll()
        {
            try { var old = PluginPath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
        }

        private static HttpClient Http()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("SlayerTools/" + ModVersion);
            h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return h;
        }

        private static Version ParseVer(string s)
        {
            var m = Regex.Match(s ?? "", @"(\d+)\.(\d+)\.(\d+)");
            return m.Success ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) : new Version(0, 0, 0);
        }

        private void CheckForUpdate(bool manual = false)
        {
            if (Upd == UpdateState.Checking || Upd == UpdateState.Downloading) return;
            Upd = UpdateState.Checking; UpdError = null;
            Task.Run(async () =>
            {
                try
                {
                    using var h = Http();
                    var json = await h.GetStringAsync($"https://api.github.com/repos/{UpdateRepo}/releases/latest");
                    using var doc = JsonDocument.Parse(json);
                    var r = doc.RootElement;
                    var tag = r.GetProperty("tag_name").GetString();
                    var body = r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                    string zip = null;
                    foreach (var a in r.GetProperty("assets").EnumerateArray())
                    {
                        var name = a.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { zip = a.GetProperty("browser_download_url").GetString(); break; }
                    }
                    UpdVersion = tag?.TrimStart('v');
                    UpdNotes = string.Join("  ", body.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("-")).Take(3));
                    var sha = Regex.Match(body, @"SHA256:\s*([0-9a-fA-F]{64})");
                    UpdSha = sha.Success ? sha.Groups[1].Value.ToLowerInvariant() : null;
                    UpdZipUrl = zip;
                    Upd = ParseVer(tag) > ParseVer(ModVersion) && zip != null ? UpdateState.Available : UpdateState.UpToDate;
                    Plugin.L.LogInfo(Upd == UpdateState.Available ? $"Update available: {tag}" : $"Slayer Tools is up to date ({tag} is the latest release).");
                }
                catch (Exception e)
                {
                    UpdError = e.GetBaseException().Message;
                    Upd = UpdateState.Failed;
                    Plugin.L.LogWarning("Update check failed: " + UpdError);
                }
            });
        }

        private void InstallUpdate()
        {
            if (Upd != UpdateState.Available || UpdZipUrl == null) return;
            if (UpdSha == null) { Toast("This release has no SHA256 line in its notes: install it by hand"); return; }
            if (!UpdZipUrl.StartsWith($"https://github.com/{UpdateRepo}/releases/download/", StringComparison.OrdinalIgnoreCase))
            { Toast("Unexpected download address, update refused"); return; }
            Upd = UpdateState.Downloading;
            Task.Run(async () =>
            {
                string dll = PluginPath, old = dll + ".old", tmp = dll + ".new";
                try
                {
                    using var h = Http();
                    var bytes = await h.GetByteArrayAsync(UpdZipUrl);
                    using (var sha = SHA256.Create())
                    {
                        var got = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                        if (got != UpdSha) throw new Exception("checksum mismatch, download refused");
                    }
                    using (var zip = new ZipArchive(new MemoryStream(bytes)))
                    {
                        var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals("SlayerTools.dll", StringComparison.OrdinalIgnoreCase))
                                    ?? throw new Exception("SlayerTools.dll not found in the zip");
                        using var s = entry.Open();
                        using var f = File.Create(tmp);
                        s.CopyTo(f);
                    }
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(dll, old);                 // allowed even while the DLL is loaded
                    try { File.Move(tmp, dll); }
                    catch { File.Move(old, dll); throw; } // put the current one back
                    Upd = UpdateState.Installed;
                    Plugin.L.LogInfo($"Slayer Tools {UpdVersion} installed, restart the game to use it.");
                }
                catch (Exception e)
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    UpdError = e.GetBaseException().Message;
                    Upd = UpdateState.Failed;
                    Plugin.L.LogWarning("Update failed: " + UpdError);
                }
            });
        }

        private string UpdateLine()
        {
            switch (Upd)
            {
                case UpdateState.Available: return Col(CGreen, $"Update {UpdVersion} available") + (string.IsNullOrEmpty(UpdNotes) ? "" : "\n" + Small(Col(CGrey, UpdNotes)));
                case UpdateState.Downloading: return Col(CGold, $"Downloading {UpdVersion}...");
                case UpdateState.Installed: return Col(CGreen, $"Version {UpdVersion} installed: restart the game to use it.");
                default: return "";
            }
        }

        private string UpdateStatus()
        {
            switch (Upd)
            {
                case UpdateState.Checking: return "Checking...";
                case UpdateState.UpToDate: return Col(CGreen, $"Up to date ({ModVersion})");
                case UpdateState.Available: return Col(CGreen, $"{UpdVersion} available");
                case UpdateState.Downloading: return Col(CGold, "Downloading...");
                case UpdateState.Installed: return Col(CGreen, $"{UpdVersion} installed, restart the game");
                case UpdateState.Failed: return Col(CRed, "Failed: " + UpdError);
                default: return Col(CGrey, "Not checked yet");
            }
        }

        private void UpdateTick()
        {
            if (updStarted || !ui.CheckUpdates) return;
            updStarted = true;
            CheckForUpdate();
        }
    }
}
