using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace SlayerTools
{
    [BepInPlugin("leo.idleslayer.slayertools", "Slayer Tools", "6.1.0")]
    public class Plugin : BasePlugin
    {
        internal static ManualLogSource L;
        internal static ConfigEntry<KeyCode> KeyMenu, KeySpeed, KeyCoins, KeySP, KeyPortal, KeyRage;
        internal static ConfigEntry<double> CoinsHours, SPPercent;
        internal static ConfigEntry<string> SpeedSteps;
        internal static ConfigEntry<int> Port;
        internal static bool UserWantsToQuit;

        public override void Load()
        {
            L = Log;
            KeyMenu   = Config.Bind("Hotkeys", "Menu", KeyCode.F2, "Open / close the Slayer Tools menu (where you left it)");
            KeySpeed  = Config.Bind("Hotkeys", "Speed", KeyCode.F6, "Cycle the game speed");
            KeyCoins  = Config.Bind("Hotkeys", "Coins", KeyCode.F7, "Add coins");
            KeySP     = Config.Bind("Hotkeys", "SlayerPoints", KeyCode.F8, "Add Slayer Points");
            KeyPortal = Config.Bind("Hotkeys", "Portal", KeyCode.F9, "Portal ready");
            KeyRage   = Config.Bind("Hotkeys", "RageMode", KeyCode.F10, "Rage Mode ready");
            CoinsHours = Config.Bind("Settings", "CoinHours", 1.0, "Hours of production added per press");
            SPPercent  = Config.Bind("Settings", "SPPercent", 10.0, "Percentage of SP added per press (minimum 10)");
            SpeedSteps = Config.Bind("Settings", "Speeds", "1,2,5,10", "Speed steps");
            Port       = Config.Bind("Overlay", "Port", 47823, "Local port (127.0.0.1) used by the overlay");

            ToolsBehaviour.ApplyPendingRestore();
            ToolsBehaviour.CleanupOldDll();

            try
            {
                var h = new Harmony("leo.idleslayer.slayertools");
                h.Patch(AccessTools.Method(typeof(Application), "Quit", Type.EmptyTypes), prefix: new HarmonyMethod(typeof(Plugin), nameof(QuitGuard)));
                h.Patch(AccessTools.Method(typeof(Application), "Quit", new[] { typeof(int) }), prefix: new HarmonyMethod(typeof(Plugin), nameof(QuitGuardCode)));
                h.Patch(AccessTools.Method(typeof(UIManager), "ExitGame"), prefix: new HarmonyMethod(typeof(Plugin), nameof(MarkUserQuit)));
                h.Patch(AccessTools.Method(typeof(UIManager), "WaitForUploadAndQuit"), prefix: new HarmonyMethod(typeof(Plugin), nameof(MarkUserQuit)));
                foreach (var name in new[] { "GetKeyDown", "GetKey", "GetKeyUp" })
                    foreach (var argT in new[] { typeof(KeyCode), typeof(string) })
                    {
                        try
                        {
                            var m = AccessTools.Method(typeof(Input), name, new[] { argT });
                            if (m != null) h.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(KeyGuard)));
                        }
                        catch (Exception e) { Log.LogWarning($"Keyboard blocking for {name}({argT.Name}) failed: {e.Message}"); }
                    }
                foreach (var mName in new[] { "OpenPowers", "OpenUpgrades", "OpenInfoPanel", "OpenQuestsPanel", "OpenBonusesPanel" })
                    try { h.Patch(AccessTools.Method(typeof(ShopManager), mName), postfix: new HarmonyMethod(typeof(Plugin), nameof(GameMenuChanged))); }
                    catch (Exception e) { Log.LogWarning($"Patch {mName}: {e.Message}"); }
                try
                {
                    foreach (var m in typeof(UIManager).GetMethods().Where(m => m.Name == "ToggleShop"))
                        h.Patch(m, postfix: new HarmonyMethod(typeof(Plugin), nameof(ShopToggled)));
                }
                catch (Exception e) { Log.LogWarning("Patch ToggleShop: " + e.Message); }
                h.Patch(AccessTools.Method(typeof(PlayerInventory), "CalculateValues"),
                        prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeRecalc)),
                        postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterRecalc)));
                foreach (var (t, m) in new[] { (typeof(BossMapController), "PlayerHit"), (typeof(BossMapController), "PlayerPoison"), (typeof(BonusMapController), "SawHitted") })
                    try { h.Patch(AccessTools.Method(t, m), prefix: new HarmonyMethod(typeof(Plugin), nameof(NoHit))); }
                    catch (Exception e) { Log.LogWarning($"God mode {m}: {e.Message}"); }
                try { h.Patch(AccessTools.Method(typeof(BossMapController), "TakeDamage"), prefix: new HarmonyMethod(typeof(Plugin), nameof(BossDmg))); }
                catch (Exception e) { Log.LogWarning("One-hit bosses: " + e.Message); }
            }
            catch (Exception e) { Log.LogError("Anti-quit guard failed: " + e); }

            ClassInjector.RegisterTypeInIl2Cpp<ToolsBehaviour>();
            AddComponent<ToolsBehaviour>();
            Bridge.Start(Port.Value);
            Log.LogInfo($"Slayer Tools {ToolsBehaviour.ModVersion} loaded. Open it with the star tab, the star button or {KeyMenu.Value}.");
        }

        private static void MarkUserQuit() => UserWantsToQuit = true;
        private static bool NoHit() => ToolsBehaviour.SkipPlayerHit();
        private static void BossDmg(BossMapController __instance, ref float damage) => ToolsBehaviour.BossDamage(__instance, ref damage);
        private static void GameMenuChanged() { try { ToolsBehaviour.Instance?.OnGameMenuChanged(); } catch { } }
        private static void ShopToggled(bool show) { if (!show) GameMenuChanged(); }

        // While the player types in a menu field, the game must not receive the keys (jump, boost, rage...)
        internal static bool Typing;
        private static bool KeyGuard(ref bool __result) { if (!Typing) return true; __result = false; return false; }
        private static bool QuitGuard()
        {
            if (UserWantsToQuit) return true;
            L.LogInfo("Automatic quit (mod detection) blocked.");
            return false;
        }
        private static bool QuitGuardCode(int exitCode) => QuitGuard();

        // The game recalculates its stats (CalculateValues) on every achievement, purchase, rage, map change...
        // We record the values before, then put our changes back on the ones the recalculation overwrote.
        private static void BeforeRecalc() { try { ToolsBehaviour.Instance?.SnapshotPinned(); } catch { } }
        private static void AfterRecalc() { try { ToolsBehaviour.Instance?.ReapplyPinned(); } catch (Exception e) { L.LogWarning("Reapply: " + e.Message); } }
    }

    // ------------------------------------------------------------------
    // Small local HTTP server (127.0.0.1 only) for the overlay.
    // No game calls here: everything goes through a queue processed in Update().
    // ------------------------------------------------------------------
    internal static class Bridge
    {
        internal class Pending { public string Body; public TaskCompletionSource<string> Done = new TaskCompletionSource<string>(); }

        internal static readonly ConcurrentQueue<Pending> Commands = new ConcurrentQueue<Pending>();
        internal static volatile string State = "{\"ok\":true,\"inGame\":false}";
        internal static volatile string InvProps = "[]";
        internal static volatile string SpProps = "[]";
        internal static long LastPoll;
        private static TcpListener listener;

        internal static bool Polled => Environment.TickCount64 - Interlocked.Read(ref LastPoll) < 3000;

        public static void Start(int port)
        {
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                var t = new Thread(Loop) { IsBackground = true, Name = "SlayerTools-Bridge" };
                t.Start();
            }
            catch (Exception e) { Plugin.L.LogError("Could not open the overlay port: " + e.Message); }
        }

        private static void Loop()
        {
            while (true)
            {
                try
                {
                    var client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(_ => Handle(client));
                }
                catch (Exception e) { Plugin.L.LogWarning("Bridge: " + e.Message); Thread.Sleep(500); }
            }
        }

        private static void Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 3000;
                    var stream = client.GetStream();
                    var head = new StringBuilder();
                    var buf = new byte[1];
                    while (!head.ToString().EndsWith("\r\n\r\n") && head.Length < 8192)
                    {
                        if (stream.Read(buf, 0, 1) <= 0) return;
                        head.Append((char)buf[0]);
                    }
                    var lines = head.ToString().Split("\r\n");
                    var first = lines[0].Split(' ');
                    if (first.Length < 2) return;
                    string method = first[0], path = first[1];
                    int len = 0;
                    foreach (var l in lines)
                        if (l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(l.Substring(15).Trim(), out len);
                    string body = "";
                    if (len > 0 && len < 65536)
                    {
                        var data = new byte[len]; int read = 0;
                        while (read < len) { int n = stream.Read(data, read, len - read); if (n <= 0) break; read += n; }
                        body = Encoding.UTF8.GetString(data, 0, read);
                    }

                    Interlocked.Exchange(ref LastPoll, Environment.TickCount64);
                    string resp;
                    int code = 200;
                    if (method == "GET" && path.StartsWith("/state")) resp = State;
                    else if (method == "GET" && path.StartsWith("/props/inv")) resp = InvProps;
                    else if (method == "GET" && path.StartsWith("/props/sp")) resp = SpProps;
                    else if (method == "POST" && path.StartsWith("/cmd"))
                    {
                        var p = new Pending { Body = body };
                        Commands.Enqueue(p);
                        resp = p.Done.Task.Wait(3000) ? p.Done.Task.Result : "{\"ok\":false,\"msg\":\"The game is not responding (loading?)\"}";
                    }
                    else { code = 404; resp = "{\"ok\":false,\"msg\":\"unknown\"}"; }

                    var bytes = Encoding.UTF8.GetBytes(resp);
                    var hdr = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                    stream.Write(hdr, 0, hdr.Length);
                    stream.Write(bytes, 0, bytes.Length);
                }
                catch { }
            }
        }
    }

    internal static class Ext
    {
        public static T Let<T>(this T o, Action<T> f) { f(o); return o; }
    }

    internal class EditableProp
    {
        public string Name;
        public PropertyInfo Prop;
        public bool IsStatic;
        public bool ReadOnly;
    }

    public partial class ToolsBehaviour : MonoBehaviour
    {
        public ToolsBehaviour(IntPtr ptr) : base(ptr) { Instance = this; LoadPinned(); LoadOptions(); LoadUi(); }
        internal static ToolsBehaviour Instance;

        // ---------- pinned stats (survive recalculations and restarts) ----------
        private readonly Dictionary<string, double> pinned = new Dictionary<string, double>();
        private readonly Dictionary<string, double> before = new Dictionary<string, double>();
        private static string PinFile => Path.Combine(Paths.ConfigPath, "leo.idleslayer.slayertools.pinned.json");

        private void LoadPinned()
        {
            try
            {
                if (!File.Exists(PinFile)) return;
                var d = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(PinFile));
                if (d != null) foreach (var kv in d) pinned[kv.Key] = kv.Value;
                Plugin.L.LogInfo($"{pinned.Count} pinned stat(s) reloaded.");
            }
            catch (Exception e) { Plugin.L.LogWarning("Reading pinned stats: " + e.Message); }
        }

        private void SavePinned()
        {
            try { File.WriteAllText(PinFile, JsonSerializer.Serialize(pinned)); }
            catch (Exception e) { Plugin.L.LogWarning("Saving pinned stats: " + e.Message); }
        }

        // ---------- game UI analysis (used to build a native menu) ----------
        private string DumpUI()
        {
            var path = Path.Combine(Paths.BepInExRootPath, "slayer_ui_dump.txt");
            var sb = new StringBuilder();
            sb.AppendLine("# Slayer Tools - Idle Slayer UI tree  " + DateTime.Now.ToString("s"));
            sb.AppendLine($"# Screen {Screen.width}x{Screen.height}");
            int nodes = 0;
            var seen = new HashSet<int>();
            var canvases = Resources.FindObjectsOfTypeAll<Canvas>();
            foreach (var c in canvases)
            {
                if (c == null) continue;
                var root = c.transform.root;
                if (root == null || !seen.Add(root.gameObject.GetInstanceID())) continue;
                if (root.gameObject.hideFlags != HideFlags.None) continue;   // prefabs / assets that are not instantiated
                bool inScene;
                try { inScene = root.gameObject.scene.IsValid(); } catch { inScene = true; }
                if (!inScene) continue;
                sb.AppendLine();
                sb.AppendLine("=== ROOT " + root.name);
                DumpNode(root, 0, sb, ref nodes);
                if (nodes > 30000) { sb.AppendLine("... (truncated)"); break; }
            }
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        private static string Esc(string s) => (s ?? "").Replace("\n", " ").Replace("\r", " ");

        private void DumpNode(Transform t, int depth, StringBuilder sb, ref int nodes)
        {
            if (++nodes > 30000 || depth > 16) return;
            var go = t.gameObject;
            var line = new StringBuilder();
            line.Append(new string(' ', depth * 2)).Append(go.activeSelf ? "+ " : "- ").Append(t.name);
            try
            {
                var rt = t.TryCast<RectTransform>();
                if (rt != null)
                {
                    var r = rt.rect;
                    line.Append($"  [{r.width:0}x{r.height:0} @ {rt.anchoredPosition.x:0},{rt.anchoredPosition.y:0}]");
                }
            }
            catch { }
            try
            {
                var comps = go.GetComponents<Component>();
                var names = new List<string>();
                foreach (var comp in comps)
                {
                    if (comp == null) continue;
                    string n = comp.GetIl2CppType().Name;
                    if (n == "Transform" || n == "RectTransform" || n == "CanvasRenderer") continue;
                    var img = comp.TryCast<UnityEngine.UI.Image>();
                    if (img != null && img.sprite != null) n += "(" + img.sprite.name + ")";
                    var tmp = comp.TryCast<TMPro.TMP_Text>();
                    if (tmp != null) n += "(\"" + Esc(tmp.text) + "\" " + tmp.fontSize.ToString("0") + "px" + (tmp.font != null ? " " + tmp.font.name : "") + ")";
                    var txt = comp.TryCast<UnityEngine.UI.Text>();
                    if (txt != null) n += "(\"" + Esc(txt.text) + "\")";
                    names.Add(n);
                }
                if (names.Count > 0) line.Append("  {").Append(string.Join(", ", names)).Append('}');
            }
            catch (Exception e) { line.Append("  {? " + e.GetType().Name + "}"); }
            sb.AppendLine(line.ToString());
            for (int i = 0; i < t.childCount; i++) DumpNode(t.GetChild(i), depth + 1, sb, ref nodes);
        }


        // ======================================================================
        // Helpers shared by the menu pages (the UI itself lives in Ui.cs and Pages.cs)
        // ======================================================================
        private bool freezeBoost;
        private List<EditableProp> boostProps;

        private static string EditText(EditableProp p, object target)
        {
            try
            {
                if (!p.IsStatic && target == null) return "";
                double v = Convert.ToDouble(p.Prop.GetValue(p.IsStatic ? null : target));
                if (Math.Abs(v) < 1e6) return v.ToString("0.####", CultureInfo.InvariantCulture);
                return Fmt(v);
            }
            catch { return ""; }
        }

        // unpin = give the stat back to the game, then ask it to recalculate everything
        private void Unpin(string name)
        {
            if (!pinned.TryGetValue(name, out var old)) return;
            pinned.Remove(name);
            SavePinned();
            Recalculate();
            PushUndo("unpin " + Pretty(name), () =>
            {
                var p = FindInv(name); var inv = PlayerInventory.instance;
                if (p != null && inv != null) try { Write(p, inv, old); } catch { }
                pinned[name] = old; SavePinned();
            });
            Toast(Pretty(name) + " unpinned: the game recalculated it");
        }

        // Asks the game to recalculate every stat, without ever letting Slayer Points go down.
        // A recalculation can lower them by a rounding hair; the cloud sync then sees the cloud save as
        // "better", downloads it and reloads the game every few seconds (sending the player back to the menu).
        private void Recalculate()
        {
            var inv = PlayerInventory.instance; if (inv == null) return;
            double sp = SlayerPoints.points, life = SlayerPoints.lifetime;
            try { inv.CalculateValues(); } catch (Exception e) { Plugin.L.LogWarning("Recalculation: " + e.Message); }
            try
            {
                if (SlayerPoints.points < sp) SlayerPoints.points = sp;
                if (SlayerPoints.lifetime < life) SlayerPoints.lifetime = life;
            }
            catch { }
        }

        private void UnpinAll()
        {
            int c = pinned.Count;
            if (c == 0) { Toast("Nothing to unpin"); return; }
            var old = new Dictionary<string, double>(pinned);
            PushUndo("unpin all", () =>
            {
                var inv = PlayerInventory.instance;
                foreach (var kv in old) { var p = FindInv(kv.Key); if (p != null && inv != null) try { Write(p, inv, kv.Value); } catch { } pinned[kv.Key] = kv.Value; }
                SavePinned();
            });
            pinned.Clear();
            SavePinned();
            Recalculate();
            Toast(c + " stat(s) unpinned and recalculated");
        }



        // ---------- Presets: save / apply sets of pinned stats ----------
        private static string PresetFile => Path.Combine(Paths.ConfigPath, "leo.idleslayer.slayertools.presets.json");
        private Dictionary<string, Dictionary<string, double>> presets;

        private Dictionary<string, Dictionary<string, double>> Presets()
        {
            if (presets != null) return presets;
            try
            {
                presets = File.Exists(PresetFile)
                    ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double>>>(File.ReadAllText(PresetFile))
                    : null;
            }
            catch (Exception e) { Plugin.L.LogWarning("Reading presets: " + e.Message); }
            return presets ??= new Dictionary<string, Dictionary<string, double>>();
        }

        private void SavePresets()
        {
            try { File.WriteAllText(PresetFile, JsonSerializer.Serialize(Presets())); }
            catch (Exception e) { Plugin.L.LogWarning("Saving presets: " + e.Message); }
        }

        private void SavePreset(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) { Toast("Give the preset a name"); return; }
            if (pinned.Count == 0) { Toast("No pinned stat to save"); return; }
            Presets()[name] = new Dictionary<string, double>(pinned);
            SavePresets();
            Toast($"Preset \"{name}\" saved ({pinned.Count} stats)");
            NmFillPresets();
        }

        private void ApplyPreset(string name)
        {
            if (!Presets().TryGetValue(name, out var p)) return;
            var inv = PlayerInventory.instance;
            if (inv == null) { Toast("Not in game yet"); return; }
            BackupSave("before-preset");
            var oldPinned = new Dictionary<string, double>(pinned);
            var oldVals = new Dictionary<string, object>();
            foreach (var kv in p) { var pr = FindInv(kv.Key); if (pr != null) try { oldVals[kv.Key] = pr.Prop.GetValue(inv); } catch { } }
            PushUndo("preset " + name, () =>
            {
                var i2 = PlayerInventory.instance; if (i2 == null) return;
                foreach (var kv in oldVals) try { FindInv(kv.Key).Prop.SetValue(i2, kv.Value); } catch { }
                pinned.Clear(); foreach (var kv in oldPinned) pinned[kv.Key] = kv.Value; SavePinned();
            });
            int n = 0;
            foreach (var kv in p)
            {
                var prop = FindInv(kv.Key);
                if (prop == null || prop.ReadOnly) continue;
                try { Write(prop, inv, kv.Value); pinned[kv.Key] = kv.Value; n++; } catch { }
            }
            SavePinned();
            if (lockCoins) lockedCoins = inv.coins;
            Toast($"Preset \"{name}\" applied ({n} stats)");
        }

        private IEnumerable<(Quest q, string kind)> AllQuests()
        {
            var inv = PlayerInventory.instance;
            if (inv == null) yield break;
            if (inv.quests != null) foreach (var q in inv.quests) if (q != null) yield return (q, "Quest");
            if (inv.dailyQuests != null) foreach (var q in inv.dailyQuests) if (q != null && q.active) yield return (q, "Daily");
            if (inv.weeklyQuests != null) foreach (var q in inv.weeklyQuests) if (q != null && q.active) yield return (q, "Weekly");
        }

        private static string QName(Quest q)
        {
            string n = !string.IsNullOrEmpty(q.localizedName) ? q.localizedName : q.name;
            return n ?? "?";
        }

        private static bool QOpen(Quest q)
        {
            try { return !q.isClaimed && (q.CanBeCompleted() || q.IsCompleted()); } catch { return false; }
        }

        private string QLabel(Quest q, string kind)
        {
            bool done = false; try { done = q.IsCompleted(); } catch { }
            return $"[{kind}] {QName(q)}  :  {q.questCurrentGoal:N0}/{q.questGoal:N0}" + (done ? "   (to claim)" : "   > Complete");
        }


        private static string EName(Enemy e) => (!string.IsNullOrEmpty(e.localizedPluralName) ? e.localizedPluralName : !string.IsNullOrEmpty(e.localizedName) ? e.localizedName : e.name) ?? "?";


        private static string CdText(double? cd) => cd == null ? "" : cd.Value <= 0.05 ? "  (ready)" : $"  ({cd.Value:0} s)";

        private static string ValueText(EditableProp p, object target)
        {
            try
            {
                if (!p.IsStatic && target == null) return "-";
                var o = p.Prop.GetValue(p.IsStatic ? null : target);
                return o is bool b ? (b ? "yes" : "no") : Fmt(Convert.ToDouble(o));
            }
            catch { return "?"; }
        }

        private List<EditableProp> BoostProps()
        {
            if (boostProps != null) return boostProps;
            boostProps = Collect(typeof(Boost), false);
            boostProps.AddRange(Collect(typeof(Ability), false));
            boostProps = boostProps.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            return boostProps;
        }

        private string ApplyTo(string key, EditableProp p, object target, string text)
        {
            if (key == "inv" || key == "sp") return SetProp(key, p.Name, text);
            if (target == null) return "Not in game yet";
            if (p.ReadOnly) return p.Name + " is locked";
            RememberProp(p, target, Pretty(p.Name));
            try
            {
                if (p.Prop.PropertyType == typeof(bool))
                {
                    bool v = !(bool)p.Prop.GetValue(target); p.Prop.SetValue(target, v);
                    return Toast(Pretty(p.Name) + " = " + OnOff(v));
                }
                if (!TryParseNumber(text, out double d)) return Toast("Invalid number: " + text);
                Write(p, target, d);
                return Toast(Pretty(p.Name) + " = " + ValueText(p, target));
            }
            catch (Exception e) { return Toast("Error: " + e.GetBaseException().Message); }
        }

        private string ResetBoost()
        {
            var b = PlayerInventory.instance?.boost;
            if (b == null) return Toast("Boost not found");
            if (b.currentCd > 0) b.currentCd = 0.0001f;   // the game moves it to 0 and notifies the UI
            return Toast("Boost ready!");
        }

        private EditableProp FindInv(string name) => (invProps ??= Collect(typeof(PlayerInventory), false)).FirstOrDefault(x => x.Name == name);

        private static double Read(EditableProp p, object target) => Convert.ToDouble(p.Prop.GetValue(target));
        private static void Write(EditableProp p, object target, double d)
        {
            var pt = p.Prop.PropertyType;
            if (pt == typeof(double)) p.Prop.SetValue(target, d);
            else if (pt == typeof(float)) p.Prop.SetValue(target, (float)d);
            else if (pt == typeof(bool)) p.Prop.SetValue(target, d != 0);
            else p.Prop.SetValue(target, (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, d)));
        }

        internal void SnapshotPinned()
        {
            before.Clear();
            var inv = PlayerInventory.instance;
            if (inv == null) return;
            foreach (var name in pinned.Keys)
            {
                var p = FindInv(name);
                if (p != null) try { before[name] = Read(p, inv); } catch { }
            }
        }

        internal void ReapplyPinned()
        {
            var inv = PlayerInventory.instance;
            if (inv == null || pinned.Count == 0) return;
            int n = 0;
            foreach (var kv in pinned)
            {
                var p = FindInv(kv.Key);
                if (p == null) continue;
                try
                {
                    double now = Read(p, inv);
                    // only if the recalculation changed the value: counters (coins...) that the game
                    // does not recalculate stay free, so your purchases cost the normal price.
                    bool recalculated = !before.TryGetValue(kv.Key, out var old) || old != now;
                    if (recalculated && now != kv.Value) { Write(p, inv, kv.Value); n++; }
                }
                catch { }
            }
            if (n > 0) Plugin.L.LogInfo($"{n} stat(s) restored after the game recalculated.");
        }


        private bool inputBroken, overlayVisible = true;
        private int speedIndex, toastSeq;
        private string toast = "";
        private bool freezePortal, freezeRage, lockCoins;
        private double lockedCoins;
        private float nextSnapshot;
        private List<EditableProp> invProps, spProps;

        private static bool Blocked(string n)
        {
            n = n.ToLowerInvariant();
            return n.Contains("jewelsofsoul") || n.Contains("purchase") || n.Contains("adswatched") || n.Contains("starterpack") || n == "initialized";
        }

        private static List<EditableProp> Collect(Type t, bool statics)
        {
            var flags = BindingFlags.Public | BindingFlags.DeclaredOnly | (statics ? BindingFlags.Static : BindingFlags.Instance);
            var list = new List<EditableProp>();
            foreach (var p in t.GetProperties(flags))
            {
                var pt = p.PropertyType;
                if (pt != typeof(double) && pt != typeof(float) && pt != typeof(int) && pt != typeof(bool)) continue;
                if (p.GetMethod == null) continue;
                list.Add(new EditableProp { Name = p.Name, Prop = p, IsStatic = statics, ReadOnly = p.SetMethod == null || Blocked(p.Name) });
            }
            return list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private float toastAt;
        private string RecentToast() => Time.realtimeSinceStartup - toastAt < 4f && !string.IsNullOrEmpty(toast) ? toast : null;

        private string Toast(string msg)
        {
            toast = msg; toastSeq++; toastAt = Time.realtimeSinceStartup;
            Plugin.L.LogMessage(msg);
            Journal(msg);
            return msg;
        }

        // ---------- numbers ----------
        private static readonly string[] Suffixes = { "K", "M", "B", "T", "Qa", "Qi", "Sx", "Sp", "Oc", "No", "Dc" };

        private static string Fmt(double v)
        {
            if (Math.Abs(v) < 1e6) return v.ToString("N0", CultureInfo.InvariantCulture);
            int e = (int)Math.Floor(Math.Log10(Math.Abs(v)) / 3);
            if (e - 1 >= Suffixes.Length) return v.ToString("0.##E+0", CultureInfo.InvariantCulture);
            return (v / Math.Pow(1000, e)).ToString("0.##", CultureInfo.InvariantCulture) + Suffixes[e - 1];
        }

        private static bool TryParseNumber(string s, out double v)
        {
            v = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim().Replace(" ", "").Replace("'", "").Replace(",", ".");
            double mult = 1;
            for (int i = Suffixes.Length - 1; i >= 0; i--)
                if (s.EndsWith(Suffixes[i], StringComparison.OrdinalIgnoreCase) && s.Length > Suffixes[i].Length)
                { mult = Math.Pow(1000, i + 1); s = s.Substring(0, s.Length - Suffixes[i].Length); break; }
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
            v *= mult;
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }

        private static string J(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
        private static string JN(double d) => double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("R", CultureInfo.InvariantCulture);

        // ---------- actions ----------
        private string SetSpeed(float s) { Time.timeScale = s; return Toast("Speed x" + s.ToString(CultureInfo.InvariantCulture)); }

        private void CycleSpeed()
        {
            var parts = Plugin.SpeedSteps.Value.Split(',');
            speedIndex = (speedIndex + 1) % parts.Length;
            float.TryParse(parts[speedIndex].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float s);
            SetSpeed(s <= 0 ? 1f : s);
        }

        private string AddCoins(double hours)
        {
            var inv = PlayerInventory.instance;
            if (inv == null) return Toast("Not in game yet");
            double add = Math.Max(inv.coinsPerSecond * 3600.0 * hours, 1000);
            RememberCoinsAndSP("+" + Fmt(add) + " coins"); ResetRates();
            inv.AddCoins(add);
            if (lockCoins) lockedCoins = inv.coins;
            return Toast("+" + Fmt(add) + " coins");
        }

        private string AddSP(double percent)
        {
            if (PlayerInventory.instance == null) return Toast("Not in game yet");
            double add = Math.Max(SlayerPoints.points * percent / 100.0, 10);
            RememberCoinsAndSP("+" + Fmt(add) + " Slayer Points"); ResetRates();
            SlayerPoints.AddSlayerPoints(add);
            return Toast("+" + Fmt(add) + " Slayer Points");
        }

        private string ResetPortal(bool quiet = false)
        {
            var p = PortalButton.instance;
            if (p == null) return quiet ? "" : Toast("Portal not found");
            p.currentCd = 0;
            return quiet ? "" : Toast("Portal ready!");
        }

        private string ResetRage(bool quiet = false)
        {
            var r = RageModeManager.instance;
            if (r == null) return quiet ? "" : Toast("Rage Mode not found");
            r.currentCd = 0;
            return quiet ? "" : Toast("Rage Mode ready!");
        }

        private string SetProp(string tab, string name, string text)
        {
            var list = tab == "sp" ? (spProps ??= Collect(typeof(SlayerPoints), true)) : (invProps ??= Collect(typeof(PlayerInventory), false));
            var p = list.FirstOrDefault(x => x.Name == name);
            if (p == null) return Toast("Unknown stat: " + name);
            if (p.ReadOnly) return Toast(name + " is locked");
            if (!p.IsStatic && PlayerInventory.instance == null) return Toast("Not in game yet");
            object target = p.IsStatic ? null : (object)PlayerInventory.instance;
            RememberProp(p, target, Pretty(name));
            ResetRates();
            try
            {
                var pt = p.Prop.PropertyType;
                if (pt == typeof(bool))
                {
                    bool v = !(bool)p.Prop.GetValue(target);
                    p.Prop.SetValue(target, v);
                    if (!p.IsStatic) { pinned[name] = v ? 1 : 0; SavePinned(); }
                    return Toast(Pretty(name) + " = " + (v ? "yes" : "no"));
                }
                if (!TryParseNumber(text, out double d)) return Toast("Invalid number: " + text);
                if (pt == typeof(double)) p.Prop.SetValue(target, d);
                else if (pt == typeof(float)) p.Prop.SetValue(target, (float)d);
                else p.Prop.SetValue(target, (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, d)));
                if (name == "coins" && lockCoins) lockedCoins = d;
                if (!p.IsStatic) { pinned[name] = Convert.ToDouble(p.Prop.GetValue(target)); SavePinned(); }
                return Toast(Pretty(name) + " = " + Fmt(Convert.ToDouble(p.Prop.GetValue(target))));
            }
            catch (Exception e) { return Toast("Error: " + e.GetBaseException().Message); }
        }

        private string Run(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var r = doc.RootElement;
                string cmd = r.GetProperty("cmd").GetString();
                double num(string k, double def) => r.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.Number ? x.GetDouble() : def;
                string str(string k) => r.TryGetProperty(k, out var x) ? (x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString()) : "";
                bool flag(string k) => r.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.True;
                string msg;
                switch (cmd)
                {
                    case "coins": msg = AddCoins(num("hours", 1)); break;
                    case "coinsx2":
                        if (PlayerInventory.instance == null) { msg = Toast("Not in game yet"); break; }
                        RememberCoinsAndSP("coins x2"); ResetRates();
                        PlayerInventory.instance.coins *= 2; if (lockCoins) lockedCoins = PlayerInventory.instance.coins;
                        msg = Toast("Coins x2"); break;
                    case "sp": msg = AddSP(num("pct", 10)); break;
                    case "portal": msg = ResetPortal(); break;
                    case "rage": msg = ResetRage(); break;
                    case "speed": msg = SetSpeed((float)Math.Max(0.1, Math.Min(50, num("value", 1)))); break;
                    case "toggle":
                        string n = str("name"); bool v = flag("value");
                        if (n == "freezePortal") freezePortal = v;
                        else if (n == "freezeRage") freezeRage = v;
                        else if (n == "lockCoins") { lockCoins = v; if (PlayerInventory.instance != null) lockedCoins = PlayerInventory.instance.coins; }
                        msg = Toast((v ? "Enabled: " : "Disabled: ") + n); break;
                    case "set": msg = SetProp(str("tab"), str("name"), str("value")); break;
                    case "hide": overlayVisible = false; msg = ""; break;
                    case "unpin":
                        msg = pinned.Remove(str("name")) ? Toast(str("name") + " unpinned (the game will recalculate it)") : "";
                        SavePinned(); break;
                    case "dumpui":
                        msg = Toast("UI analyzed: " + DumpUI()); break;
                    case "unpinall":
                        int c = pinned.Count; pinned.Clear(); SavePinned();
                        msg = Toast(c + " stat(s) unpinned"); break;
                    default: msg = "Unknown command"; break;
                }
                BuildSnapshot(true);
                return "{\"ok\":true,\"msg\":" + J(msg) + "}";
            }
            catch (Exception e) { return "{\"ok\":false,\"msg\":" + J(e.GetBaseException().Message) + "}"; }
        }

        // ---------- snapshot for the overlay ----------
        private string PropsJson(List<EditableProp> list, object target)
        {
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (var p in list)
            {
                string val;
                try
                {
                    var o = p.Prop.GetValue(target);
                    val = o is bool b ? (b ? "true" : "false") : JN(Convert.ToDouble(o));
                }
                catch { val = "null"; }
                if (!first) sb.Append(',');
                first = false;
                string type = p.Prop.PropertyType == typeof(bool) ? "bool" : p.Prop.PropertyType == typeof(int) ? "int" : "num";
                sb.Append("{\"n\":").Append(J(p.Name)).Append(",\"t\":\"").Append(type).Append("\",\"v\":").Append(val)
                  .Append(",\"ro\":").Append(p.ReadOnly ? "true" : "false")
                  .Append(",\"pin\":").Append(!p.IsStatic && pinned.ContainsKey(p.Name) ? "true" : "false").Append('}');
            }
            return sb.Append(']').ToString();
        }

        private void BuildSnapshot(bool force = false)
        {
            if (!force && (Time.realtimeSinceStartup < nextSnapshot || !Bridge.Polled)) return;
            nextSnapshot = Time.realtimeSinceStartup + 0.25f;
            var inv = PlayerInventory.instance;
            bool inGame = inv != null;
            var sb = new StringBuilder("{\"ok\":true,\"version\":\"6.1.0\"");
            sb.Append(",\"inGame\":").Append(inGame ? "true" : "false");
            sb.Append(",\"visible\":").Append(overlayVisible ? "true" : "false");
            sb.Append(",\"speed\":").Append(JN(Time.timeScale));
            sb.Append(",\"freezePortal\":").Append(freezePortal ? "true" : "false");
            sb.Append(",\"freezeRage\":").Append(freezeRage ? "true" : "false");
            sb.Append(",\"lockCoins\":").Append(lockCoins ? "true" : "false");
            sb.Append(",\"pinned\":").Append(pinned.Count);
            sb.Append(",\"toast\":").Append(J(toast)).Append(",\"toastSeq\":").Append(toastSeq);
            if (inGame)
            {
                try
                {
                    sb.Append(",\"coins\":").Append(JN(inv.coins));
                    sb.Append(",\"cps\":").Append(JN(inv.coinsPerSecond));
                    sb.Append(",\"sp\":").Append(JN(SlayerPoints.points));
                    sb.Append(",\"souls\":").Append(JN(SlayerPoints.soulsLeft));
                    var portal = PortalButton.instance; if (portal != null) sb.Append(",\"portalCd\":").Append(JN(portal.currentCd));
                    var rage = RageModeManager.instance; if (rage != null) sb.Append(",\"rageCd\":").Append(JN(rage.currentCd));
                }
                catch { }
                invProps ??= Collect(typeof(PlayerInventory), false);
                Bridge.InvProps = PropsJson(invProps, inv);
            }
            spProps ??= Collect(typeof(SlayerPoints), true);
            Bridge.SpProps = PropsJson(spProps, null);
            Bridge.State = sb.Append('}').ToString();
        }

        private bool appliedOnLoad;
        private void Update()
        {
            // On load: ask the game to recalculate, our patch then puts the computed stats back
            // (without touching counters such as coins, already loaded from the save).
            if (!appliedOnLoad && PlayerInventory.instance != null && pinned.Count > 0)
            { appliedOnLoad = true; Recalculate(); }
            for (int i = 0; i < 20 && Bridge.Commands.TryDequeue(out var p); i++)
                p.Done.TrySetResult(Run(p.Body));

            try
            {
                if (freezePortal) ResetPortal(true);
                if (freezeRage) ResetRage(true);
                if (lockCoins && PlayerInventory.instance != null && PlayerInventory.instance.coins < lockedCoins)
                    PlayerInventory.instance.coins = lockedCoins;
            }
            catch { }

            try { FeaturesTick(); } catch (Exception e) { Plugin.L.LogWarning("5.0 features: " + e.Message); }
            NmUpdate();
            try { BuildSnapshot(); } catch (Exception e) { Plugin.L.LogWarning("Snapshot: " + e.Message); }

            if (inputBroken) return;
            try
            {
                MenuHotkeys();
                if (Input.GetKeyDown(Plugin.KeySpeed.Value)) CycleSpeed();
                if (Input.GetKeyDown(Plugin.KeyCoins.Value)) AddCoins(Plugin.CoinsHours.Value);
                if (Input.GetKeyDown(Plugin.KeySP.Value)) AddSP(Plugin.SPPercent.Value);
                if (Input.GetKeyDown(Plugin.KeyPortal.Value)) ResetPortal();
                if (Input.GetKeyDown(Plugin.KeyRage.Value)) ResetRage();
                if (Input.GetKeyDown(KeyCode.F11)) { try { Toast("UI analyzed: " + DumpUI()); } catch (Exception e) { Toast("Analysis failed: " + e.GetBaseException().Message); } }
            }
            catch (Exception e)
            {
                inputBroken = true;
                Plugin.L.LogError("Keyboard unavailable: " + e);
            }
        }
    }
}
