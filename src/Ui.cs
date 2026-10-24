using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx;
using UnityEngine;

namespace SlayerTools
{
    // ======================================================================
    // v6 UI framework.
    //
    // Every page has the same anatomy:
    //   [ < Back ][        Section > Page title        ][ Home ]   header
    //   [ status: last message or page hint        ][ Undo ]   status bar
    //   ... page content ...
    //
    // The menu remembers where you were (last page, scroll position, searches,
    // filters, list pages) in leo.idleslayer.slayertools.ui.json.
    // ======================================================================

    /// <summary>What the menu remembers between sessions.</summary>
    internal class UiState
    {
        public string LastPage { get; set; } = "home";
        public List<string> Recent { get; set; } = new List<string>();
        public Dictionary<string, float> Scroll { get; set; } = new Dictionary<string, float>();
        public Dictionary<string, string> Search { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, int> Filter { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> ListPage { get; set; } = new Dictionary<string, int>();
        public int RowsPerPage { get; set; } = 8;
        public bool OpenAtHome { get; set; }
        public bool ConfirmDanger { get; set; } = true;
        public int BagStep { get; set; } = 2;
        public string SeenVersion { get; set; } = "";
    }

    public partial class ToolsBehaviour
    {
        internal const string ModVersion = "6.0.0";

        // ---------------- templates and containers ----------------
        private GameObject nmTemplate, nmInputTemplate, nmGridTemplate;
        private TMPro.TMP_FontAsset nmFont;
        private Material nmFontMat;
        private bool nmBuildFailed, nmBroken;
        private int nmFailures, nmBuildTries;
        private Transform nmContent;
        private float nmWidth = 528f;
        private float nmNextCheck, nmNextRefresh;
        private Dictionary<string, Sprite> nmSprites;

        // ---------------- pages ----------------
        private class PageInfo
        {
            public string Key, Title, Hint;
            public GameObject Root;
            public Action OnShow;     // called every time the page opens
            public Action Tick;       // called every frame while the page is visible
        }
        private readonly Dictionary<string, PageInfo> nmPages = new Dictionary<string, PageInfo>();
        private readonly List<GameObject> nmHidden = new List<GameObject>();
        private readonly List<(TMPro.TextMeshProUGUI txt, Func<string> label, GameObject page)> nmLabels = new List<(TMPro.TextMeshProUGUI, Func<string>, GameObject)>();
        private readonly List<(GameObject go, Func<bool> enabled, GameObject page)> nmEnabled = new List<(GameObject, Func<bool>, GameObject)>();
        private readonly List<(GameObject go, Func<bool> visible, GameObject page)> nmVisible = new List<(GameObject, Func<bool>, GameObject)>();
        private readonly List<TMPro.TMP_InputField> nmInputs = new List<TMPro.TMP_InputField>();
        private readonly List<string> nmHistory = new List<string>();
        private string nmPage;

        // ---------------- remembered state ----------------
        private UiState ui = new UiState();
        private bool uiDirty;
        private float uiNextSave;
        private static string UiFile => Path.Combine(Paths.ConfigPath, "leo.idleslayer.slayertools.ui.json");

        private void LoadUi()
        {
            try { if (File.Exists(UiFile)) ui = JsonSerializer.Deserialize<UiState>(File.ReadAllText(UiFile)) ?? new UiState(); }
            catch (Exception e) { Plugin.L.LogWarning("Reading menu state: " + e.Message); ui = new UiState(); }
            ui.RowsPerPage = Mathf.Clamp(ui.RowsPerPage, 4, MaxRows);
        }

        private void SaveUi(bool now = false)
        {
            uiDirty = true;
            if (!now && Time.realtimeSinceStartup < uiNextSave) return;
            uiNextSave = Time.realtimeSinceStartup + 2f;
            uiDirty = false;
            try { File.WriteAllText(UiFile, JsonSerializer.Serialize(ui)); }
            catch (Exception e) { Plugin.L.LogWarning("Saving menu state: " + e.Message); }
        }

        // ---------------- colours (rich text) ----------------
        private const string CGold = "#FFD24A", CGreen = "#7CFF8A", CRed = "#FF6B6B", COrange = "#FFA94D", CGrey = "#A8A29A", CBlue = "#8CC8FF", CPurple = "#C9A0FF";
        private static string Col(string c, string s) => $"<color={c}>{s}</color>";

        // "#RRGGBB" -> Color. (UnityEngine.ColorUtility is not usable through Il2CppInterop: MissingMethodException.)
        private static Color Hex(string hex)
        {
            try
            {
                hex = hex.TrimStart('#');
                float r = Convert.ToInt32(hex.Substring(0, 2), 16) / 255f, g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255f, b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255f;
                return new Color(r, g, b, 1f);
            }
            catch { return Color.white; }
        }
        private static string OnOff(bool b) => b ? Col(CGreen, "ON") : Col(CGrey, "OFF");

        // ======================================================================
        // Building blocks
        // ======================================================================
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

        private GameObject NmBox(Transform parent, string name, bool vertical, float spacing = 8, float height = -1)
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
            if (height > 0) Size(go, height);
            return go;
        }

        private static void Size(GameObject go, float height, float width = -1, float flex = -1)
        {
            var le = go.GetComponent<UnityEngine.UI.LayoutElement>() ?? go.AddComponent<UnityEngine.UI.LayoutElement>();
            if (height > 0) { le.minHeight = height; le.preferredHeight = height; }
            if (width > 0) { le.minWidth = width; le.preferredWidth = width; }
            if (flex >= 0) le.flexibleWidth = flex;
        }

        private void Bind(TMPro.TextMeshProUGUI txt, Func<string> label, GameObject page)
        {
            if (txt == null) return;
            try { txt.text = label(); } catch { txt.text = ""; }
            nmLabels.Add((txt, label, page));
        }

        private void Wire(GameObject go, Action onClick)
        {
            var btn = go.GetComponent<UnityEngine.UI.Button>();
            if (btn == null || onClick == null) return;
            btn.onClick.AddListener((Action)(() =>
            {
                try { onClick(); }
                catch (Exception e) { Toast("Error: " + e.GetBaseException().Message); }
                NmRefresh();
            }));
        }

        /// <summary>Greys out and disables a control while <paramref name="enabled"/> is false.</summary>
        private void EnableWhen(GameObject go, Func<bool> enabled, GameObject page)
        {
            if (go.GetComponent<CanvasGroup>() == null) go.AddComponent<CanvasGroup>();
            nmEnabled.Add((go, enabled, page));
        }

        /// <summary>Shows a control only while <paramref name="visible"/> is true.</summary>
        private void ShowWhen(GameObject go, Func<bool> visible, GameObject page) => nmVisible.Add((go, visible, page));

        /// <summary>Plain text button (whole width of its cell), cloned from the game's News button.</summary>
        private GameObject NmButton(Transform parent, Func<string> label, Action onClick, GameObject page, float height = 44, float width = -1, float flex = -1)
        {
            var go = UnityEngine.Object.Instantiate(nmTemplate, parent, false);
            go.name = "ST Button";
            go.SetActive(true);
            Strip(go);
            var txt = go.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (txt != null)
            {
                txt.enableAutoSizing = true; txt.fontSizeMin = 9; txt.fontSizeMax = 17;
                txt.enableWordWrapping = true; txt.alignment = TMPro.TextAlignmentOptions.Center;
                Bind(txt, label, page);
            }
            var iconT = go.transform.Find("Background/Content/Image");
            if (iconT != null) iconT.gameObject.SetActive(false);
            var bgT = go.transform.Find("Background")?.GetComponent<RectTransform>();
            if (bgT != null) { bgT.anchorMin = Vector2.zero; bgT.anchorMax = Vector2.one; bgT.offsetMin = new Vector2(-3, -1.5f); bgT.offsetMax = new Vector2(3, 3.5f); }
            var ctT = go.transform.Find("Background/Content")?.GetComponent<RectTransform>();
            if (ctT != null) { ctT.anchorMin = Vector2.zero; ctT.anchorMax = Vector2.one; ctT.offsetMin = new Vector2(8, 5); ctT.offsetMax = new Vector2(-8, -4); }
            if (txt != null) { var r = txt.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero; }
            Wire(go, onClick);
            Size(go, height, width, flex);
            return go;
        }

        /// <summary>Icon + text tile (icon on the left), used for navigation and actions.</summary>
        private GameObject NmTile(Transform parent, Func<string> label, Func<Sprite> icon, Action onClick, GameObject page)
        {
            var go = NmButton(parent, label, onClick, page);
            var txt = go.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            var iconT = go.transform.Find("Background/Content/Image");
            if (iconT != null)
            {
                iconT.gameObject.SetActive(true);
                var r = iconT.GetComponent<RectTransform>();
                r.anchorMin = new Vector2(0, 0.5f); r.anchorMax = new Vector2(0, 0.5f); r.pivot = new Vector2(0, 0.5f);
                r.sizeDelta = new Vector2(30, 30); r.anchoredPosition = new Vector2(2, 0);
                var img = iconT.GetComponent<UnityEngine.UI.Image>();
                if (img != null)
                {
                    img.preserveAspect = true;
                    try { var s = icon?.Invoke(); if (s != null) img.sprite = s; } catch { }
                }
            }
            if (txt != null)
            {
                txt.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
                txt.rectTransform.offsetMin = new Vector2(38, 0);
                txt.fontSizeMax = 16;
            }
            return go;
        }

        private static void SetTileIcon(GameObject tile, Sprite s, float alpha = 1f)
        {
            var img = tile?.transform.Find("Background/Content/Image")?.GetComponent<UnityEngine.UI.Image>();
            if (img == null) return;
            img.sprite = s; img.enabled = s != null; img.color = new Color(1, 1, 1, alpha);
        }

        /// <summary>Plain text (no button background): section titles, descriptions, big read-outs.</summary>
        private GameObject NmText(Transform parent, Func<string> label, GameObject page, float height, float size = 15, bool left = true, string color = null)
        {
            var go = new GameObject("ST Text");
            go.AddComponent<RectTransform>();
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            var t = go.AddComponent<TMPro.TextMeshProUGUI>();
            if (nmFont != null) { t.font = nmFont; if (nmFontMat != null) t.fontSharedMaterial = nmFontMat; }
            // fixed size: auto-sizing a freshly created TMP text inside layout groups ends up far too big
            t.enableAutoSizing = false; t.fontSize = size;
            t.enableWordWrapping = true; t.raycastTarget = false;
            t.alignment = left ? TMPro.TextAlignmentOptions.MidlineLeft : TMPro.TextAlignmentOptions.Center;
            t.color = color == null ? new Color(0.96f, 0.91f, 0.82f) : Hex(color);
            Bind(t, label, page);
            if (height > 0) Size(go, height);   // height <= 0: as tall as the text needs (TMP preferred height)
            return go;
        }

        // section title with some air above it, so sections are clearly separated
        private GameObject NmSection(Transform parent, string title, GameObject page)
        {
            var go = NmText(parent, () => Col(CGold, title.ToUpperInvariant()), page, 40, 15);
            var t = go.GetComponent<TMPro.TextMeshProUGUI>();
            if (t != null) t.alignment = TMPro.TextAlignmentOptions.BottomLeft;
            return go;
        }

        /// <summary>Grid with a fixed number of columns that fills the panel width.</summary>
        private GameObject NmCols(Transform parent, int cols, float cellHeight = 52, float spacing = 10)
        {
            var go = new GameObject("ST Grid");
            go.AddComponent<RectTransform>();
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            var g = go.AddComponent<UnityEngine.UI.GridLayoutGroup>();
            float w = Mathf.Floor((nmWidth - (cols - 1) * spacing) / cols);
            g.cellSize = new Vector2(w, cellHeight);
            g.spacing = new Vector2(spacing, spacing);
            g.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = cols;
            g.childAlignment = TextAnchor.UpperLeft;
            return go;
        }

        private TMPro.TMP_InputField NmInput(Transform parent, string placeholder, float height = 44, float width = -1, float flex = 1)
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
                input.textComponent.enableAutoSizing = true; input.textComponent.fontSizeMin = 9; input.textComponent.fontSizeMax = 17;
                input.textComponent.alignment = TMPro.TextAlignmentOptions.Center;
                if (input.placeholder == null)
                {
                    var ph = UnityEngine.Object.Instantiate(input.textComponent.gameObject, input.textComponent.transform.parent, false);
                    ph.name = "Placeholder";
                    input.placeholder = ph.GetComponent<TMPro.TextMeshProUGUI>();
                }
                var pt = input.placeholder.TryCast<TMPro.TextMeshProUGUI>();
                if (pt != null) { pt.text = placeholder; pt.color = new Color(1, 1, 1, 0.45f); pt.enableAutoSizing = true; pt.fontSizeMin = 8; pt.fontSizeMax = 15; }
            }
            var area = go.transform.Find("Text Area")?.GetComponent<RectTransform>();
            if (area != null) { area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one; area.offsetMin = new Vector2(8, 3); area.offsetMax = new Vector2(-8, -3); }
            foreach (var t in go.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
            { var r = t.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero; }
            Size(go, height, width, flex);
            nmInputs.Add(input);
            return input;
        }

