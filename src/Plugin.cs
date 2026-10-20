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
    [BepInPlugin("leo.idleslayer.slayertools", "Slayer Tools", "5.2.0")]
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
            KeyMenu   = Config.Bind("Hotkeys", "Overlay", KeyCode.F2, "Show / hide the overlay");
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
            Log.LogInfo($"Slayer Tools 5.0 loaded. Overlay on 127.0.0.1:{Port.Value}, F2 to show it.");
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
        public ToolsBehaviour(IntPtr ptr) : base(ptr) { Instance = this; LoadPinned(); LoadOptions(); }
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
        // Full native menu: a "Slayer Tools" button in the game menu, with pages
        // built from the game's real UI elements (buttons, text field).
        // ======================================================================
        private GameObject nmTemplate, nmInputTemplate, nmGridTemplate;
        private bool nmBuildFailed;
        private Transform nmContent;
        private readonly Dictionary<string, GameObject> nmPages = new Dictionary<string, GameObject>();
        private readonly List<GameObject> nmHidden = new List<GameObject>();
        private readonly List<(TMPro.TextMeshProUGUI txt, Func<string> label, GameObject page)> nmLabels = new List<(TMPro.TextMeshProUGUI, Func<string>, GameObject)>();
        private string nmPage;
        private bool nmBroken;
        private float nmNextCheck, nmNextRefresh;
        private Dictionary<string, Sprite> nmSprites;
        private bool freezeBoost;

        // stat editor
        private class NmRow { public GameObject go; public EditableProp prop; public string source; }
        private readonly Dictionary<string, List<NmRow>> nmRows = new Dictionary<string, List<NmRow>>();
        private readonly Dictionary<string, TMPro.TMP_InputField> nmSearch = new Dictionary<string, TMPro.TMP_InputField>();
        private readonly Dictionary<string, TMPro.TMP_InputField> nmValue = new Dictionary<string, TMPro.TMP_InputField>();
        private readonly Dictionary<string, EditableProp> nmSelected = new Dictionary<string, EditableProp>();
        private readonly Dictionary<string, string> nmLastFilter = new Dictionary<string, string>();
        private List<EditableProp> boostProps;
        private readonly List<(TMPro.TMP_InputField input, EditableProp prop, string key, Func<object> target)> nmRowInputs = new List<(TMPro.TMP_InputField, EditableProp, string, Func<object>)>();

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
            if (!pinned.Remove(name)) return;
            SavePinned();
            try { PlayerInventory.instance?.CalculateValues(); } catch (Exception e) { Plugin.L.LogWarning("Recalculation: " + e.Message); }
            Toast(name + " unpinned: value recalculated by the game");
        }

        private void UnpinAll()
        {
            int c = pinned.Count;
            pinned.Clear();
            SavePinned();
            try { PlayerInventory.instance?.CalculateValues(); } catch (Exception e) { Plugin.L.LogWarning("Recalculation: " + e.Message); }
            Toast(c + " stat(s) unpinned and recalculated");
        }

        private Sprite NmSprite(string name)
        {
            if (nmSprites == null)
            {
                nmSprites = new Dictionary<string, Sprite>();
                foreach (var sp in Resources.FindObjectsOfTypeAll<Sprite>())
                    if (sp != null && !nmSprites.ContainsKey(sp.name)) nmSprites[sp.name] = sp;
            }
            return nmSprites.TryGetValue(name, out var s) ? s : null;
        }

        private static void Strip(GameObject go)
        {
            foreach (var st in go.GetComponentsInChildren<StartingText>(true)) UnityEngine.Object.DestroyImmediate(st);
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == "Alert Icon") t.gameObject.SetActive(false);
            var b = go.GetComponent<UnityEngine.UI.Button>();
            if (b != null) b.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        }

        // --- building blocks ---
        private GameObject NmBox(Transform parent, string name, bool vertical, float spacing = 10, float height = -1)
        {
            var go = new GameObject(name);
            go.AddComponent<RectTransform>();
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            if (vertical)
            {
                var v = go.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
                v.spacing = spacing; v.childControlWidth = true; v.childControlHeight = true;
                v.childForceExpandWidth = true; v.childForceExpandHeight = false;
                var f = go.AddComponent<UnityEngine.UI.ContentSizeFitter>();
                f.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            }
            else
            {
                var h = go.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                h.spacing = spacing; h.childControlWidth = true; h.childControlHeight = true;
                h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            }
            if (height > 0)
            {
                var le = go.AddComponent<UnityEngine.UI.LayoutElement>();
                le.minHeight = height; le.preferredHeight = height;
            }
            return go;
        }

        private static void Size(GameObject go, float height, float width = -1, float flex = -1)
        {
            var le = go.GetComponent<UnityEngine.UI.LayoutElement>() ?? go.AddComponent<UnityEngine.UI.LayoutElement>();
            if (height > 0) { le.minHeight = height; le.preferredHeight = height; }
            if (width > 0) { le.minWidth = width; le.preferredWidth = width; }
            if (flex >= 0) le.flexibleWidth = flex;
        }

        private GameObject NmButton(Transform parent, Func<string> label, string sprite, Action onClick, GameObject page, bool icon = true)
        {
            var go = UnityEngine.Object.Instantiate(nmTemplate, parent, false);
            go.name = "ST Button";
            go.SetActive(true);
            Strip(go);
            var txt = go.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (txt != null)
            {
                txt.enableAutoSizing = true; txt.fontSizeMin = 9; txt.fontSizeMax = 19;
                txt.text = label();
                nmLabels.Add((txt, label, page));
            }
            var iconT = go.transform.Find("Background/Content/Image");
            if (iconT != null)
            {
                if (!icon) iconT.gameObject.SetActive(false);
                else { var sp = NmSprite(sprite); var img = iconT.GetComponent<UnityEngine.UI.Image>(); if (sp != null && img != null) img.sprite = sp; }
            }
            if (!icon)
            {
                var bgT = go.transform.Find("Background")?.GetComponent<RectTransform>();
                if (bgT != null) { bgT.anchorMin = Vector2.zero; bgT.anchorMax = Vector2.one; bgT.offsetMin = new Vector2(-3, -1.5f); bgT.offsetMax = new Vector2(3, 3.5f); }
                var ctT = go.transform.Find("Background/Content")?.GetComponent<RectTransform>();
                if (ctT != null) { ctT.anchorMin = Vector2.zero; ctT.anchorMax = Vector2.one; ctT.offsetMin = new Vector2(8, 6); ctT.offsetMax = new Vector2(-8, -4); }
                if (txt != null) { var r = txt.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero; }
            }
            var btn = go.GetComponent<UnityEngine.UI.Button>();
            if (onClick != null) btn.onClick.AddListener((Action)(() =>
            {
                try { onClick(); NmRefresh(true); } catch (Exception e) { Toast("Error: " + e.GetBaseException().Message); }
            }));
            return go;
        }

        private TMPro.TMP_InputField NmInput(Transform parent, string placeholder)
        {
            var go = UnityEngine.Object.Instantiate(nmInputTemplate, parent, false);
            go.name = "ST Input";
            go.SetActive(true);
            foreach (var st in go.GetComponentsInChildren<StartingText>(true)) UnityEngine.Object.DestroyImmediate(st);
            var input = go.GetComponent<TMPro.TMP_InputField>();
            input.onEndEdit = new TMPro.TMP_InputField.SubmitEvent();
            input.onSubmit = new TMPro.TMP_InputField.SubmitEvent();
            input.onValueChanged = new TMPro.TMP_InputField.OnChangeEvent();
            input.onSelect = new TMPro.TMP_InputField.SelectionEvent();
            input.onDeselect = new TMPro.TMP_InputField.SelectionEvent();
            input.characterLimit = 64;
            input.contentType = TMPro.TMP_InputField.ContentType.Standard;
            input.lineType = TMPro.TMP_InputField.LineType.SingleLine;
            input.interactable = true;
            input.text = "";
            var img = go.GetComponent<UnityEngine.UI.Image>() ?? go.AddComponent<UnityEngine.UI.Image>();
            var sp = NmSprite("Single Button Square") ?? NmSprite("Single Button");
            if (sp != null) { img.sprite = sp; img.type = UnityEngine.UI.Image.Type.Sliced; }
            img.color = new Color(0.75f, 0.75f, 0.8f, 1f);
            input.targetGraphic = img;
            if (input.textComponent != null)
            {
                input.textComponent.text = "";
                input.textComponent.enableAutoSizing = true; input.textComponent.fontSizeMin = 10; input.textComponent.fontSizeMax = 20;
                input.textComponent.alignment = TMPro.TextAlignmentOptions.Center;
                if (input.placeholder == null)
                {
                    var ph = UnityEngine.Object.Instantiate(input.textComponent.gameObject, input.textComponent.transform.parent, false);
                    ph.name = "Placeholder";
                    var pt = ph.GetComponent<TMPro.TextMeshProUGUI>();
                    pt.text = placeholder; pt.color = new Color(1, 1, 1, 0.45f);
                    input.placeholder = pt;
                }
            }
            var area = go.transform.Find("Text Area") as RectTransform ?? go.transform.Find("Text Area")?.GetComponent<RectTransform>();
            if (area != null) { area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one; area.offsetMin = new Vector2(10, 4); area.offsetMax = new Vector2(-10, -4); }
            foreach (var t in go.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
            { var r = t.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero; }
            Size(go, 50, -1, 1);
            return input;
        }

        private GameObject NmGrid(Transform parent, string name)
        {
            var g = UnityEngine.Object.Instantiate(nmGridTemplate, parent, false);
            g.name = name;
            for (int i = g.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(g.transform.GetChild(i).gameObject);
            g.SetActive(true);
            return g;
        }

        private GameObject NmNewPage(string key)
        {
            var page = NmBox(nmContent, "Slayer Tools " + key, true, 12);
            page.SetActive(false);
            nmPages[key] = page;
            return page;
        }

        // --- page construction ---
        private void NmBuild()
        {
            var info = InfoPanel.instance;
            if (info == null) return;
            nmContent = info.transform;
            var buttons = nmContent.Find("Buttons");
            if (buttons == null) return;
            var news = buttons.Find("News") ?? buttons.Find("Upgrades");
            if (news == null) return;
            nmGridTemplate = buttons.gameObject;
            nmTemplate = news.gameObject;
            foreach (var f in Resources.FindObjectsOfTypeAll<TMPro.TMP_InputField>())
                if (f != null && f.gameObject.hideFlags == HideFlags.None && f.textComponent != null) { nmInputTemplate = f.gameObject; break; }

            NmBuildEntryPoints();

            // ---------- Home ----------
            var home = NmNewPage("home");
            NmButton(home.transform, () => StatsLine(), "Star", null, home, false).Let(g => Size(g, 90));
            var hg = NmGrid(home.transform, "grid").transform;
            NmButton(hg, () => "< Back to game", "Up Arrow", NmClose, home);
            NmButton(hg, () => "Actions", "Star", () => NmShow("actions"), home);
            NmButton(hg, () => "Events", "Star Silver", () => NmShow("events"), home);
            NmButton(hg, () => "Combat & chests", "Face", () => NmShow("combat"), home);
            NmButton(hg, () => "Dashboard", "news", () => NmShow("dash"), home);
            NmButton(hg, () => "Inventory", "Checklist", () => NmShow("inv"), home);
            NmButton(hg, () => "Slayer Points", "Face", () => NmShow("sp"), home);
            NmButton(hg, () => "Boost", "Up Arrow", () => NmShow("boost"), home);
            NmButton(hg, () => "Quests", "Checklist", () => NmShow("quests"), home);
            NmButton(hg, () => "Enemies killed", "book_11", () => NmShow("enemies"), home);
            NmButton(hg, () => "Presets", "Star Silver", () => NmShow("presets"), home);
            NmButton(hg, () => "Bag (resources)", "drops_34", () => NmShow("bag"), home);
            NmButton(hg, () => "Divinities", "Face", () => NmShow("divin"), home);
            NmButton(hg, () => "Log", "book_11", () => NmShow("log"), home);
            NmButton(hg, () => "Settings", "drops_34", () => NmShow("settings"), home);
            NmButton(hg, UndoLabel, "Up Arrow", DoUndo, home);

            // ---------- Actions ----------
            var act = NmNewPage("actions");
            NmButton(act.transform, () => StatsLine(), "Star", null, act, false).Let(g => Size(g, 90));
            var ag = NmGrid(act.transform, "grid").transform;
            NmButton(ag, () => "< Back", "Up Arrow", () => NmShow("home"), act);
            NmButton(ag, UndoLabel, "Up Arrow", DoUndo, act);
            NmButton(ag, () => "+1 hour of coins", "Star", () => AddCoins(1), act);
            NmButton(ag, () => "+1 day of coins", "Star", () => AddCoins(24), act);
            NmButton(ag, () => "+1 week of coins", "Star Silver", () => AddCoins(168), act);
            NmButton(ag, () => "Coins x2", "Star", () => Run("{\"cmd\":\"coinsx2\"}"), act);
            NmButton(ag, () => "Coins locked: " + OnOff(lockCoins), "drops_34", () =>
            {
                lockCoins = !lockCoins;
                if (PlayerInventory.instance != null) lockedCoins = PlayerInventory.instance.coins;
            }, act);
            NmButton(ag, () => "Slayer Points +10%", "Face", () => AddSP(10), act);
            NmButton(ag, () => "Slayer Points +50%", "Face", () => AddSP(50), act);
            NmButton(ag, () => "Slayer Points x2", "Face", () => AddSP(100), act);
            NmButton(ag, () => "Portal ready" + CdText(PortalButton.instance?.currentCd), "Checklist", () => ResetPortal(), act);
            NmButton(ag, () => "Rage Mode ready" + CdText(RageModeManager.instance?.currentCd), "Checklist", () => ResetRage(), act);
            NmButton(ag, () => "Portal always ready: " + OnOff(freezePortal), "drops_34", () => freezePortal = !freezePortal, act);
            NmButton(ag, () => "Rage always ready: " + OnOff(freezeRage), "drops_34", () => freezeRage = !freezeRage, act);
            NmButton(ag, () => "Game speed x" + Time.timeScale.ToString("0.#", CultureInfo.InvariantCulture), "news", () =>
            {
                float[] steps = { 1, 2, 5, 10, 20 };
                int i = Array.FindIndex(steps, x => Mathf.Approximately(x, Time.timeScale));
                SetSpeed(steps[(i + 1) % steps.Length]);
            }, act);
            NmButton(ag, () => "Normal speed (x1)", "news", () => SetSpeed(1), act);

            // ---------- Editors ----------
            NmBuildEditor("inv", "Inventory", () => (invProps ??= Collect(typeof(PlayerInventory), false)), () => PlayerInventory.instance);
            NmBuildEditor("sp", "Slayer Points", () => (spProps ??= Collect(typeof(SlayerPoints), true)), () => null);
            NmBuildEditor("boost", "Boost", BoostProps, () => PlayerInventory.instance?.boost, page =>
            {
                var bg = NmGrid(page.transform, "grid").transform;
                NmButton(bg, () => "Boost ready" + CdText(PlayerInventory.instance?.boost?.currentCd), "Up Arrow", () => ResetBoost(), page);
                NmButton(bg, () => "Boost always ready: " + OnOff(freezeBoost), "drops_34", () => freezeBoost = !freezeBoost, page);
            });

            // ---------- Settings ----------
            var set = NmNewPage("settings");
            NmButton(set.transform, () => RecentToast() ?? "Slayer Tools settings", "news", null, set, false).Let(g => Size(g, 54));
            var sg = NmGrid(set.transform, "grid").transform;
            NmButton(sg, () => "< Back", "Up Arrow", () => NmShow("home"), set);
            NmButton(sg, () => "Pinned stats: " + pinned.Count, "Checklist", null, set);
            NmButton(sg, () => "Unpin all", "drops_34", UnpinAll, set);
            NmButton(sg, () => "Analyze the UI", "book_11", () => Toast("UI analyzed: " + DumpUI()), set);
            NmButton(sg, UndoLabel, "Up Arrow", DoUndo, set);
            NmBuildSaveSettings(sg, set);
            NmButton(set.transform, () =>
                "Edited stats are pinned: the game no longer overwrites them\n" +
                "(achievements, purchases, rage...), even after a restart.\n" +
                "Keys: F6 speed  F7 coins  F8 SP  F9 portal  F10 rage  F11 analyze",
                "news", null, set, false).Let(g => Size(g, 110));

            NmBuildFeaturePages();
            NmBuildPresets();
            NmBuildQuests();
            NmBuildEnemies();
            Plugin.L.LogInfo("Full native menu added to the game menu.");
        }

        private void NmBuildEditor(string key, string title, Func<List<EditableProp>> props, Func<object> target, Action<GameObject> extra = null)
        {
            var page = NmNewPage(key);
            var top = NmGrid(page.transform, "grid").transform;
            NmButton(top, () => "< Back", "Up Arrow", () => NmShow("home"), page);
            NmButton(top, () => title, "Checklist", null, page);
            NmButton(top, UndoLabel, "Up Arrow", DoUndo, page);
            extra?.Invoke(page);

            if (nmInputTemplate == null)
            {
                NmButton(page.transform, () => "No text field found in the game: use the overlay to edit values.", "news", null, page, false).Let(g => Size(g, 70));
                return;
            }

            // search
            var searchRow = NmBox(page.transform, "search", false, 10, 54);
            NmButton(searchRow.transform, () => "Search", "news", null, page, false).Let(g => Size(g, 54, 150, 0));
            nmSearch[key] = NmInput(searchRow.transform, "e.g. ruby, chance, damage...");

            // info line
            NmButton(page.transform, () => RecentToast() ?? "Type a value then Enter.  Max = 100.  Fav = top of the list.", "news", null, page, false).Let(g => Size(g, 54));

            // stat list: [name] [value box, Enter = apply] [Unpin]
            var list = NmBox(page.transform, "list", true, 6);
            var rows = new List<NmRow>();
            foreach (var p in props())
            {
                var prop = p;
                var row = new NmRow { prop = prop, source = key };
                row.go = NmBox(list.transform, "row " + prop.Name, false, 8, 46);
                NmButton(row.go.transform, () => (IsFav(key, prop.Name) ? "<color=#FFD24A>></color> " : "") + (key == "inv" && pinned.ContainsKey(prop.Name) ? "* " : "") + Pretty(prop.Name), "news", null, page, false).Let(g => Size(g, 46, -1, 1));
                if (prop.ReadOnly)
                {
                    NmButton(row.go.transform, () => ValueText(prop, target()) + "  (locked)", "news", null, page, false).Let(g => Size(g, 46, 250, 0));
                }
                else if (prop.Prop.PropertyType == typeof(bool))
                {
                    NmButton(row.go.transform, () => ValueText(prop, target()) == "yes" ? "YES" : "no", "drops_34", () => Toast(ApplyTo(key, prop, target(), null)), page, false).Let(g => Size(g, 46, 250, 0));
                }
                else
                {
                    if (IsChance(prop.Name))
                        NmButton(row.go.transform, () => "Max", "Star", () => Quick(key, prop, target, 1, 100), page, false).Let(g => Size(g, 46, 72, 0));
                    var input = NmInput(row.go.transform, "value");
                    Size(input.gameObject, 46, 210, 0);
                    input.text = EditText(prop, target());
                    input.onSubmit.AddListener((Action<string>)(txt =>
                    {
                        try
                        {
                            if (txt == EditText(prop, target())) return;   // nothing changed
                            Toast(ApplyTo(key, prop, target(), txt));
                            input.text = EditText(prop, target());
                            NmRefresh(true);
                        }
                        catch (Exception e) { Toast("Error: " + e.GetBaseException().Message); }
                    }));
                    nmRowInputs.Add((input, prop, key, target));
                }
                if (key == "inv")
                    NmButton(row.go.transform, () => pinned.ContainsKey(prop.Name) ? "Unpin" : "-", "drops_34", () =>
                    {
                        if (pinned.ContainsKey(prop.Name)) Unpin(prop.Name);
                    }, page, false).Let(g => Size(g, 46, 120, 0));
                NmButton(row.go.transform, () => IsFav(key, prop.Name) ? "<color=#FFD24A>FAV</color>" : "fav", "news", () => ToggleFav(key, prop.Name), page, false).Let(g => Size(g, 46, 76, 0));
                rows.Add(row);
            }
            nmRows[key] = rows;
            SortRows(key);
        }



        // ---------- Presets: save / apply sets of pinned stats ----------
        private static string PresetFile => Path.Combine(Paths.ConfigPath, "leo.idleslayer.slayertools.presets.json");
        private Dictionary<string, Dictionary<string, double>> presets;
        private TMPro.TMP_InputField nmPresetName;
        private GameObject nmPresetList;
        private readonly List<GameObject> nmPresetRows = new List<GameObject>();

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

        private void NmBuildPresets()
        {
            var page = NmNewPage("presets");
            var top = NmGrid(page.transform, "grid").transform;
            NmButton(top, () => "< Back", "Up Arrow", () => NmShow("home"), page);
            NmButton(top, () => "Delete all presets", "drops_34", () =>
            {
                int c = Presets().Count; Presets().Clear(); SavePresets(); NmFillPresets();
                Toast(c + " preset(s) deleted");
            }, page);
            NmButton(page.transform, () => RecentToast() ?? $"A preset saves your {pinned.Count} currently pinned stat(s).", "news", null, page, false).Let(g => Size(g, 54));
            if (nmInputTemplate != null)
            {
                var row = NmBox(page.transform, "save", false, 10, 54);
                nmPresetName = NmInput(row.transform, "preset name (Enter to save)");
                nmPresetName.onSubmit.AddListener((Action<string>)(txt => { SavePreset(txt); nmPresetName.text = ""; }));
                NmButton(row.transform, () => "Save", "Star", () => { SavePreset(nmPresetName.text); nmPresetName.text = ""; }, page, false).Let(g => Size(g, 54, 150, 0));
            }
            nmPresetList = NmBox(page.transform, "list", true, 6);
        }

        private void NmFillPresets()
        {
            if (nmPresetList == null) return;
            foreach (var go in nmPresetRows) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            nmPresetRows.Clear();
            nmLabels.RemoveAll(l => l.txt == null);
            var page = nmPages["presets"];
            if (Presets().Count == 0)
            {
                nmPresetRows.Add(NmButton(nmPresetList.transform, () => "No presets yet.", "news", null, page, false).Let(g => Size(g, 46)));
                return;
            }
            foreach (var kv in Presets().OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                string name = kv.Key; int count = kv.Value.Count;
                var row = NmBox(nmPresetList.transform, "preset " + name, false, 8, 46);
                NmButton(row.transform, () => $"{name}  ({count} stats)", "news", null, page, false).Let(g => Size(g, 46, -1, 1));
                NmButton(row.transform, () => "Apply", "Star", () => ApplyPreset(name), page, false).Let(g => Size(g, 46, 160, 0));
                NmButton(row.transform, () => "Delete", "drops_34", () =>
                {
                    Presets().Remove(name); SavePresets(); Toast($"Preset \"{name}\" deleted"); NmFillPresets();
                }, page, false).Let(g => Size(g, 46, 160, 0));
                nmPresetRows.Add(row);
            }
        }

        // ---------- Quests: complete them in one click (the game's ForceComplete) ----------
        private GameObject nmQuestList;

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

        private void NmBuildQuests()
        {
            var page = NmNewPage("quests");
            var top = NmGrid(page.transform, "grid").transform;
            NmButton(top, () => "< Back", "Up Arrow", () => NmShow("home"), page);
            NmButton(top, () => "Complete all quests", "Star", () =>
            {
                int n = 0;
                foreach (var (q, _) in AllQuests().ToList())
                    if (QOpen(q) && !q.IsCompleted()) { q.ForceComplete(false); n++; }
                Toast(n + " quest(s) completed: go claim them in the game");
                NmFillQuests();
            }, page);
            NmButton(page.transform, () => RecentToast() ?? "Click a quest to complete it, then claim the reward in the game.", "news", null, page, false).Let(g => Size(g, 54));
            nmQuestList = NmBox(page.transform, "list", true, 6);
        }

        private void NmFillQuests()
        {
            if (nmQuestList == null) return;
            for (int i = nmQuestList.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(nmQuestList.transform.GetChild(i).gameObject);
            nmLabels.RemoveAll(l => l.txt == null);
            var page = nmPages["quests"];
            int count = 0;
            foreach (var (q, kind) in AllQuests().ToList())
            {
                if (!QOpen(q)) continue;
                var quest = q; var k = kind;
                var row = NmButton(nmQuestList.transform, () => QLabel(quest, k), "news", () =>
                {
                    if (quest.IsCompleted()) { Toast("Already completed: claim it in the game"); return; }
                    quest.ForceComplete(false);
                    Toast(QName(quest) + " completed!");
                }, page, false);
                Size(row, 44);
                count++;
            }
            if (count == 0)
                NmButton(nmQuestList.transform, () => "No active quests.", "news", null, page, false).Let(g => Size(g, 44));
        }

        // ---------- Enemies: kill counter (achievements like "Kill 500 Black Demons") ----------
        private Enemy nmEnemySel;
        private TMPro.TMP_InputField nmEnemyValue, nmEnemySearch;
        private readonly List<(GameObject go, Enemy e)> nmEnemyRows = new List<(GameObject, Enemy)>();
        private string nmEnemyLastFilter;

        private static string EName(Enemy e) => (!string.IsNullOrEmpty(e.localizedPluralName) ? e.localizedPluralName : !string.IsNullOrEmpty(e.localizedName) ? e.localizedName : e.name) ?? "?";

        private void NmBuildEnemies()
        {
            var page = NmNewPage("enemies");
            var top = NmGrid(page.transform, "grid").transform;
            NmButton(top, () => "< Back", "Up Arrow", () => NmShow("home"), page);
            NmButton(top, () => "Enemies killed", "book_11", null, page);
            NmButton(page.transform, () => RecentToast() ?? (nmEnemySel == null
                ? "Pick an enemy, type a kill count, then Apply."
                : EName(nmEnemySel) + " : " + nmEnemySel.killCount.ToString("N0") + " kills"), "news", null, page, false).Let(g => Size(g, 54));
            if (nmInputTemplate == null) return;
            var searchRow = NmBox(page.transform, "search", false, 10, 54);
            NmButton(searchRow.transform, () => "Search", "news", null, page, false).Let(g => Size(g, 54, 150, 0));
            nmEnemySearch = NmInput(searchRow.transform, "e.g. goblin, demon...");
            var editRow = NmBox(page.transform, "edit", false, 10, 54);
            nmEnemyValue = NmInput(editRow.transform, "kill count (e.g. 5000)");
            NmButton(editRow.transform, () => "Apply", "Star", () =>
            {
                if (nmEnemySel == null) { Toast("Pick an enemy first"); return; }
                if (!TryParseNumber(nmEnemyValue.text, out double d)) { Toast("Invalid number"); return; }
                var en0 = nmEnemySel; double old0 = en0.killCount;
                PushUndo(EName(en0) + " kills", () => en0.killCount = old0);
                nmEnemySel.killCount = Math.Max(0, d);
                nmEnemyValue.text = "";
                Toast(EName(nmEnemySel) + " : " + d.ToString("N0") + " kills (the achievement unlocks on the next kill)");
            }, page, false).Let(g => Size(g, 54, 150, 0));

            var list = NmBox(page.transform, "list", true, 6);
            var inv = PlayerInventory.instance;
            if (inv?.enemies == null) return;
            foreach (var e in inv.enemies)
            {
                if (e == null) continue;
                var en = e;
                var row = NmButton(list.transform, () => (nmEnemySel == en ? ">  " : "") + EName(en) + "  :  " + en.killCount.ToString("N0") + " kills", "news", () =>
                {
                    nmEnemySel = en; nmEnemyValue.text = ""; nmEnemyValue.ActivateInputField();
                }, page, false);
                Size(row, 44);
                nmEnemyRows.Add((row, en));
            }
        }

        private void NmFilterEnemies()
        {
            if (nmEnemySearch == null) return;
            string q = (nmEnemySearch.text ?? "").Trim();
            if (q == nmEnemyLastFilter) return;
            nmEnemyLastFilter = q;
            foreach (var (go, e) in nmEnemyRows)
                if (go != null) go.SetActive(q.Length == 0 || EName(e).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || (e.name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }


        // ---------- entry points: a tab in the menu bar + a button at the top left ----------
        private GameObject nmShopTab, nmLeftButton;
        private UnityEngine.UI.Image nmShopTabImage;
        private bool nmOpening;
        internal static ToolsBehaviour Self => Instance;

        private static GameObject FindGo(string path)
        {
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t == null || t.gameObject.hideFlags != HideFlags.None) continue;
                if (t.name != path.Substring(path.LastIndexOf('/') + 1)) continue;
                // check the parent path
                var parts = path.Split('/');
                var cur = t; bool ok = true;
                for (int i = parts.Length - 1; i >= 0 && ok; i--)
                {
                    if (cur == null || cur.name != parts[i]) ok = false;
                    cur = cur?.parent;
                }
                if (ok) return t.gameObject;
            }
            return null;
        }

        private void OpenFromOutside()
        {
            try
            {
                var ui = UIManager.instance;
                if (ui != null && !ui.ShopVisible()) ui.ToggleShop(true, true);
                nmOpening = true;
                ShopManager.instance?.OpenInfoPanel();
            }
            finally { nmOpening = false; }
            NmShow("home");
        }

        private void NmBuildEntryPoints()
        {
            // 1) tab in the bottom bar of the menu (between Info and Close)
            var bar = FindGo("Shop Panel/Wrapper/Buttons");
            var infoTab = bar?.transform.Find("Info");
            if (infoTab != null)
            {
                nmShopTab = UnityEngine.Object.Instantiate(infoTab.gameObject, bar.transform, false);
                nmShopTab.name = "Slayer Tools Tab";
                Strip(nmShopTab);
                var ov = nmShopTab.transform.Find("Notification Overlay"); if (ov != null) ov.gameObject.SetActive(false);
                var icon = nmShopTab.transform.Find("Image")?.GetComponent<UnityEngine.UI.Image>();
                var sp = NmSprite("Star"); if (icon != null && sp != null) icon.sprite = sp;
                nmShopTabImage = nmShopTab.GetComponent<UnityEngine.UI.Image>();
                nmShopTab.transform.SetSiblingIndex(infoTab.GetSiblingIndex() + 1);
                nmShopTab.GetComponent<UnityEngine.UI.Button>().onClick.AddListener((Action)(() =>
                {
                    try { nmOpening = true; ShopManager.instance?.OpenInfoPanel(); } finally { nmOpening = false; }
                    NmShow("home");
                }));
            }
            else Plugin.L.LogWarning("Menu tab bar not found.");

            // 2) round button at the top left, next to Ascension and the Bag
            var asc = FindGo("Left Buttons/Ascension Button");
            if (asc != null)
            {
                nmLeftButton = UnityEngine.Object.Instantiate(asc, asc.transform.parent, false);
                nmLeftButton.name = "Slayer Tools Button";
                Strip(nmLeftButton);
                var ov = nmLeftButton.transform.Find("Notification Overlay"); if (ov != null) ov.gameObject.SetActive(false);
                var icon = nmLeftButton.transform.Find("Image")?.GetComponent<UnityEngine.UI.Image>();
                var sp = NmSprite("Star"); if (icon != null && sp != null) icon.sprite = sp;
                nmLeftButton.GetComponent<UnityEngine.UI.Button>().onClick.AddListener((Action)(() =>
                {
                    try { if (nmPage != null && UIManager.instance != null && UIManager.instance.ShopVisible()) { NmClose(); UIManager.instance.ToggleShop(false, true); } else OpenFromOutside(); }
                    catch (Exception e) { Toast("Error: " + e.GetBaseException().Message); }
                }));
            }
            else Plugin.L.LogWarning("Ascension button not found.");
        }

        // called by the game patches: another tab or closing the menu closes our pages
        internal void OnGameMenuChanged()
        {
            if (nmOpening || nmPage == null) return;
            NmClose();
        }

        // tab color: copy the "selected" look of the Info tab onto ours
        private Color? nmTabOn, nmTabOff;
        private void NmTabColors()
        {
            var sm = ShopManager.instance;
            if (sm == null || nmShopTabImage == null || sm.infoPanelTabImage == null || sm.powersTabImage == null) return;
            if (nmPage != null)
            {
                nmTabOn ??= sm.infoPanelTabImage.color;
                nmTabOff ??= sm.powersTabImage.color;
                nmShopTabImage.color = nmTabOn.Value;
                sm.infoPanelTabImage.color = nmTabOff.Value;
            }
            else if (nmTabOff != null) nmShopTabImage.color = nmTabOff.Value;
        }

        // --- display helpers ---
        private string StatsLine()
        {
            var inv = PlayerInventory.instance;
            if (inv == null) return "Waiting for the game...";
            var t = RecentToast();
            if (t != null) return t;
            var line = $"Coins {Fmt(inv.coins)}   ·   CpS {Fmt(inv.coinsPerSecond)}\nSlayer Points {Fmt(SlayerPoints.points)}   ·   Souls {Fmt(SlayerPoints.soulsLeft)}";
            return VersionWarning != null ? "<color=#FF8A5A>" + VersionWarning + "</color>\n" + line : line;
        }

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

        private string RowText(string key, EditableProp p, object target)
        {
            string mark = key == "inv" && pinned.ContainsKey(p.Name) ? "* " : "";
            string sel = nmSelected.TryGetValue(key, out var s) && s == p ? ">  " : "";
            return sel + mark + p.Name + "  :  " + ValueText(p, target) + (p.ReadOnly ? "   (locked)" : "");
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

        private static string OnOff(bool b) => b ? "YES" : "no";

        // --- navigation ---
        private void NmShow(string key)
        {
            if (nmContent == null) return;
            if (nmPage == null)
            {
                nmHidden.Clear();
                for (int i = 0; i < nmContent.childCount; i++)
                {
                    var c = nmContent.GetChild(i).gameObject;
                    if (!c.activeSelf || nmPages.ContainsValue(c)) continue;
                    c.SetActive(false);
                    nmHidden.Add(c);
                }
            }
            foreach (var kv in nmPages) kv.Value.SetActive(kv.Key == key);
            nmPage = key;
            NmTabColors();
            if (key == "quests") NmFillQuests();
            if (key == "presets") NmFillPresets();
            if (key == "bag") NmFillBag();
            if (key == "divin") NmFillDivinities();
            NmRefresh(true);
        }

        private void NmClose()
        {
            if (nmTabOff != null && nmShopTabImage != null) nmShopTabImage.color = nmTabOff.Value;
            foreach (var kv in nmPages) if (kv.Value != null) kv.Value.SetActive(false);
            foreach (var c in nmHidden) if (c != null) c.SetActive(true);
            nmHidden.Clear();
            nmPage = null;
        }

        private void NmFilter(string key)
        {
            if (!nmRows.TryGetValue(key, out var rows) || !nmSearch.TryGetValue(key, out var input) || input == null) return;
            string q = (input.text ?? "").Trim();
            if (nmLastFilter.TryGetValue(key, out var last) && last == q) return;
            nmLastFilter[key] = q;
            foreach (var r in rows)
                r.go.SetActive(q.Length == 0 || Matches(r.prop.Name, q));
        }

        private void NmRefresh(bool force = false)
        {
            if (nmPage == null || !nmPages.TryGetValue(nmPage, out var current)) return;
            foreach (var (input, prop, key, target) in nmRowInputs)
                if (input != null && key == nmPage && !input.isFocused && input.gameObject.activeInHierarchy)
                    try { var t = EditText(prop, target()); if (input.text != t) input.text = t; } catch { }
            foreach (var (txt, label, page) in nmLabels)
                if (txt != null && (page == null || page == current) && txt.gameObject.activeInHierarchy)
                    try { txt.text = label(); } catch { }
        }

        private void NmUpdate()
        {
            if (nmBroken) return;
            try
            {
                if (freezeBoost) { var b = PlayerInventory.instance?.boost; if (b != null && b.currentCd > 0.0001f) b.currentCd = 0.0001f; }

                if (Time.realtimeSinceStartup >= nmNextCheck)
                {
                    nmNextCheck = Time.realtimeSinceStartup + 2f;
                    if (nmShopTab == null && nmLeftButton == null && InfoPanel.instance != null && !nmBuildFailed)
                    {
                        nmLabels.Clear(); nmHidden.Clear(); nmPages.Clear(); nmRows.Clear(); nmSearch.Clear(); nmValue.Clear(); nmLastFilter.Clear();
                        nmPage = null; nmRowInputs.Clear(); nmPresetRows.Clear(); nmEnemyRows.Clear(); nmEnemyLastFilter = null; nmEnemySel = null; nmQuestList = null; ResetBagUi(); ResetDivUi();
                        NmBuild();
                        if (nmShopTab == null && nmLeftButton == null) nmBuildFailed = true;
                    }
                }
                bool typing = false;
                foreach (var f in nmSearch.Values) if (f != null && f.isFocused) typing = true;
                foreach (var f in nmValue.Values) if (f != null && f.isFocused) typing = true;
                foreach (var r in nmRowInputs) if (r.input != null && r.input.isFocused) { typing = true; break; }
                if ((nmPresetName != null && nmPresetName.isFocused) || (nmEnemySearch != null && nmEnemySearch.isFocused) || (nmEnemyValue != null && nmEnemyValue.isFocused) || BagTyping() || DivTyping()) typing = true;
                Plugin.Typing = typing;

                if (nmPage != null)
                {
                    NmFilter(nmPage);
                    if (nmPage == "enemies") NmFilterEnemies();
                    if (nmPage == "bag") NmFilterBag();
                    if (Time.realtimeSinceStartup >= nmNextRefresh)
                    {
                        nmNextRefresh = Time.realtimeSinceStartup + 0.5f;
                        NmRefresh();
                        if (!nmPages[nmPage].activeInHierarchy) NmClose();
                        NmTabColors();   // the player closed the game menu
                    }
                }
            }
            catch (Exception e)
            {
                nmBroken = true;
                Plugin.L.LogError("Native menu disabled: " + e);
                try { NmClose(); } catch { }
            }
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
            var sb = new StringBuilder("{\"ok\":true,\"version\":\"5.2.0\"");
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
            { appliedOnLoad = true; try { PlayerInventory.instance.CalculateValues(); } catch (Exception e) { Plugin.L.LogWarning("Initial recalculation: " + e.Message); } }
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
                if (Input.GetKeyDown(Plugin.KeyMenu.Value)) { overlayVisible = !overlayVisible; BuildSnapshot(true); }
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
