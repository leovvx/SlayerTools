using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
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

        // Plain SHA-256 (FIPS 180-4). Built in so the plugin does not depend on a cryptography assembly
        // whose name differs between .NET 6 (BepInEx) and .NET 8.
        private static readonly uint[] K256 = {
            0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
            0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
            0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
            0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2 };

        internal static string Sha256Hex(byte[] data)
        {
            uint[] h = { 0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19 };
            long bitLen = (long)data.Length * 8;
            int padded = ((data.Length + 9 + 63) / 64) * 64;
            var msg = new byte[padded];
            Buffer.BlockCopy(data, 0, msg, 0, data.Length);
            msg[data.Length] = 0x80;
            for (int i = 0; i < 8; i++) msg[padded - 1 - i] = (byte)(bitLen >> (8 * i));
            var w = new uint[64];
            static uint R(uint x, int n) => (x >> n) | (x << (32 - n));
            for (int off = 0; off < padded; off += 64)
            {
                for (int i = 0; i < 16; i++) w[i] = (uint)(msg[off + 4 * i] << 24 | msg[off + 4 * i + 1] << 16 | msg[off + 4 * i + 2] << 8 | msg[off + 4 * i + 3]);
                for (int i = 16; i < 64; i++)
                {
                    uint s0 = R(w[i - 15], 7) ^ R(w[i - 15], 18) ^ (w[i - 15] >> 3), s1 = R(w[i - 2], 17) ^ R(w[i - 2], 19) ^ (w[i - 2] >> 10);
                    w[i] = w[i - 16] + s0 + w[i - 7] + s1;
                }
                uint a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
                for (int i = 0; i < 64; i++)
                {
                    uint t1 = hh + (R(e, 6) ^ R(e, 11) ^ R(e, 25)) + ((e & f) ^ (~e & g)) + K256[i] + w[i];
                    uint t2 = (R(a, 2) ^ R(a, 13) ^ R(a, 22)) + ((a & b) ^ (a & c) ^ (b & c));
                    hh = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
                }
                h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
            }
            var sb = new System.Text.StringBuilder(64);
            foreach (var x in h) sb.Append(x.ToString("x8"));
            return sb.ToString();
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
                    if (Sha256Hex(bytes) != UpdSha) throw new Exception("checksum mismatch, download refused");
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
