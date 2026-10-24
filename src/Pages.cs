using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SlayerTools
{
    // ======================================================================
    // v6 pages. Every page is built once, when the game's Info panel exists,
    // and re-bound to live game data every time it opens.
    // ======================================================================
    public partial class ToolsBehaviour
    {
        private void NmBuildPages()
        {
            // each page is built on its own: a page that fails (e.g. after a game update) is logged and skipped,
            // the others still work, and the Home tiles leading to it are greyed out
            void page(string name, Action build)
            {
                try { build(); }
                catch (Exception e)
                {
                    Plugin.L.LogError($"Page '{name}' could not be built: {e}");
                    if (nmPages.TryGetValue(name, out var p)) { try { UnityEngine.Object.Destroy(p.Root); } catch { } nmPages.Remove(name); }
                }
            }
            page("home", BuildHome);
            page("search", BuildSearch);
            page("actions", BuildActions);
            page("events", BuildEvents);
            page("combat", BuildCombat);
            page("inv", () => BuildEditor("inv", "Edited stats are pinned (*): the game can no longer overwrite them.",
                () => (invProps ??= Collect(typeof(PlayerInventory), false)), () => PlayerInventory.instance, new[] { "All", "Favorites", "Pinned" }));
            page("sp", () => BuildEditor("sp", "Slayer Points internals. Type a value and press Enter.",
                () => (spProps ??= Collect(typeof(SlayerPoints), true)), () => null, new[] { "All", "Favorites" }));
            page("boost", () => BuildEditor("boost", "Every boost stat. Type a value and press Enter.",
                BoostProps, () => PlayerInventory.instance?.boost, new[] { "All", "Favorites" }));
            page("bag", BuildBag);
            page("divin", BuildDivinities);
            page("quests", BuildQuests);
            page("enemies", BuildEnemies);
            page("presets", BuildPresets);
            page("dash", BuildDashboard);
            page("log", BuildLog);
            page("settings", BuildSettings);
            page("whatsnew", BuildWhatsNew);
            if (!nmPages.ContainsKey("home")) throw new InvalidOperationException("the Home page could not be built");
        }

        // ---------------- small shared bits ----------------
        private static string Small(string s) => "<size=72%>" + s + "</size>";
        private string Badge(string title, string badge) => string.IsNullOrEmpty(badge) ? title : title + "\n" + Small(Col(CGrey, badge));

        private GameObject NavTile(Transform grid, string key, Func<string> badge, GameObject page) =>
            NmTile(grid, () => nmPages.ContainsKey(key) ? Badge(Title(key), badge?.Invoke()) : Badge(Title(key), "unavailable"), () => PageIcon(key), () => Go(key), page)
                .Let(t => EnableWhen(t, () => nmPages.ContainsKey(key), page));

        private static void LeftAlign(GameObject button)
        {
            var t = button.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (t != null) t.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
        }

        // ======================================================================
        // Home
        // ======================================================================
        private string SummaryText()
        {
            var inv = PlayerInventory.instance;
            if (inv == null) return Col(CGrey, "Waiting for the game...");
            return $"Coins {Col(CGold, Fmt(inv.coins))}   {Col(CGrey, "CpS")} {Fmt(inv.coinsPerSecond)}\n" +
                   $"Slayer Points {Col(CPurple, Fmt(SlayerPoints.points))}   {Col(CGrey, "Souls")} {Fmt(SlayerPoints.soulsLeft)}";
        }

        private List<string> ActiveEffects()
        {
            var l = new List<string>();
            if (GodMode) l.Add("God mode");
            if (OneShotBoss) l.Add("One-hit bosses");
            if (AutoSpheres) l.Add("Auto spheres");
            if (freezePortal) l.Add("Portal ready");
            if (freezeRage) l.Add("Rage ready");
            if (freezeBoost) l.Add("Boost ready");
            if (lockCoins) l.Add("Coins locked");
            if (!Mathf.Approximately(Time.timeScale, 1f)) l.Add("Speed x" + Time.timeScale.ToString("0.#", CultureInfo.InvariantCulture));
            return l;
        }

        private static bool PowerAvailable(Power p)
        {
            try
            {
                if (p.lockedBehindUpgrade != null && !p.lockedBehindUpgrade.bought) return false;
                var pl = PowersList.instance;
                return pl == null || pl.CheckRequirements(p);
            }
            catch { return false; }
        }

        // How many levels of one item the given coins can buy (the item's level is restored afterwards).
        private static int AffordableLevels(Power p, double coins, out double spent)
        {
            spent = 0; int n = 0, start = p.level;
            try
            {
                while (n < 100000)
                {
                    double cost = p.CalculateCost(1);
                    if (double.IsNaN(cost) || cost <= 0 || spent + cost > coins) break;
                    spent += cost; n++; p.level += 1;
                }
            }
            catch { }
            finally { p.level = start; }
            return n;
        }

        // "Buy max" over all equipment: list every unlocked item with how many levels the coins can buy,
        // buy the one with the most levels to max, then the next one with what is left, and so on.
        private void BuyEquipmentMax()
        {
            var inv = PlayerInventory.instance;
            if (inv?.powers == null) { Toast("Not in game yet"); return; }
            var items = inv.powers.Where(p => p != null && PowerAvailable(p)).ToList();
            if (items.Count == 0) { Toast("No equipment available yet"); return; }
            double coins0 = inv.coins;
            var levels0 = items.Select(p => (p, p.level)).ToList();
            var order = items.Select(p => (p, n: AffordableLevels(p, inv.coins, out _))).Where(x => x.n > 0).OrderByDescending(x => x.n).Select(x => x.p).ToList();
            int total = 0, touched = 0;
            foreach (var p in order)
            {
                int n = AffordableLevels(p, inv.coins, out double spent);   // with the coins left now
                if (n <= 0) continue;
                inv.coins -= spent; p.level += n; total += n; touched++;
            }
            if (total == 0) { Toast("Not enough coins for any equipment level"); return; }
            Recalculate();
            try { PowersList.instance?.RefreshList(); } catch { }
            if (lockCoins) lockedCoins = inv.coins;
            PushUndo("equipment buy max", () =>
            {
                var i = PlayerInventory.instance; if (i == null) return;
                foreach (var (p, l) in levels0) p.level = l;
                i.coins = coins0; Recalculate();
            });
            Toast($"{total} level(s) bought on {touched} item(s)");
        }

        private void AllEffectsOff()
        {
            GodMode = OneShotBoss = AutoSpheres = false;
            freezePortal = freezeRage = freezeBoost = lockCoins = false;
            Time.timeScale = 1f;
            SaveOptions();
            Toast("Every active effect is off");
        }

        private void BuildHome()
        {
            var page = NmPage("home", "Pick a page or search everything. F2 opens this menu anywhere.");
            NmText(page.transform, SummaryText, page, 0, 15);

            var act = NmBox(page.transform, "active", false, 6, 40);
            NmText(act.transform, () => Col(CGreen, "Active: ") + string.Join("  ·  ", ActiveEffects()), page, 40, 13).Let(g => Size(g, 40, -1, 1));
            NmButton(act.transform, () => "All off", AllEffectsOff, page, 40, 96, 0);
            ShowWhen(act, () => ActiveEffects().Count > 0, page);

            if (nmInputTemplate != null)
            {
                var srow = NmBox(page.transform, "search", false, 6, 44);
                var gs = NmInput(srow.transform, "Search stats, resources, divinities, enemies...");
                Action run = () =>
                {
                    var q = (gs.text ?? "").Trim();
                    if (q.Length == 0) { Toast("Type something to search"); return; }
                    ui.Search["search"] = q; gs.text = "";
                    Go("search");
                };
                gs.onSubmit.AddListener((Action<string>)(_ => run()));
                NmButton(srow.transform, () => "Search", run, page, 44, 96, 0);
            }

            // recently used pages (the most useful ones come back first)
            var recentTitle = NmSection(page.transform, "Recent", page);
            ShowWhen(recentTitle, () => ui.Recent.Any(k => nmPages.ContainsKey(k)), page);
            var rg = NmCols(page.transform, 3, 46);
            var recentTiles = new List<GameObject>();
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                Func<string> key = () => ui.Recent.Where(k => nmPages.ContainsKey(k)).Skip(idx).FirstOrDefault();
                var tile = NmTile(rg.transform, () => key() == null ? "" : Title(key()), null, () => { var k = key(); if (k != null) Go(k); }, page);
                ShowWhen(tile, () => key() != null, page);
                recentTiles.Add(tile);
            }
            nmPages["home"].OnShow = () =>
            {
                var keys = ui.Recent.Where(k => nmPages.ContainsKey(k)).Take(3).ToList();
                for (int i = 0; i < recentTiles.Count; i++) SetTileIcon(recentTiles[i], i < keys.Count ? PageIcon(keys[i]) : null);
            };

            NmSection(page.transform, "Play", page);
            var play = NmCols(page.transform, 3, 52);
            NavTile(play.transform, "actions", () => ActiveEffects().Count > 0 ? ActiveEffects().Count + " active" : "coins, SP, cooldowns", page);
            NavTile(play.transform, "events", () => IsRunnerNow() ? "boxes, bonus, ascension" : "run only", page);
            NavTile(play.transform, "combat", () => RevealMimics ? "mimics shown" : "mimics hidden", page);

            NmSection(page.transform, "Edit", page);
            var edit = NmCols(page.transform, 3, 52);
            NavTile(edit.transform, "inv", () => pinned.Count > 0 ? pinned.Count + " pinned" : "every stat", page);
            NavTile(edit.transform, "sp", () => PlayerInventory.instance != null ? Fmt(SlayerPoints.points) : "", page);
            NavTile(edit.transform, "boost", () => freezeBoost ? "always ready" : "speed, cooldown", page);
            NavTile(edit.transform, "bag", () => BagSummary(), page);
            NavTile(edit.transform, "divin", () => DivSummary(), page);
            NavTile(edit.transform, "quests", () => { int n = OpenQuests().Count(); return n > 0 ? n + " open" : "none open"; }, page);
            NavTile(edit.transform, "enemies", () => "kill counters", page);

            NmSection(page.transform, "Tools", page);
            var tools = NmCols(page.transform, 3, 52);
            NavTile(tools.transform, "presets", () => Presets().Count + " saved", page);
            NavTile(tools.transform, "dash", () => "live rates", page);
            NavTile(tools.transform, "log", () => journal.Count + " entries", page);
            NavTile(tools.transform, "settings", () => "menu, backups", page);
            NavTile(tools.transform, "whatsnew", () => "v" + ModVersion, page);

            // unpin every edited stat: the game recalculates them all from scratch
            var un = NmCols(page.transform, 1, 46);
            NmButton(un.transform, () => pinned.Count == 0 ? Col(CGrey, "No pinned stats") : ConfirmText("unpinall", $"Unpin all {pinned.Count} stats and recalculate"),
                () => { if (Confirm("unpinall")) UnpinAll(); }, page).Let(b => EnableWhen(b, () => pinned.Count > 0, page));
        }

        private string BagSummary()
        {
            try
            {
                var d = PlayerInventory.instance?.drops; if (d == null) return "resources";
                int found = d.Count(x => x != null && !x.locked);
                return found + " found";
            }
            catch { return "resources"; }
        }

        private string DivSummary()
        {
            try
            {
                var d = PlayerInventory.instance?.divinities; if (d == null) return "points";
                return d.Count(x => x != null && x.unlocked) + " / " + d.Count(x => x != null) + " active";
            }
            catch { return "points"; }
        }

        // ======================================================================
        // Search everything
        // ======================================================================
        private class Hit { public string Kind, Label, Page, Term; }

        private IEnumerable<Hit> AllHits()
        {
            foreach (var p in invProps ??= Collect(typeof(PlayerInventory), false)) yield return new Hit { Kind = "Inventory", Label = Pretty(p.Name), Page = "inv", Term = Pretty(p.Name) };
            foreach (var p in spProps ??= Collect(typeof(SlayerPoints), true)) yield return new Hit { Kind = "Slayer Points", Label = Pretty(p.Name), Page = "sp", Term = Pretty(p.Name) };
            foreach (var p in BoostProps()) yield return new Hit { Kind = "Boost", Label = Pretty(p.Name), Page = "boost", Term = Pretty(p.Name) };
            var inv = PlayerInventory.instance;
            if (inv?.drops != null) foreach (var d in inv.drops) if (d != null) yield return new Hit { Kind = "Bag", Label = DName(d), Page = "bag", Term = DName(d) };
            if (inv?.divinities != null) foreach (var d in inv.divinities) if (d != null) yield return new Hit { Kind = "Divinity", Label = DvName(d), Page = "divin", Term = DvName(d) };
            if (inv?.enemies != null) foreach (var e in inv.enemies) if (e != null) yield return new Hit { Kind = "Enemy", Label = EName(e), Page = "enemies", Term = EName(e) };
            foreach (var kv in PageMeta) if (kv.Key != "home" && kv.Key != "search") yield return new Hit { Kind = "Page", Label = kv.Value.title, Page = kv.Key, Term = null };
        }

        private void JumpTo(Hit h)
        {
            if (h.Term != null && nmLists.TryGetValue(h.Page, out var l))
            {
                ui.Search[h.Page] = h.Term;
                ui.Filter[h.Page] = 0;
                if (l.Search != null) l.Search.text = h.Term;
                l.Page = 0;
                Rebind(l);
            }
            Go(h.Page);
        }

        private void BuildSearch()
        {
            var page = NmPage("search", "Click a result to jump there, already filtered.");
            PagedList l = null;
            l = new PagedList
            {
                Key = "search",
                Source = () =>
                {
                    var q = (l.Search?.text ?? "").Trim();
                    return q.Length == 0 ? Enumerable.Empty<object>() : AllHits().Cast<object>();
                },
                Match = (o, q) => ((Hit)o).Label.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0,
                Filters = new[] { "All", "Stats", "Bag", "Divinities", "Enemies", "Pages" },
                Filter = (o, f) =>
                {
                    var h = (Hit)o;
                    switch (f)
                    {
                        case 1: return h.Page == "inv" || h.Page == "sp" || h.Page == "boost";
                        case 2: return h.Kind == "Bag";
                        case 3: return h.Kind == "Divinity";
                        case 4: return h.Kind == "Enemy";
                        case 5: return h.Kind == "Page";
                        default: return true;
                    }
                },
                Sort = x => x.OrderBy(o => ((Hit)o).Kind == "Page" ? 0 : 1).ThenBy(o => ((Hit)o).Label, StringComparer.OrdinalIgnoreCase),
            };
            NmList(page, l, "Search everything...", (row, item) =>
            {
                Func<Hit> H = () => item() as Hit;
                NmButton(row, () => { var h = H(); return h == null ? "" : Col(CGrey, h.Kind + "  ") + h.Label; }, () => { var h = H(); if (h != null) JumpTo(h); }, page, 46, -1, 1)
                    .Let(LeftAlign);
                NmButton(row, () => "Open >", () => { var h = H(); if (h != null) JumpTo(h); }, page, 46, 100, 0);
            });
            nmPages["search"].OnShow = () =>
            {
                if (l.Search != null && ui.Search.TryGetValue("search", out var q) && l.Search.text != q) l.Search.text = q;
                Rebind(l);
            };
            nmPages["search"].Tick = () => TickList(l);
        }

        // ======================================================================
        // Actions
        // ======================================================================
        private void BuildActions()
        {
            var page = NmPage("actions", "Hotkeys: F7 coins, F8 SP, F9 portal, F10 rage, F6 speed.");

            NmSection(page.transform, "Coins", page);
            var c = NmCols(page.transform, 2, 46);
            NmButton(c.transform, () => "+1 hour of coins", () => AddCoins(1), page);
            NmButton(c.transform, () => "+1 day of coins", () => AddCoins(24), page);
            NmButton(c.transform, () => "+1 week of coins", () => AddCoins(168), page);
            NmButton(c.transform, () => "Coins x2", () => Run("{\"cmd\":\"coinsx2\"}"), page);
            NmToggle(page.transform, "Lock coins", "your coins can no longer go down", () => lockCoins, v =>
            {
                lockCoins = v; if (PlayerInventory.instance != null) lockedCoins = PlayerInventory.instance.coins;
            }, page);

            NmSection(page.transform, "Equipment", page);
            var eq = NmCols(page.transform, 1, 46);
            NmButton(eq.transform, () => "Buy max equipment\n" + Small(Col(CGrey, "item with the most affordable levels first, then the next...")), BuyEquipmentMax, page);

            NmSection(page.transform, "Slayer Points", page);
            var s = NmCols(page.transform, 3, 46);
            NmButton(s.transform, () => "+10%", () => AddSP(10), page);
            NmButton(s.transform, () => "+50%", () => AddSP(50), page);
            NmButton(s.transform, () => "x2", () => AddSP(100), page);

            NmSection(page.transform, "Cooldowns", page);
            var cd = NmCols(page.transform, 3, 46);
            NmButton(cd.transform, () => "Portal" + CdText(PortalButton.instance?.currentCd), () => ResetPortal(), page);
            NmButton(cd.transform, () => "Rage" + CdText(RageModeManager.instance?.currentCd), () => ResetRage(), page);
            NmButton(cd.transform, () => "Boost" + CdText(PlayerInventory.instance?.boost?.currentCd), () => ResetBoost(), page);
            NmToggle(page.transform, "Portal always ready", null, () => freezePortal, v => freezePortal = v, page);
            NmToggle(page.transform, "Rage always ready", null, () => freezeRage, v => freezeRage = v, page);
            NmToggle(page.transform, "Boost always ready", null, () => freezeBoost, v => freezeBoost = v, page);

            NmSection(page.transform, "Game speed", page);
            var sp = NmCols(page.transform, 5, 44);
            foreach (var x in new[] { 1f, 2f, 5f, 10f, 20f })
            {
                float v = x;
                NmButton(sp.transform, () => Mathf.Approximately(Time.timeScale, v) ? Col(CGold, "x" + v) : "x" + v, () => SetSpeed(v), page);
            }
        }

        // ======================================================================
        // Events
        // ======================================================================
        private void BuildEvents()
        {
            var page = NmPage("events", "Spawns appear just ahead of you. The menu stays open: spam away.");
            NmText(page.transform, () => Col(COrange, "Spawns and portals only work during the normal run."), page, 0, 13)
                .Let(g => ShowWhen(g, () => !IsRunnerNow(), page));

            NmSection(page.transform, "Spawn", page);
            var g1 = NmCols(page.transform, 2, 48);
            void spawn(string label, string icon, Action a) =>
                NmTile(g1.transform, () => label, () => NmSprite(icon), a, page).Let(t => EnableWhen(t, IsRunnerNow, page));
            spawn("Purple box", "Star", () => SpawnBox(RandomBox.Type.Special, "Purple box"));
            spawn("Silver box", "Star Silver", () => SpawnBox(RandomBox.Type.Silver, "Silver box"));
            spawn("Golden box", "Star", () => SpawnBox(RandomBox.Type.Gold, "Golden box"));
            spawn("Normal box", "Star Silver", () => SpawnBox(RandomBox.Type.Normal, "Box"));
            spawn("Giant", "Face", SpawnGiantNow);
            spawn("Chest hunt key", "Checklist", SpawnKey);

            NmSection(page.transform, "Bonus Stage", page);
            NmText(page.transform, () => Col(CGrey, "Opens a portal and closes the menu: jump into it."), page, 0, 12);
            var g2 = NmCols(page.transform, 2, 46);
            void bonus(string label, Func<BaseMap> map) =>
                NmButton(g2.transform, () => label, () => LaunchBonus(map, label), page).Let(t => EnableWhen(t, IsRunnerNow, page));
            bonus("Your Bonus Stage", () => MapController.instance.CurrentBonusMap());
            bonus("Bonus Stage 1", () => Maps.list.BonusStage);
            bonus("Bonus Stage 2", () => Maps.list.BonusStage2);
            bonus("Bonus Stage 3", () => Maps.list.BonusStage3);

            NmSection(page.transform, "Ascension", page);
            NmText(page.transform, () => Col(CGrey, "Next: ") + AscensionPreview(), page, 0, 12);
            var g3 = NmCols(page.transform, 2, 46);
            NmButton(g3.transform, () => ConfirmText("asc", "Ascension"), () => DoAscend(false), page);
            NmButton(g3.transform, () => ConfirmText("ultra", "Ultra ascension"), () => DoAscend(true), page);

            NmSection(page.transform, "Minions", page);
            var g4 = NmCols(page.transform, 3, 46);
            NmButton(g4.transform, () => "Finish missions", MinionsFinish, page);
            NmButton(g4.transform, () => "Claim all", MinionsClaim, page);
            NmButton(g4.transform, () => "Send all", MinionsSend, page);
        }

        // ======================================================================
        // Combat & chests
        // ======================================================================
        private void BuildCombat()
        {
            var page = NmPage("combat", "These options are remembered between sessions.");
            NmText(page.transform, () => ChestStatus(), page, 0, 13);

            NmSection(page.transform, "Chest hunt", page);
            NmToggle(page.transform, "Show mimics", "mimics turn red with MIMIC written on them", () => RevealMimics, v => { RevealMimics = v; ClearChestMarks(); }, page);
            NmToggle(page.transform, "Show every chest", "label each chest with what it contains", () => RevealAll, v => { RevealAll = v; ClearChestMarks(); }, page);
            var k = NmCols(page.transform, 1, 44);
            NmButton(k.transform, () => "Spawn a chest hunt key", SpawnKey, page).Let(t => EnableWhen(t, IsRunnerNow, page));

            NmSection(page.transform, "Bosses", page);
            NmToggle(page.transform, "God mode", "no lives lost against bosses or Bonus Stage saws", () => GodMode, v => GodMode = v, page);
            NmToggle(page.transform, "One-hit bosses", "every hit kills the boss", () => OneShotBoss, v => OneShotBoss = v, page);

            NmSection(page.transform, "Bonus Stage", page);
            NmToggle(page.transform, "Auto bonus spheres", "spheres fly to you by themselves", () => AutoSpheres, v => AutoSpheres = v, page);
        }

        // ======================================================================
        // Stat editors (Inventory, Slayer Points, Boost)
        // ======================================================================
        private void BuildEditor(string key, string hint, Func<List<EditableProp>> props, Func<object> target, string[] filters)
        {
            var page = NmPage(key, hint + "  Click a name to add it to your favorites.");

            if (key == "inv")
            {
                var bar = NmBox(page.transform, "pins", false, 6, 40);
                NmText(bar.transform, () => pinned.Count == 0 ? Col(CGrey, "No pinned stat yet.") : Col(CBlue, pinned.Count + " pinned stat(s)") + Col(CGrey, " kept after recalculations and restarts"), page, 40, 13)
                    .Let(g => Size(g, 40, -1, 1));
                NmButton(bar.transform, () => ConfirmText("unpinall", "Unpin all"), () => { if (Confirm("unpinall")) { UnpinAll(); if (nmLists.TryGetValue("inv", out var l0)) Rebind(l0); } }, page, 40, 130, 0)
                    .Let(g => EnableWhen(g, () => pinned.Count > 0, page));
            }
            if (key == "boost")
            {
                var g = NmCols(page.transform, 1, 44);
                NmButton(g.transform, () => "Boost ready now" + CdText(PlayerInventory.instance?.boost?.currentCd), () => ResetBoost(), page);
                NmToggle(page.transform, "Boost always ready", null, () => freezeBoost, v => freezeBoost = v, page);
            }

            PagedList l = null;
            l = new PagedList
            {
                Key = key,
                Source = () => props().Cast<object>(),
                Match = (o, q) => Matches(((EditableProp)o).Name, q),
                Filters = filters,
                Filter = (o, f) =>
                {
                    var p = (EditableProp)o;
                    if (f == 1) return IsFav(key, p.Name);
                    if (f == 2) return pinned.ContainsKey(p.Name);
                    return true;
                },
                Sort = x => x.OrderBy(o => IsFav(key, ((EditableProp)o).Name) ? 0 : 1)
                             .ThenBy(o => ((EditableProp)o).ReadOnly ? 1 : 0)
                             .ThenBy(o => Pretty(((EditableProp)o).Name), StringComparer.OrdinalIgnoreCase),
            };

            NmList(page, l, "Search (e.g. ruby, chance, damage)", (row, item) =>
            {
                Func<EditableProp> P = () => item() as EditableProp;
                Func<bool> isBool = () => P()?.Prop.PropertyType == typeof(bool);

                NmButton(row, () =>
                {
                    var p = P(); if (p == null) return "";
                    string name = (IsFav(key, p.Name) ? Col(CGold, "* ") : "") + Pretty(p.Name);
                    if (key == "inv" && pinned.ContainsKey(p.Name)) name = Col(CBlue, name);
                    if (p.ReadOnly) name += "  " + Col(CGrey, Small(ValueText(p, target()) + "  (read-only)"));
                    return name;
                }, () => { var p = P(); if (p != null) ToggleFav(key, p.Name); }, page, 46, -1, 1).Let(LeftAlign);

                var max = NmButton(row, () => "Max", () => { var p = P(); if (p != null) Quick(key, p, target, 1, 100); }, page, 46, 62, 0);
                ShowWhen(max, () => { var p = P(); return p != null && !p.ReadOnly && !isBool() && IsChance(p.Name); }, page);

                TMPro.TMP_InputField input = null;
                if (nmInputTemplate != null)
                {
                    input = NmInput(row, "value", 46, 140, 0);
                    var inp = input;
                    inp.onSubmit.AddListener((Action<string>)(txt =>
                    {
                        var p = P(); if (p == null || p.ReadOnly) return;
                        if (txt == EditText(p, target())) return;   // nothing changed
                        ApplyTo(key, p, target(), txt);
                        inp.text = EditText(p, target());
                        if (key == "inv") Rebind(l);
                        NmRefresh();
                    }));
                    ShowWhen(input.gameObject, () => { var p = P(); return p != null && !p.ReadOnly && !isBool(); }, page);
                }
                var tog = NmButton(row, () => { var p = P(); return p == null ? "" : OnOff(ValueText(p, target()) == "yes"); },
                    () => { var p = P(); if (p != null && !p.ReadOnly) ApplyTo(key, p, target(), null); }, page, 46, 140, 0);
                ShowWhen(tog, () => P() != null && !P().ReadOnly && isBool(), page);

                var unpin = NmButton(row, () => "Unpin", () => { var p = P(); if (p != null) { Unpin(p.Name); Rebind(l); } }, page, 46, 84, 0);
                ShowWhen(unpin, () => { var p = P(); return key == "inv" && p != null && pinned.ContainsKey(p.Name); }, page);

                if (input != null)
                {
                    var inp = input;
                    l.OnBind.Add(() =>
                    {
                        var p = P(); if (p == null || inp.isFocused) return;
                        var t = p.ReadOnly ? "" : EditText(p, target());
                        if (inp.text != t) inp.text = t;
                    });
                }
            });

            float next = 0;
            nmPages[key].OnShow = () => Rebind(l);
            nmPages[key].Tick = () =>
            {
                TickList(l);
                if (Time.realtimeSinceStartup < next) return;
                next = Time.realtimeSinceStartup + 1f;
                foreach (var b in l.OnBind) try { b(); } catch { }
            };
        }

        // ======================================================================
        // Bag
        // ======================================================================
        private static readonly double[] BagSteps = { 1, 10, 100, 1000, 10000, 1000000 };
        private double BagStepValue => BagSteps[Mathf.Clamp(ui.BagStep, 0, BagSteps.Length - 1)];

        private void BuildBag()
        {
            var page = NmPage("bag", "Add or remove resources with their real icons. Undo works here too.");
            var bar = NmCols(page.transform, 3, 44);
            NmButton(bar.transform, () => "Step: " + Col(CGold, Fmt(BagStepValue)), () => { ui.BagStep = (ui.BagStep + 1) % BagSteps.Length; SaveUi(); }, page);
            NmButton(bar.transform, () => "Over limit: " + OnOff(BagOverLimit), () => { BagOverLimit = !BagOverLimit; SaveOptions(); Toast(BagOverLimit ? "You can now go past the bag size" : "The bag size limit applies again"); }, page);
            NmButton(bar.transform, () => "Fill everything", () => { BagFillAll(); if (nmLists.TryGetValue("bag", out var l0)) Rebind(l0); }, page);

            PagedList l = null;
            l = new PagedList
            {
                Key = "bag",
                Source = () => PlayerInventory.instance?.drops?.Where(d => d != null).Cast<object>() ?? Enumerable.Empty<object>(),
                Match = (o, q) => DName((Drop)o).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || (((Drop)o).name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0,
                Filters = new[] { "All", "Found", "Not found", "Not full" },
                Filter = (o, f) =>
                {
                    var d = (Drop)o;
                    if (f == 1) return !d.locked;
                    if (f == 2) return d.locked;
                    if (f == 3) { double m = DMax(d); return !d.locked && m > 0 && d.amount < m; }
                    return true;
                },
                Sort = x => x.OrderBy(o => ((Drop)o).locked ? 1 : 0)
                             .ThenBy(o => { try { var c = ((Drop)o).category; return c != null ? c.sortingOrder : 999; } catch { return 999; } })
                             .ThenBy(o => DName((Drop)o), StringComparer.OrdinalIgnoreCase),
            };
            NmList(page, l, "Search (e.g. egg, scale, key)", (row, item) =>
            {
                Func<Drop> D = () => item() as Drop;
                var tile = NmTile(row, () => { var d = D(); return d == null ? "" : DLabel(d); }, null, null, page);
                Size(tile, 46, -1, 1);
                NmButton(row, () => "-" + Fmt(BagStepValue), () => { var d = D(); if (d != null) BagAdd(d, -BagStepValue); }, page, 46, 70, 0);
                NmButton(row, () => "+" + Fmt(BagStepValue), () => { var d = D(); if (d != null) BagAdd(d, BagStepValue); }, page, 46, 70, 0);
                if (nmInputTemplate != null)
                {
                    var input = NmInput(row, "set", 46, 96, 0);
                    input.onSubmit.AddListener((Action<string>)(txt =>
                    {
                        var d = D(); if (d == null) return;
                        if (!TryParseNumber(txt, out double v)) { Toast("Invalid number: " + txt); return; }
                        BagSet(d, v); input.text = ""; NmRefresh();
                    }));
                }
                NmButton(row, () => "Full", () =>
                {
                    var d = D(); if (d == null) return;
                    double max = DMax(d);
                    if (max <= 0 || max > 1e15) { Toast(DName(d) + ": no known limit"); return; }
                    BagSet(d, max);
                }, page, 46, 70, 0);
                l.OnBind.Add(() => { var d = D(); if (d != null) SetTileIcon(tile, d.icon, d.locked ? 0.4f : 1f); });
            });
            nmPages["bag"].OnShow = () => Rebind(l);
            nmPages["bag"].Tick = () => TickList(l);
        }

        // ======================================================================
        // Divinities
        // ======================================================================
        private void BuildDivinities()
        {
            var page = NmPage("divin", "Missing points are added automatically. Deactivating refunds them.");
            NmText(page.transform, DivHeader, page, 0, 14);
            var g = NmCols(page.transform, 4, 44);
            NmButton(g.transform, () => "+1 pt", () => AddDivPoints(1), page);
            NmButton(g.transform, () => "+10 pts", () => AddDivPoints(10), page);
            NmButton(g.transform, () => "+100 pts", () => AddDivPoints(100), page);
            NmButton(g.transform, () => "+1000 pts", () => AddDivPoints(1000), page);

            PagedList l = null;
            var g2 = NmCols(page.transform, 2, 44);
            NmButton(g2.transform, () => "Activate all", () =>
            {
                var inv = PlayerInventory.instance; if (inv?.divinities == null) { Toast("Not in game yet"); return; }
                int n = 0;
                BeginUndoBatch();
                try { foreach (var d in inv.divinities.ToArray()) if (d != null && !d.unlocked) { ActivateDiv(d); n++; } }
                finally { EndUndoBatch("activate all divinities"); }
                Toast(n + " divinit(y/ies) activated");
                Rebind(l);
            }, page);
            NmButton(g2.transform, () => ConfirmText("divoff", "Deactivate all"), () =>
            {
                if (!Confirm("divoff")) return;
                var inv = PlayerInventory.instance; if (inv?.divinities == null) { Toast("Not in game yet"); return; }
                int n = 0;
                BeginUndoBatch();
                try { foreach (var d in inv.divinities.ToArray()) if (d != null && d.unlocked) { DeactivateDiv(d); n++; } }
                finally { EndUndoBatch("deactivate all divinities"); }
                Toast(n + " divinit(y/ies) deactivated, points refunded");
                Rebind(l);
            }, page);

            l = new PagedList
            {
                Key = "divin",
                Source = () => PlayerInventory.instance?.divinities?.Where(d => d != null).Cast<object>() ?? Enumerable.Empty<object>(),
                Match = (o, q) => DvName((Divinity)o).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0,
                Filters = new[] { "All", "Active", "Inactive", "Dark" },
                Filter = (o, f) =>
                {
                    var d = (Divinity)o;
                    if (f == 1) return d.unlocked;
                    if (f == 2) return !d.unlocked;
                    if (f == 3) return d.isDark;
                    return true;
                },
                Sort = x => x.OrderBy(o => ((Divinity)o).isDark ? 1 : 0).ThenBy(o => ((Divinity)o).cost).ThenBy(o => DvName((Divinity)o)),
            };
            NmList(page, l, "Search divinities", (row, item) =>
            {
                Func<Divinity> V = () => item() as Divinity;
                var tile = NmTile(row, () =>
                {
                    var d = V(); if (d == null) return "";
                    return (d.unlocked ? Col(CGreen, "ACTIVE  ") : "") + DvName(d) + (d.isDark ? Col(CPurple, "  (dark)") : "") + "\n" + Small(Col(CGrey, Fmt(d.cost) + " points"));
                }, null, null, page);
                Size(tile, 46, -1, 1);
                NmButton(row, () => { var d = V(); return d == null ? "" : d.unlocked ? Col(CGrey, "Deactivate") : Col(CGreen, "Activate"); }, () =>
                {
                    var d = V(); if (d == null) return;
                    if (d.unlocked) DeactivateDiv(d); else ActivateDiv(d);
                    if (ListFilter(l) != 0) Rebind(l);
                }, page, 46, 130, 0);
                l.OnBind.Add(() => { var d = V(); if (d != null) SetTileIcon(tile, d.icon); });
            });
            nmPages["divin"].OnShow = () => Rebind(l);
            nmPages["divin"].Tick = () => TickList(l);
        }

        // ======================================================================
        // Quests
        // ======================================================================
        private class QItem { public Quest Q; public string Kind; }

        private IEnumerable<QItem> OpenQuests() => AllQuests().Where(x => QOpen(x.q)).Select(x => new QItem { Q = x.q, Kind = x.kind });

        private void BuildQuests()
        {
            var page = NmPage("quests", "Complete a quest here, then claim its reward in the game as usual.");
            PagedList l = null;
            var g = NmCols(page.transform, 1, 44);
            NmButton(g.transform, () => { int n = OpenQuests().Count(x => !SafeDone(x.Q)); return n > 0 ? $"Complete all ({n})" : Col(CGrey, "Nothing left to complete"); }, () =>
            {
                int n = 0;
                foreach (var x in OpenQuests().ToList()) if (!SafeDone(x.Q)) { x.Q.ForceComplete(false); n++; }
                Toast(n + " quest(s) completed: go claim them in the game");
                Rebind(l);
            }, page).Let(b => EnableWhen(b, () => OpenQuests().Any(x => !SafeDone(x.Q)), page));
            var q2 = NmCols(page.transform, 2, 46);
            NmButton(q2.transform, () => "Buy all upgrades\n" + Small(Col(CGrey, "unlocks new quests")), () =>
            {
                var sm = ShopManager.instance; if (sm == null || PlayerInventory.instance == null) { Toast("Not in game yet"); return; }
                int before = OpenQuests().Count(), bought = 0;
                // the game's own BuyAllUpgrades() crashes when its Upgrades tab was never opened (its list is
                // not built yet), so buy them one by one, with the same rules (cost, requirements)
                var all = PlayerInventory.instance?.upgrades;
                if (all == null) { Toast("Upgrades not loaded yet"); return; }
                foreach (var u in all)
                {
                    if (u == null || u.bought) continue;
                    try { if (sm.BuyUpgrade(u, false, false, true)) bought++; } catch { }
                }
                try { UpgradesList.instance?.RefreshList(); } catch { }
                Toast($"{bought} upgrade(s) bought, {Math.Max(0, OpenQuests().Count() - before)} new quest(s)");
                Rebind(l);
            }, page);
            NmButton(q2.transform, () => { int n = OpenQuests().Count(); return n > 0 ? $"Complete + claim all ({n})" : Col(CGrey, "Nothing to claim"); }, () =>
            {
                int n = 0;
                foreach (var x in OpenQuests().ToList()) { try { x.Q.ForceComplete(true); n++; } catch (Exception e) { Plugin.L.LogWarning("Claim: " + e.Message); } }
                Toast(n + " quest(s) completed and claimed");
                Rebind(l);
            }, page).Let(b => EnableWhen(b, () => OpenQuests().Any(), page));

            l = new PagedList
            {
                Key = "quests",
                Source = () => OpenQuests().Cast<object>(),
                Match = (o, q) => QName(((QItem)o).Q).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0,
                Filters = new[] { "All", "Quests", "Daily", "Weekly", "To claim" },
                Filter = (o, f) =>
                {
                    var x = (QItem)o;
                    if (f == 1) return x.Kind == "Quest";
                    if (f == 2) return x.Kind == "Daily";
                    if (f == 3) return x.Kind == "Weekly";
                    if (f == 4) return SafeDone(x.Q);
                    return true;
                },
                Sort = x => x.OrderBy(o => SafeDone(((QItem)o).Q) ? 1 : 0),
            };
            NmList(page, l, "Search quests", (row, item) =>
            {
                Func<QItem> Q = () => item() as QItem;
                NmButton(row, () =>
                {
                    var x = Q(); if (x == null) return "";
                    return Col(CGrey, x.Kind + "  ") + QName(x.Q) + "\n" + Small(Col(CGrey, $"{x.Q.questCurrentGoal:N0} / {x.Q.questGoal:N0}"));
                }, null, page, 46, -1, 1).Let(LeftAlign);
                NmButton(row, () => { var x = Q(); return x == null ? "" : SafeDone(x.Q) ? Col(CGreen, "To claim") : "Complete"; }, () =>
                {
                    var x = Q(); if (x == null) return;
                    if (SafeDone(x.Q)) { Toast("Already completed: claim it in the game"); return; }
                    x.Q.ForceComplete(false);
                    Toast(QName(x.Q) + " completed!");
                }, page, 46, 130, 0);
            });
            nmPages["quests"].OnShow = () => Rebind(l);
            nmPages["quests"].Tick = () => TickList(l);
        }

        private static bool SafeDone(Quest q) { try { return q.IsCompleted(); } catch { return false; } }

        // ======================================================================
        // Enemies
        // ======================================================================
        private void BuildEnemies()
        {
            var page = NmPage("enemies", "Type a kill count and press Enter. The achievement unlocks on the next kill.");
            PagedList l = null;
            l = new PagedList
            {
                Key = "enemies",
                Source = () => PlayerInventory.instance?.enemies?.Where(e => e != null).Cast<object>() ?? Enumerable.Empty<object>(),
                Match = (o, q) => EName((Enemy)o).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || (((Enemy)o).name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0,
                Sort = x => x.OrderBy(o => EName((Enemy)o), StringComparer.OrdinalIgnoreCase),
            };
            NmList(page, l, "Search (e.g. goblin, demon)", (row, item) =>
            {
                Func<Enemy> E = () => item() as Enemy;
                NmButton(row, () => { var e = E(); return e == null ? "" : EName(e) + "  " + Col(CGrey, Small(e.killCount.ToString("N0") + " kills")); }, null, page, 46, -1, 1).Let(LeftAlign);
                if (nmInputTemplate != null)
                {
                    var input = NmInput(row, "kills", 46, 150, 0);
                    input.onSubmit.AddListener((Action<string>)(txt =>
                    {
                        var e = E(); if (e == null) return;
                        if (!TryParseNumber(txt, out double d)) { Toast("Invalid number: " + txt); return; }
                        double old = e.killCount;
                        PushUndo(EName(e) + " kills", () => e.killCount = old);
                        e.killCount = Math.Max(0, d);
                        input.text = "";
                        Toast(EName(e) + ": " + d.ToString("N0") + " kills (the achievement unlocks on the next kill)");
                    }));
                }
            });
            nmPages["enemies"].OnShow = () => Rebind(l);
            nmPages["enemies"].Tick = () => TickList(l);
        }

        // ======================================================================
        // Presets
        // ======================================================================
        private PagedList presetList;
        private void NmFillPresets() { if (presetList != null) Rebind(presetList); }

        private void BuildPresets()
        {
            var page = NmPage("presets", "A preset saves your currently pinned stats under a name.");
            NmText(page.transform, () => pinned.Count == 0 ? Col(COrange, "Pin some stats first (edit them in Inventory).") : Col(CGrey, $"Saving now would store {pinned.Count} pinned stat(s)."), page, 0, 13);
            if (nmInputTemplate != null)
            {
                var row = NmBox(page.transform, "save", false, 6, 44);
                var name = NmInput(row.transform, "Preset name, then Enter");
                name.onSubmit.AddListener((Action<string>)(txt => { SavePreset(txt); name.text = ""; }));
                NmButton(row.transform, () => "Save", () => { SavePreset(name.text); name.text = ""; }, page, 44, 96, 0)
                    .Let(g => EnableWhen(g, () => pinned.Count > 0, page));
            }
            PagedList l = null;
            l = new PagedList
            {
                Key = "presets",
                Source = () => Presets().Keys.Cast<object>(),
                Match = (o, q) => ((string)o).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0,
                Sort = x => x.OrderBy(o => (string)o, StringComparer.OrdinalIgnoreCase),
            };
            presetList = l;
            NmList(page, l, "Search presets", (row, item) =>
            {
                Func<string> N = () => item() as string;
                NmButton(row, () => { var n = N(); return n == null ? "" : n + "  " + Col(CGrey, Small((Presets().TryGetValue(n, out var p) ? p.Count : 0) + " stats")); }, null, page, 46, -1, 1).Let(LeftAlign);
                NmButton(row, () => "Apply", () => { var n = N(); if (n != null) { ApplyPreset(n); } }, page, 46, 96, 0);
                NmButton(row, () => { var n = N(); return n == null ? "" : ConfirmText("delpreset:" + n, "Delete"); }, () =>
                {
                    var n = N(); if (n == null || !Confirm("delpreset:" + n)) return;
                    var copy = new Dictionary<string, double>(Presets()[n]);
                    Presets().Remove(n); SavePresets();
                    PushUndo("delete preset " + n, () => { Presets()[n] = copy; SavePresets(); NmFillPresets(); });
                    Toast($"Preset \"{n}\" deleted");
                    Rebind(l);
                }, page, 46, 110, 0);
            });
            nmPages["presets"].OnShow = () => Rebind(l);
            nmPages["presets"].Tick = () => TickList(l);
        }

        // ======================================================================
        // Dashboard and log
        // ======================================================================
        private void BuildDashboard()
        {
            var page = NmPage("dash", "Rates are measured over the last 2 minutes.");
            NmText(page.transform, DashText, page, 0, 13);
            var g = NmCols(page.transform, 1, 44);
            NmButton(g.transform, () => "Reset the measurement", () => { ResetRates(); sessionStart = Time.realtimeSinceStartup; Toast("Measurement reset"); }, page);
        }

        private void BuildLog()
        {
            var page = NmPage("log", "Newest first. Everything is also written to BepInEx\\slayer_log.txt.");
            NmText(page.transform, JournalText, page, 0, 11);
            var g = NmCols(page.transform, 1, 44);
            NmButton(g.transform, () => "Clear the log", () => { journal.Clear(); Toast("Log cleared"); }, page);
        }

        // ======================================================================
        // Settings
        // ======================================================================
        private static readonly int[] RowChoices = { 6, 8, 10, 12, 15 };

        private void BuildSettings()
        {
            var page = NmPage("settings", "Everything here is saved automatically.");

            NmSection(page.transform, "Menu", page);
            NmToggle(page.transform, "Reopen where I left it", "otherwise the menu always opens on Home", () => !ui.OpenAtHome, v => { ui.OpenAtHome = !v; SaveUi(true); }, page);
            NmToggle(page.transform, "Confirm dangerous actions", "ascension, restore, unpin all, delete... need a second click", () => ui.ConfirmDanger, v => { ui.ConfirmDanger = v; SaveUi(true); }, page);
            NmToggle(page.transform, "Friendly stat names", "off = the game's code names (rubyChance...)", () => FriendlyNames, v => { FriendlyNames = v; foreach (var l in nmLists.Values) Rebind(l); }, page);
            var g = NmCols(page.transform, 2, 44);
            NmButton(g.transform, () => "Rows per page: " + Col(CGold, ui.RowsPerPage.ToString()), () =>
            {
                int i = Array.IndexOf(RowChoices, ui.RowsPerPage);
                ui.RowsPerPage = RowChoices[(i + 1) % RowChoices.Length];
                SaveUi(true);
                foreach (var l in nmLists.Values) Rebind(l);
            }, page);
            NmButton(g.transform, () => ConfirmText("forget", "Forget menu memory"), () =>
            {
                if (!Confirm("forget")) return;
                var keep = ui; ui = new UiState { RowsPerPage = keep.RowsPerPage, OpenAtHome = keep.OpenAtHome, ConfirmDanger = keep.ConfirmDanger, SeenVersion = keep.SeenVersion, BagStep = keep.BagStep };
                foreach (var l in nmLists.Values) { if (l.Search != null) l.Search.text = ""; l.Page = 0; Rebind(l); }
                SaveUi(true);
                Toast("Searches, filters, scroll positions and recent pages forgotten");
            }, page);

            NmSection(page.transform, "Pinned stats", page);
            var p = NmBox(page.transform, "pins", false, 6, 44);
            NmText(p.transform, () => pinned.Count + " pinned stat(s)" + Col(CGrey, Small("  kept after recalculations and restarts")), page, 44, 14).Let(x => Size(x, 44, -1, 1));
            NmButton(p.transform, () => ConfirmText("unpinall", "Unpin all"), () => { if (Confirm("unpinall")) UnpinAll(); }, page, 44, 130, 0)
                .Let(x => EnableWhen(x, () => pinned.Count > 0, page));

            NmSection(page.transform, "Save backups", page);
            NmText(page.transform, BackupInfo, page, 0, 13);
            var b = NmCols(page.transform, 3, 44);
            NmButton(b.transform, () => "Back up now", () =>
            {
                var d = BackupSave("manual", true);
                Toast(d != null ? "Save copied: " + Path.GetFileName(d) : "Copy failed (see the log file)");
            }, page);
            NmButton(b.transform, () => "Older backup", () => { var list = Backups(); if (list.Count > 0) restoreIndex = (restoreIndex + 1) % list.Count; }, page)
                .Let(x => EnableWhen(x, () => Backups().Count > 1, page));
            NmButton(b.transform, () => File.Exists(RestoreMarker) ? Col(COrange, "Cancel restore") : ConfirmText("restore", "Restore"), RestoreClick, page)
                .Let(x => EnableWhen(x, () => Backups().Count > 0, page));

            NmSection(page.transform, "Hotkeys", page);
            NmText(page.transform, () =>
                $"{Plugin.KeyMenu.Value}  open / close this menu      Mouse back button  previous page\n" +
                $"{Plugin.KeySpeed.Value}  game speed   {Plugin.KeyCoins.Value}  coins   {Plugin.KeySP.Value}  SP   {Plugin.KeyPortal.Value}  portal   {Plugin.KeyRage.Value}  rage   F11  UI dump\n" +
                Col(CGrey, Small("Change them in BepInEx\\config\\leo.idleslayer.slayertools.cfg")), page, 0, 12);

            NmSection(page.transform, "Advanced", page);
            var a = NmCols(page.transform, 2, 44);
            NmButton(a.transform, () => "Analyze the game UI", () => Toast("UI analyzed: " + DumpUI()), page);
            NmButton(a.transform, () => "What's new", () => Go("whatsnew"), page);
            NmText(page.transform, () => Col(CGrey, $"Slayer Tools {ModVersion}  ·  game {SafeVersion()}  ·  single-player only: Jewels of Soul, purchases and the online casino are never touched."), page, 0, 11);
        }

        private string BackupInfo()
        {
            var list = Backups();
            if (list.Count == 0) return Col(CGrey, "No backup yet. One is made at the start of every session.");
            restoreIndex = Math.Min(restoreIndex, list.Count - 1);
            var pending = File.Exists(RestoreMarker) ? Col(COrange, "  Restore scheduled: restart the game.") : "";
            return $"{list.Count} backup(s). Selected: {Col(CGold, Path.GetFileName(list[restoreIndex]))}" + pending;
        }

        private static string SafeVersion() { try { return Application.version; } catch { return "?"; } }

        // ======================================================================
        // What's new (shown once after an update)
        // ======================================================================
        private void BuildWhatsNew()
        {
            var page = NmPage("whatsnew", "Shown once after each update.");
            NmText(page.transform, () =>
                Col(CGold, "Slayer Tools " + ModVersion) + "\n\n" +
                "- The menu reopens exactly where you left it: page, scroll, searches, filters.\n" +
                "- Same layout everywhere: Back / title / Home, one status bar, Undo always there.\n" +
                "- New Home: live summary, active effects with \"All off\", recent pages, search.\n" +
                "- Search everything at once: stats, resources, divinities, enemies, pages.\n" +
                "- Long lists are paged and filterable (favorites, pinned, found, active...).\n" +
                "- Buttons that cannot work right now are greyed out instead of failing.\n" +
                "- Real ON/OFF switches with a description for every option.\n" +
                "- Dangerous actions ask for a second click; more of them can be undone.\n" +
                "- F2 opens the menu from anywhere; the mouse back button goes back.",
                page, 0, 13);
            var g = NmCols(page.transform, 1, 44);
            NmButton(g.transform, () => "Got it", () => Go("home"), page);
        }
    }
}