        /// <summary>Title + description on the left, ON/OFF switch on the right. The whole row is clickable.</summary>
        private void NmToggle(Transform parent, string title, string desc, Func<bool> get, Action<bool> set, GameObject page)
        {
            var row = NmBox(parent, "toggle " + title, false, 6, desc == null ? 44 : 58);
            Action flip = () => { set(!get()); SaveOptions(); Toast(title + ": " + (get() ? "on" : "off")); };
            var label = NmButton(row.transform, () => desc == null ? title : title + "\n" + Col(CGrey, "<size=75%>" + desc + "</size>"), flip, page, desc == null ? 44 : 58, -1, 1);
            var t = label.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (t != null) t.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
            NmButton(row.transform, () => OnOff(get()), flip, page, desc == null ? 44 : 58, 90, 0);
        }

        // ======================================================================
        // Pages, header and status bar
        // ======================================================================
        private static readonly Dictionary<string, (string title, string section, string icon)> PageMeta = new Dictionary<string, (string, string, string)>
        {
            ["home"] = ("Slayer Tools", "", "Star"),
            ["search"] = ("Search", "", "news"),
            ["actions"] = ("Actions", "Play", "Star"),
            ["events"] = ("Events", "Play", "Star Silver"),
            ["combat"] = ("Combat & chests", "Play", "Face"),
            ["inv"] = ("Inventory", "Edit", "Checklist"),
            ["sp"] = ("Slayer Points", "Edit", "Face"),
            ["boost"] = ("Boost", "Edit", "Up Arrow"),
            ["bag"] = ("Bag", "Edit", "drops_34"),
            ["divin"] = ("Divinities", "Edit", "Star"),
            ["quests"] = ("Quests", "Edit", "Checklist"),
            ["enemies"] = ("Enemies", "Edit", "book_11"),
            ["presets"] = ("Presets", "Tools", "Star Silver"),
            ["dash"] = ("Dashboard", "Tools", "news"),
            ["log"] = ("Log", "Tools", "book_11"),
            ["settings"] = ("Settings", "Tools", "drops_34"),
            ["whatsnew"] = ("What's new", "Tools", "news"),
        };

        private static string Title(string key) => PageMeta.TryGetValue(key, out var m) ? m.title : key;

        private Sprite PageIcon(string key)
        {
            try
            {
                if (key == "bag") { var d = PlayerInventory.instance?.drops?.FirstOrDefault(x => x != null && x.icon != null && !x.locked); if (d != null) return d.icon; }
                if (key == "divin") { var s = DivinitiesManager.instance?.divinityPointsIcon; if (s != null) return s; }
            }
            catch { }
            return NmSprite(PageMeta.TryGetValue(key, out var m) ? m.icon : "Star");
        }

        /// <summary>Creates a page with the shared header and status bar, and returns its content column.</summary>
        private GameObject NmPage(string key, string hint, Action onShow = null, Action tick = null)
        {
            var root = NmBox(nmContent, "Slayer Tools " + key, true, 10);
            root.SetActive(false);
            var info = new PageInfo { Key = key, Title = Title(key), Hint = hint, Root = root, OnShow = onShow, Tick = tick };
            nmPages[key] = info;

            // header
            var head = NmBox(root.transform, "header", false, 6, 44);
            NmButton(head.transform, () => key == "home" ? "Close" : "< Back", () => Back(), root, 44, 96, 0);
            var meta = PageMeta.TryGetValue(key, out var m) ? m : (key, "", "Star");
            NmText(head.transform, () => (meta.Item2.Length > 0 ? Col(CGrey, meta.Item2 + "  >  ") : "") + Col(CGold, meta.Item1), root, 44, 17, false)
                .Let(g => Size(g, 44, -1, 1));
            if (key != "home") NmButton(head.transform, () => "Home", () => Go("home"), root, 44, 96, 0);
            else NmButton(head.transform, () => "Settings", () => Go("settings"), root, 44, 96, 0);

            // status bar: last message (coloured by kind) or the page hint, plus Undo
            var status = NmBox(root.transform, "status", false, 6, 40);
            NmText(status.transform, () => StatusText(info), root, 40, 12).Let(g => Size(g, 40, -1, 1));
            var undoBtn = NmButton(status.transform, () => undo.Count == 0 ? Col(CGrey, "Undo") : "Undo (" + undo.Count + ")", DoUndo, root, 40, 96, 0);
            EnableWhen(undoBtn, () => undo.Count > 0, root);
            NmBox(root.transform, "spacer", false, 0, 4);   // a bit of air between the bars and the content
            return root;
        }

        private string StatusText(PageInfo p)
        {
            var t = RecentToast();
            if (t != null) return Col(ToastColor(t), t);
            if (p.Key == "home" && VersionWarning != null) return Col(COrange, VersionWarning);
            return Col(CGrey, p.Hint ?? "");
        }

        private static string ToastColor(string msg)
        {
            string m = msg.ToLowerInvariant();
            if (m.StartsWith("error") || m.Contains("failed") || m.Contains("invalid") || m.Contains("not found") || m.Contains("unknown")) return CRed;
            if (m.Contains("click again") || m.Contains("not in game") || m.Contains("only available") || m.Contains("locked")
                || m.Contains("not unlocked") || m.Contains("bag full") || m.Contains("nothing to") || m.Contains("not loaded")) return COrange;
            return CGreen;
        }

        // ======================================================================
        // Navigation
        // ======================================================================
        private UnityEngine.UI.ScrollRect nmScroll;
        private RectTransform nmScrollContent;
        private string nmPendingScrollPage;
        private int nmPendingScrollFrames;

        /// <summary>Opens a page and remembers it in the back history.</summary>
        private void Go(string key)
        {
            if (!nmPages.ContainsKey(key)) return;
            if (nmPage != null && nmPage != key)
            {
                nmHistory.Add(nmPage);
                if (nmHistory.Count > 30) nmHistory.RemoveAt(0);
            }
            NmShow(key);
        }

        private void Back()
        {
            while (nmHistory.Count > 0)
            {
                var prev = nmHistory[nmHistory.Count - 1];
                nmHistory.RemoveAt(nmHistory.Count - 1);
                if (prev != nmPage && nmPages.ContainsKey(prev)) { NmShow(prev); return; }
            }
            if (nmPage != "home") { NmShow("home"); return; }
            CloseGameMenu();
        }

        private void NmShow(string key)
        {
            if (nmContent == null || !nmPages.TryGetValue(key, out var info)) return;
            SaveScroll();
            if (nmPage == null)
            {
                nmHidden.Clear();
                for (int i = 0; i < nmContent.childCount; i++)
                {
                    var c = nmContent.GetChild(i).gameObject;
                    if (!c.activeSelf || nmPages.Values.Any(p => p.Root == c)) continue;
                    c.SetActive(false);
                    nmHidden.Add(c);
                }
            }
            foreach (var kv in nmPages) kv.Value.Root.SetActive(kv.Key == key);
            nmPage = key;
            ui.LastPage = key;
            if (key != "home" && key != "whatsnew")
            {
                ui.Recent.Remove(key);
                ui.Recent.Insert(0, key);
                while (ui.Recent.Count > 6) ui.Recent.RemoveAt(ui.Recent.Count - 1);
            }
            SaveUi();
            NmTabColors();
            try { info.OnShow?.Invoke(); } catch (Exception e) { Plugin.L.LogWarning($"Opening {key}: {e.Message}"); }
            NmRefresh();
            // the layout is rebuilt during the next frames: restore the scroll position after that
            nmPendingScrollPage = key; nmPendingScrollFrames = 2;
        }

        private void SaveScroll()
        {
            if (nmPage == null || nmScrollContent == null) return;
            ui.Scroll[nmPage] = nmScrollContent.anchoredPosition.y;
        }

        private void ApplyPendingScroll()
        {
            if (nmPendingScrollPage == null || nmScrollContent == null) return;
            if (--nmPendingScrollFrames > 0) return;
            float y = ui.Scroll.TryGetValue(nmPendingScrollPage, out var v) ? v : 0f;
            try
            {
                Canvas.ForceUpdateCanvases();
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(nmScrollContent);
                var view = nmScroll.viewport != null ? nmScroll.viewport.rect.height : 471f;
                float max = Mathf.Max(0, nmScrollContent.rect.height - view);
                nmScrollContent.anchoredPosition = new Vector2(nmScrollContent.anchoredPosition.x, Mathf.Clamp(y, 0, max));
                nmScroll.velocity = Vector2.zero;
            }
            catch { }
            nmPendingScrollPage = null;
        }

        private void NmClose()
        {
            SaveScroll();
            SaveUi(true);
            // the game's own Info page shares this scroll view: give it back scrolled to the top
            try { if (nmScrollContent != null) nmScrollContent.anchoredPosition = new Vector2(nmScrollContent.anchoredPosition.x, 0); } catch { }
            if (nmTabOff != null && nmShopTabImage != null) nmShopTabImage.color = nmTabOff.Value;
            foreach (var kv in nmPages) if (kv.Value.Root != null) kv.Value.Root.SetActive(false);
            foreach (var c in nmHidden) if (c != null) c.SetActive(true);
            nmHidden.Clear();
            nmHistory.Clear();
            nmPage = null;
            Plugin.Typing = false;
        }

        private void CloseGameMenu()
        {
            try { NmClose(); var g = UIManager.instance; if (g != null && g.ShopVisible()) g.ToggleShop(false, true); } catch { }
        }

        private string StartPage()
        {
            if (ui.SeenVersion != ModVersion && nmPages.ContainsKey("whatsnew")) { ui.SeenVersion = ModVersion; SaveUi(true); return "whatsnew"; }
            if (ui.OpenAtHome || string.IsNullOrEmpty(ui.LastPage) || !nmPages.ContainsKey(ui.LastPage)) return "home";
            return ui.LastPage;
        }

        /// <summary>Opens Slayer Tools where the player left it, opening the game menu first if needed.</summary>
        private void OpenMenu()
        {
            try
            {
                var g = UIManager.instance;
                if (g != null && !g.ShopVisible()) g.ToggleShop(true, true);
                nmOpening = true;
                ShopManager.instance?.OpenInfoPanel();
            }
            finally { nmOpening = false; }
            var start = StartPage();
            if (start != "home") nmHistory.Add("home");
            NmShow(start);
        }

        private void ToggleMenu()
        {
            var g = UIManager.instance;
            if (nmPage != null && g != null && g.ShopVisible()) CloseGameMenu();
            else OpenMenu();
        }

        // ---------------- confirmation for dangerous actions ----------------
        private string confirmKey; private float confirmUntil;

        private bool Confirm(string key)
        {
            if (!ui.ConfirmDanger) return true;
            if (confirmKey == key && Time.realtimeSinceStartup < confirmUntil) { confirmKey = null; return true; }
            confirmKey = key; confirmUntil = Time.realtimeSinceStartup + 4f;
            Toast("Click again to confirm");
            return false;
        }

        private string ConfirmText(string key, string label) =>
            confirmKey == key && Time.realtimeSinceStartup < confirmUntil ? Col(COrange, "Confirm: " + label + "?") : label;

        // ======================================================================
        // Paged lists (one pool of rows, re-bound to the items of the current page)
        // ======================================================================
        private const int MaxRows = 15;

        private class PagedList
        {
            public string Key;
            public Func<IEnumerable<object>> Source;
            public Func<object, string, bool> Match;
            public string[] Filters = { "All" };
            public Func<object, int, bool> Filter = (o, f) => true;
            public Func<IEnumerable<object>, IEnumerable<object>> Sort = x => x;
            public List<object> Items = new List<object>();
            public List<GameObject> Rows = new List<GameObject>();
            public List<Action> OnBind = new List<Action>();
            public TMPro.TMP_InputField Search;
            public string LastSearch;
            public int Page, PerPage = 8;
            public object At(int row) { int i = Page * PerPage + row; return row < PerPage && i >= 0 && i < Items.Count ? Items[i] : null; }
            public int Pages => Math.Max(1, (Items.Count + PerPage - 1) / PerPage);
        }

        private readonly Dictionary<string, PagedList> nmLists = new Dictionary<string, PagedList>();

        private int ListFilter(PagedList l) => ui.Filter.TryGetValue(l.Key, out var f) && f >= 0 && f < l.Filters.Length ? f : 0;

        /// <summary>Search box + filter button + pager, then <paramref name="makeRow"/> is called once per pooled row.</summary>
        private PagedList NmList(GameObject page, PagedList l, string searchHint, Action<Transform, Func<object>> makeRow)
        {
            nmLists[l.Key] = l;
            l.PerPage = ui.RowsPerPage;
            var bar = NmBox(page.transform, "toolbar", false, 6, 44);
            if (nmInputTemplate != null)
            {
                l.Search = NmInput(bar.transform, searchHint);
                if (ui.Search.TryGetValue(l.Key, out var s)) l.Search.text = s;
            }
            if (l.Filters.Length > 1)
                NmButton(bar.transform, () => "Show: " + Col(CGold, l.Filters[ListFilter(l)]), () =>
                {
                    ui.Filter[l.Key] = (ListFilter(l) + 1) % l.Filters.Length;
                    l.Page = 0; SaveUi(); Rebind(l);
                }, page, 44, 150, 0);

            var rows = NmBox(page.transform, "rows", true, 6);
            for (int i = 0; i < MaxRows; i++)
            {
                int idx = i;
                var row = NmBox(rows.transform, "row " + i, false, 6, 46);
                makeRow(row.transform, () => l.At(idx));
                l.Rows.Add(row);
            }
            NmText(page.transform, () => "Nothing matches.", page, 30, 14, false, CGrey)
                .Let(g => ShowWhen(g, () => l.Items.Count == 0, page));

            var pager = NmBox(page.transform, "pager", false, 6, 44);
            NmButton(pager.transform, () => "< Prev", () => { l.Page = Math.Max(0, l.Page - 1); ui.ListPage[l.Key] = l.Page; SaveUi(); Rebind(l); }, page, 44, 110, 0)
                .Let(g => EnableWhen(g, () => l.Page > 0, page));
            NmText(pager.transform, () => $"Page {l.Page + 1} / {l.Pages}   " + Col(CGrey, $"({l.Items.Count} items)"), page, 44, 15, false).Let(g => Size(g, 44, -1, 1));
            NmButton(pager.transform, () => "Next >", () => { l.Page = Math.Min(l.Pages - 1, l.Page + 1); ui.ListPage[l.Key] = l.Page; SaveUi(); Rebind(l); }, page, 44, 110, 0)
                .Let(g => EnableWhen(g, () => l.Page < l.Pages - 1, page));

            l.Page = ui.ListPage.TryGetValue(l.Key, out var p) ? p : 0;
            return l;
        }

        private void Rebind(PagedList l, bool keepPage = true)
        {
            string q = (l.Search?.text ?? "").Trim();
            l.LastSearch = q;
            int f = ListFilter(l);
            try
            {
                var src = l.Source?.Invoke() ?? Enumerable.Empty<object>();
                l.Items = l.Sort(src.Where(o => o != null && l.Filter(o, f) && (q.Length == 0 || l.Match(o, q)))).ToList();
            }
            catch (Exception e) { Plugin.L.LogWarning($"List {l.Key}: {e.Message}"); l.Items = new List<object>(); }
            l.PerPage = ui.RowsPerPage;
            if (!keepPage) l.Page = 0;
            l.Page = Mathf.Clamp(l.Page, 0, l.Pages - 1);
            for (int i = 0; i < l.Rows.Count; i++) l.Rows[i].SetActive(l.At(i) != null);
            foreach (var a in l.OnBind) try { a(); } catch { }
            NmRefresh();
        }

        private void TickList(PagedList l)
        {
            if (l.Search == null) return;
            string q = (l.Search.text ?? "").Trim();
            if (q == l.LastSearch) return;
            ui.Search[l.Key] = q; SaveUi();
            l.Page = 0;
            Rebind(l);
        }

        // ======================================================================
        // Refresh loop
        // ======================================================================
        private void NmRefresh()
        {
            if (nmPage == null || !nmPages.TryGetValue(nmPage, out var cur)) return;
            var page = cur.Root;
            foreach (var (go, visible, p) in nmVisible)
                if (go != null && p == page) try { bool v = visible(); if (go.activeSelf != v) go.SetActive(v); } catch { }
            foreach (var (go, enabled, p) in nmEnabled)
            {
                if (go == null || p != page) continue;
                bool on = true; try { on = enabled(); } catch { }
                var cg = go.GetComponent<CanvasGroup>();
                if (cg != null) { cg.alpha = on ? 1f : 0.4f; cg.interactable = on; cg.blocksRaycasts = on; }
            }
            foreach (var (txt, label, p) in nmLabels)
                if (txt != null && (p == null || p == page) && txt.gameObject.activeInHierarchy)
                    try { var s = label(); if (txt.text != s) txt.text = s; } catch { }
        }

        private void NmUpdate()
        {
            if (nmBroken) return;
            try
            {
                if (freezeBoost) { var b = PlayerInventory.instance?.boost; if (b != null && b.currentCd > 0.0001f) b.currentCd = 0.0001f; }

                // The game reloads its scene (e.g. after downloading the cloud save): every object of the
                // menu is destroyed. Forget them so the menu is rebuilt on the new scene.
                if (nmPages.Count > 0 && (nmContent == null || (nmPage != null && (!nmPages.TryGetValue(nmPage, out var cp) || cp.Root == null))))
                {
                    Plugin.L.LogInfo("Game scene reloaded: rebuilding the menu.");
                    NmResetAll(); nmContent = null; nmShopTab = null; nmLeftButton = null; nmShopTabImage = null; Plugin.Typing = false;
                    nmNextCheck = 0;
                }

                if (Time.realtimeSinceStartup >= nmNextCheck)
                {
                    nmNextCheck = Time.realtimeSinceStartup + 2f;
                    if (nmShopTab == null && nmLeftButton == null && InfoPanel.instance != null && !nmBuildFailed)
                    {
                        NmResetAll();
                        NmBuild();
                        // the scene may still be loading: just try again in 2 s (log once in a while)
                        if (nmShopTab == null && nmLeftButton == null)
                        {
                            NmResetAll();
                            if (++nmBuildTries % 15 == 1) Plugin.L.LogInfo("Menu not built yet (game still loading?), retrying...");
                        }
                        else nmBuildTries = 0;
                    }
                }

                bool typing = false;
                foreach (var f in nmInputs) if (f != null && f.isFocused) { typing = true; break; }
                Plugin.Typing = typing;

                if (nmPage != null && nmPages.TryGetValue(nmPage, out var cur))
                {
                    try { cur.Tick?.Invoke(); } catch { }
                    ApplyPendingScroll();
                    if (Time.realtimeSinceStartup >= nmNextRefresh)
                    {
                        nmNextRefresh = Time.realtimeSinceStartup + 0.5f;
                        NmRefresh();
                        if (!cur.Root.activeInHierarchy) NmClose();   // the player closed the game menu
                        else { NmTabColors(); SaveScroll(); }
                    }
                }
                if (uiDirty && Time.realtimeSinceStartup >= uiNextSave) SaveUi();
            }
            catch (Exception e)
            {
                // reset and rebuild instead of giving up; only disable after repeated failures
                nmFailures++;
                Plugin.L.LogError($"Menu error ({nmFailures}/5), rebuilding: " + e);
                try { NmResetAll(); } catch { }
                nmContent = null; nmShopTab = null; nmLeftButton = null; nmShopTabImage = null; Plugin.Typing = false; nmNextCheck = 0;
                if (nmFailures >= 5) { nmBroken = true; Plugin.L.LogError("Native menu disabled after 5 errors."); }
            }
        }

        private void NmResetAll()
        {
            nmLabels.Clear(); nmEnabled.Clear(); nmVisible.Clear(); nmInputs.Clear(); nmHidden.Clear(); nmPages.Clear();
            nmLists.Clear(); nmHistory.Clear(); nmPage = null;
        }

        /// <summary>Hotkeys that drive the menu (called from Update, already blocked while typing).</summary>
        private void MenuHotkeys()
        {
            if (Input.GetKeyDown(Plugin.KeyMenu.Value)) ToggleMenu();
            if (nmPage != null && Input.GetMouseButtonDown(3)) Back();   // mouse "back" side button
        }

        // ======================================================================
        // Entry points: a tab in the menu bar + a round button at the top left
        // ======================================================================
        private GameObject nmShopTab, nmLeftButton;
        private UnityEngine.UI.Image nmShopTabImage;
        private bool nmOpening;

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
                    try
                    {
                        if (nmPage != null) return;   // already open: keep the current page
                        try { nmOpening = true; ShopManager.instance?.OpenInfoPanel(); } finally { nmOpening = false; }
                        var start = StartPage();
                        if (start != "home") nmHistory.Add("home");
                        NmShow(start);
                    }
                    catch (Exception e) { Toast("Error: " + e.GetBaseException().Message); }
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
                    try { ToggleMenu(); } catch (Exception e) { Toast("Error: " + e.GetBaseException().Message); }
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

        // Tab colour. The game marks the selected tab with a darker colour. We measure it from the other
        // tabs of the bar (darkest = selected, brightest = not selected) instead of reading the Info tab at a
        // given moment: the game may update its tabs a frame later, which used to invert our star tab.
        private Color? nmTabOn, nmTabOff;

        private static float Lum(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        private void MeasureTabColors()
        {
            if (nmShopTab == null || nmShopTab.transform.parent == null) return;
            var bar = nmShopTab.transform.parent;
            Color? dark = null, light = null;
            for (int i = 0; i < bar.childCount; i++)
            {
                var c = bar.GetChild(i);
                if (c == null || c.gameObject == nmShopTab || !c.gameObject.activeInHierarchy || c.name == "Close") continue;
                var img = c.GetComponent<UnityEngine.UI.Image>();
                if (img == null) continue;
                var col = img.color;
                if (dark == null || Lum(col) < Lum(dark.Value)) dark = col;
                if (light == null || Lum(col) > Lum(light.Value)) light = col;
            }
            if (dark != null && light != null && Lum(light.Value) - Lum(dark.Value) > 0.02f) { nmTabOn = dark; nmTabOff = light; }
        }

        private void NmTabColors()
        {
            var sm = ShopManager.instance;
            if (nmShopTabImage == null) return;
            if (nmTabOn == null || nmTabOff == null) MeasureTabColors();
            if (nmTabOn == null || nmTabOff == null) return;
            if (nmPage != null)
            {
                nmShopTabImage.color = nmTabOn.Value;
                if (sm != null && sm.infoPanelTabImage != null) sm.infoPanelTabImage.color = nmTabOff.Value;   // the game thinks Info is open
            }
            else nmShopTabImage.color = nmTabOff.Value;
        }

        // ======================================================================
        // Build everything once the game's Info panel exists
        // ======================================================================
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
            var tmpl = news.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (tmpl != null) { nmFont = tmpl.font; nmFontMat = tmpl.fontSharedMaterial; }
            foreach (var f in Resources.FindObjectsOfTypeAll<TMPro.TMP_InputField>())
                if (f != null && f.gameObject.hideFlags == HideFlags.None && f.textComponent != null) { nmInputTemplate = f.gameObject; break; }
            try
            {
                var w = nmContent.GetComponent<RectTransform>().rect.width;
                if (w > 200) nmWidth = w;
                nmScroll = nmContent.GetComponentInParent<UnityEngine.UI.ScrollRect>();
                nmScrollContent = nmScroll != null ? nmScroll.content : null;
            }
            catch { }

            NmBuildEntryPoints();
            MeasureTabColors();
            NmTabColors();
            NmBuildPages();
            Plugin.L.LogInfo($"Slayer Tools {ModVersion} menu added to the game menu ({nmPages.Count} pages).");
        }
    }
}
