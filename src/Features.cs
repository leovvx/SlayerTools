using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using BepInEx;
using UnityEngine;

namespace SlayerTools
{
    // ======================================================================
    // Features added in 5.0: undo, favorites, friendly names, quick buttons,
    // events (boxes, giant, key, bonus stage, ascension), god mode, minions,
    // mimic reveal, dashboard, log, version check, save backups.
    // ======================================================================
    public partial class ToolsBehaviour
    {
        internal const string ExpectedGameVersion = "7.2.0";

        // ---------------- options (saved in the config) ----------------
        internal static bool GodMode, OneShotBoss, AutoSpheres, RevealMimics = true, RevealAll, FriendlyNames = true;

        // ---------------- 1) Undo ----------------
        private class UndoEntry { public string Label; public Action Undo; }
        private readonly List<UndoEntry> undo = new List<UndoEntry>();

        private void PushUndo(string label, Action action)
        {
            undo.Add(new UndoEntry { Label = label, Undo = action });
            if (undo.Count > 10) undo.RemoveAt(0);
        }

        private string UndoLabel() => undo.Count == 0 ? "Undo: nothing to undo" : "Undo: " + undo[undo.Count - 1].Label + $"  ({undo.Count})";

        private void DoUndo()
        {
            if (undo.Count == 0) { Toast("Nothing to undo"); return; }
            var e = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            try { e.Undo(); Toast("Undone: " + e.Label); }
            catch (Exception ex) { Toast("Undo failed: " + ex.GetBaseException().Message); }
            ResetRates();
        }

        // remember a stat's state (value + pin) before changing it
        private void RememberProp(EditableProp p, object target, string display)
        {
            try
            {
                if (!p.IsStatic && target == null) return;
                object old = p.Prop.GetValue(p.IsStatic ? null : target);
                bool wasPinned = !p.IsStatic && p.Prop.DeclaringType == typeof(PlayerInventory) && pinned.TryGetValue(p.Name, out _);
                double oldPin = wasPinned ? pinned[p.Name] : 0;
                bool isInv = !p.IsStatic && p.Prop.DeclaringType == typeof(PlayerInventory);
                PushUndo(display, () =>
                {
                    var t = p.IsStatic ? null : (isInv ? (object)PlayerInventory.instance : target);
                    p.Prop.SetValue(t, old);
                    if (isInv)
                    {
                        if (wasPinned) pinned[p.Name] = oldPin; else pinned.Remove(p.Name);
                        SavePinned();
                        if (p.Name == "coins" && lockCoins && PlayerInventory.instance != null) lockedCoins = PlayerInventory.instance.coins;
                    }
                });
            }
            catch { }
        }

        private void RememberCoinsAndSP(string label)
        {
            var inv = PlayerInventory.instance;
            if (inv == null) return;
            double c = inv.coins, sp = SlayerPoints.points, souls = SlayerPoints.soulsLeft;
            PushUndo(label, () =>
            {
                var i = PlayerInventory.instance; if (i == null) return;
                i.coins = c; SlayerPoints.points = sp; SlayerPoints.soulsLeft = souls;
                if (lockCoins) lockedCoins = c;
            });
        }

        // ---------------- 2) Favorites ----------------
        private HashSet<string> favs;
        private static string FavFile => Path.Combine(Paths.ConfigPath, "leo.idleslayer.slayertools.favorites.json");

        private HashSet<string> Favs()
        {
            if (favs != null) return favs;
            try { if (File.Exists(FavFile)) favs = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(FavFile)); } catch { }
            return favs ??= new HashSet<string>();
        }

        private void ToggleFav(string key, string name)
        {
            string id = key + ":" + name;
            if (!Favs().Remove(id)) Favs().Add(id);
            try { File.WriteAllText(FavFile, JsonSerializer.Serialize(Favs())); } catch { }
            SortRows(key);
            Toast((Favs().Contains(id) ? "Favorite added: " : "Favorite removed: ") + Pretty(name));
        }

        private bool IsFav(string key, string name) => Favs().Contains(key + ":" + name);

        // move favorites to the top of the list
        private void SortRows(string key)
        {
            if (!nmRows.TryGetValue(key, out var rows) || rows.Count == 0) return;
            var ordered = rows.Where(r => IsFav(key, r.prop.Name)).Concat(rows.Where(r => !IsFav(key, r.prop.Name))).ToList();
            for (int i = 0; i < ordered.Count; i++) if (ordered[i].go != null) ordered[i].go.transform.SetSiblingIndex(i);
        }

        // ---------------- 3) Friendly names ----------------
        private static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["coins"] = "Coins", ["lifetimeCoins"] = "Coins earned (lifetime)", ["ascensions"] = "Ascensions",
            ["coinsPerSecond"] = "Coins per second", ["coinsRecord"] = "Coin record",
            ["currentAscensionCoinsRecord"] = "Coin record (ascension)", ["currentAscensionEnemiesSlayed"] = "Enemies killed (ascension)",
            ["coinsPickedUp"] = "Coins picked up", ["rubiesPickedUp"] = "Rubies picked up", ["sapphiresPickedUp"] = "Sapphires picked up",
            ["emeraldsPickedUp"] = "Emeralds picked up", ["diamondsPickedUp"] = "Diamonds picked up", ["zyniumsPickedUp"] = "Zyniums picked up",
            ["cpsPercentagePerDay"] = "CpS bonus per day", ["soulsBonusMultiplier"] = "Souls multiplier",
            ["secondsToSlayEnemiesWhileIdle"] = "Seconds per idle kill", ["randomBoxes"] = "Boxes opened",
            ["specialRandomBoxChance"] = "Purple box chance", ["startedDate"] = "Start date", ["lastPlayedDate"] = "Last played",
            ["lastTimerUpdateDate"] = "Last timer update", ["daysPassedSinceStarted"] = "Days since start",
            ["daysPassedSinceStartedCapped"] = "Days since start (capped)", ["portals"] = "Portals taken", ["boostsAmount"] = "Boosts used",
            ["adsWatched"] = "Ads watched", ["rubyChance"] = "Ruby chance", ["sapphireChance"] = "Sapphire chance",
            ["emeraldChance"] = "Emerald chance", ["diamondChance"] = "Diamond chance", ["zyniumChance"] = "Zynium chance",
            ["coinPatternLevels"] = "Coin pattern levels", ["enemyPatternLevels"] = "Enemy pattern levels",
            ["enemiesSlayed"] = "Enemies killed", ["enemiesSlayedWhileIdle"] = "Enemies killed while idle", ["enemiesSlayedWithArrows"] = "Enemies killed with arrows",
            ["giantsSlayed"] = "Giants killed", ["boostMaxSpeedBonus"] = "Boost max speed bonus", ["randomBoxChanceBonus"] = "Box chance bonus",
            ["enemySpawnChanceBonus"] = "Enemy spawn bonus", ["coinSpawnChanceBonus"] = "Coin spawn bonus",
            ["achievementMultiplier"] = "Achievement multiplier", ["ascensionCpsMultiplier"] = "Ascension CpS multiplier",
            ["arrowShootingSpeed"] = "Arrow shooting speed", ["minionsQuests"] = "Minion missions", ["minionsTimeBonus"] = "Minion time bonus",
            ["gemstoneAdditionalChance"] = "Extra gemstone chance", ["gemBonusStageAdditionalChance"] = "Gem chance (Bonus Stage)",
            ["magnetPowerModifier"] = "Magnet power", ["bonusStageSectionsCompleted"] = "Bonus Stage sections completed",
            ["bonusStageCompleted"] = "Bonus Stages completed", ["criticalGatherChance"] = "Critical gather chance",
            ["criticalGatherYield"] = "Critical gather yield", ["divinityPoints"] = "Divinity points",
            ["decreaseRandomBoxCoinsChances"] = "Fewer coins in boxes", ["dropChance"] = "Drop chance",
            ["giantsDropChance"] = "Giant drop chance", ["swordDamage"] = "Sword damage",
            ["smallEnemiesBowDamage"] = "Bow damage (small enemies)", ["giantsBowDamage"] = "Bow damage (giants)",
            ["bossBowDamage"] = "Bow damage (bosses)", ["giantsChanceModifier"] = "Giant spawn chance",
            ["silverRandomBoxesAmount"] = "Silver boxes", ["equipmentCpSBonusModifier"] = "Equipment CpS bonus",
            ["ultraAscensions"] = "Ultra ascensions", ["ascensionUpgradesBought"] = "Ascension upgrades bought",
            ["giantsInstantKillChance"] = "Giant one-hit kill chance", ["offlineDropChance"] = "Offline drop chance",
            ["ignoreTemporaryBonusesForIdleRewards"] = "Ignore temporary bonuses (idle)", ["jewelsOfSoul"] = "Jewels of Soul",
            ["giantsIdleSlayChance"] = "Idle giant kill chance", ["rageModeSoulsMultiplier"] = "Rage souls multiplier",
            ["temporaryItemsCrafted"] = "Temporary items crafted", ["rageModeAmount"] = "Rages used", ["rageModeDamage"] = "Rage damage",
            ["additionalChestHuntMultiplier"] = "Extra chest hunt multiplier", ["dailiesPerDay"] = "Daily quests per day",
            ["weekliesPerWeek"] = "Weekly quests per week", ["dailyQuestsCompleted"] = "Daily quests completed",
            ["lootBoxesOpened"] = "Loot boxes opened", ["minionsExtraLevels"] = "Extra minion levels",
            ["offlineEarningsDoubled"] = "Offline earnings doubled", ["CPSBonusByEquipmentAmount"] = "CpS bonus per equipment",
            ["soulsBonusByEquipmentAmount"] = "Souls bonus per equipment", ["spiritBoostBonusStageChance"] = "Spirit Boost chance (Bonus Stage)",
            ["spiritBoosts"] = "Spirit Boosts", ["questProgressPerKill"] = "Quest progress per kill",
            ["doubleMaterialsChance"] = "Double materials chance", ["magnetStrength"] = "Magnet strength",
            ["dailyMinionBonusHoursToForward"] = "Minion hours forwarded", ["decreaseBoostCDWhenPickUpCoin"] = "Boost recharge per coin",
            ["additionalArmorySlots"] = "Extra armory slots", ["weaponsFound"] = "Weapons found", ["weaponsBroken"] = "Weapons broken",
            ["rageModeCoinsMultiplier"] = "Rage coins multiplier", ["chestHunts"] = "Chest hunts",
            ["perfectChestHunts"] = "Perfect chest hunts", ["mimicsSlayed"] = "Mimics killed", ["maxMultiplierFound"] = "Best multiplier found",
            ["weeklyQuestsCompleted"] = "Weekly quests completed", ["armoryOptionsModifier"] = "Armory options",
            ["armoryExcellentModifier"] = "Excellent weapon chance", ["mimicSlayChance"] = "Mimic kill chance", ["shinyChance"] = "Shiny chance",
            ["freeSilverRandomBoxChance"] = "Free silver box chance", ["increaseCoinValue"] = "Extra coin value",
            ["windDashKills"] = "Wind Dash kills", ["rouletteWins"] = "Roulette wins", ["rouletteLosses"] = "Roulette losses",
            ["slayerChipsSpent"] = "Chips spent", ["randomBoxQuestsProgress"] = "Box quest progress",
            ["collectablesQuestsProgress"] = "Collectable quest progress", ["minimumChestHuntMultipliers"] = "Min chest hunt multipliers",
            ["increaseChestHuntArmoryChestsChance"] = "Armory chests (chest hunt)", ["increaseBonusStageArmoryChestsChance"] = "Armory chests (Bonus Stage)",
            ["playerHeartsAmount"] = "Player hearts", ["armoryItemAscendingHeightsChance"] = "Weapon chance (Ascending Heights)",
            ["armoryItemGrappleRunChance"] = "Weapon chance (Grapple Run)", ["slayersMachineSpins"] = "Machine spins",
            ["slayersMachineWins"] = "Machine wins", ["minionsRewardMultiplierPerSpentSP"] = "Minion bonus per SP spent",
            ["innerPowerCap"] = "Inner Power cap", ["swordDamageToBosses"] = "Sword damage to bosses",
            ["rageModeDamageMultiplier"] = "Rage damage multiplier", ["goldenRandomBoxChance"] = "Golden box chance",
            ["maxCpSMultiplierFromRandomBox"] = "Max CpS multiplier from boxes", ["idleRewardsPreventSeconds"] = "Seconds without idle rewards",
            ["materialsRefined"] = "Materials refined", ["idleRewardsPending"] = "Idle rewards pending",
            ["points"] = "Slayer Points", ["lifetime"] = "Slayer Points (lifetime)", ["soulsLeft"] = "Souls left", ["pre"] = "Slayer Points (before)",
            ["maxSpeed"] = "Max speed", ["decreaseSpeed"] = "Decrease speed", ["boostSpeed"] = "Boost speed",
            ["currentBoostedSwingTime"] = "Boosted swing time", ["currentCd"] = "Cooldown left", ["cooldown"] = "Cooldown",
            ["duration"] = "Duration", ["currentDuration"] = "Duration left",
        };

        // "rubyChance" -> "Ruby Chance" when there is no display name
        private static string Humanize(string n)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < n.Length; i++)
            {
                char c = n[i];
                if (i == 0) { sb.Append(char.ToUpperInvariant(c)); continue; }
                if (char.IsUpper(c) && !char.IsUpper(n[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string Pretty(string name)
        {
            if (!FriendlyNames) return name;
            return DisplayNames.TryGetValue(name, out var f) ? f : Humanize(name);
        }

        private static bool Matches(string name, string q) =>
            name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || Pretty(name).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
            || (DisplayNames.TryGetValue(name, out var f) && f.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);

        // ---------------- 4) Quick buttons ----------------
        private static bool IsChance(string n) => n.IndexOf("chance", StringComparison.OrdinalIgnoreCase) >= 0;

        private void Quick(string key, EditableProp p, Func<object> target, double factor, double? absolute)
        {
            object t = target();
            if (!p.IsStatic && t == null) { Toast("Not in game yet"); return; }
            double cur = Convert.ToDouble(p.Prop.GetValue(p.IsStatic ? null : t));
            double v = absolute ?? (cur == 0 ? factor : cur * factor);
            ApplyTo(key, p, t, v.ToString("R", CultureInfo.InvariantCulture));
            ResetRates();
        }

        // ---------------- 6) Events: boxes, giant, key ----------------
        private string NotRunner()
        {
            try { if (!GameState.IsRunner()) return "Only available during the normal run"; } catch { }
            if (MapController.instance == null) return "Not in game yet";
            return null;
        }

        private void CloseGameMenu()
        {
            try { NmClose(); var ui = UIManager.instance; if (ui != null && ui.ShopVisible()) ui.ToggleShop(false, true); } catch { }
        }

        private void SpawnBox(RandomBox.Type type, string label)
        {
            var err = NotRunner(); if (err != null) { Toast(err); return; }
            
            MapController.instance.SpawnRandomBox(Vector2.zero, type, true);
            Toast(label + " incoming! (x" + (++spawnCount) + ")");
        }

        private int spawnCount;

        private void SpawnGiantNow()
        {
            var err = NotRunner(); if (err != null) { Toast(err); return; }
            
            MapController.instance.SpawnGiant(true);
            Toast("A giant is coming!");
        }

        private void SpawnKey()
        {
            var err = NotRunner(); if (err != null) { Toast(err); return; }
            
            MapController.instance.SpawnChestHuntKey(Vector2.zero);
            Toast("Chest hunt key incoming!");
        }

        // ---------------- 7) Ascension ----------------
        private string confirmKey; private float confirmUntil;

        private bool Confirm(string key)
        {
            if (confirmKey == key && Time.realtimeSinceStartup < confirmUntil) { confirmKey = null; return true; }
            confirmKey = key; confirmUntil = Time.realtimeSinceStartup + 5f;
            Toast("Click once more to confirm (5 s)");
            return false;
        }

        private string ConfirmText(string key, string label) => confirmKey == key && Time.realtimeSinceStartup < confirmUntil ? "CONFIRM: " + label + " ?" : label;

        private void DoAscend(bool ultra)
        {
            var am = AscensionManager.instance;
            if (am == null || PlayerInventory.instance == null) { Toast("Not in game yet"); return; }
            if (ultra && am.ultraAscendButton != null && !am.ultraAscendButton.activeSelf) { Toast("Ultra ascension not unlocked yet"); return; }
            if (!Confirm(ultra ? "ultra" : "asc")) return;
            BackupSave(ultra ? "before-ultra-ascension" : "before-ascension", true);
            CloseGameMenu();
            am.Ascend(ultra);
            ResetRates();
            Toast(ultra ? "Ultra ascension done!" : "Ascension done!");
        }

        private static string StripTags(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(); bool tag = false;
            foreach (var c in s) { if (c == '<') tag = true; else if (c == '>') tag = false; else if (!tag) sb.Append(c); }
            return sb.ToString().Replace("\r", "").Trim();
        }

        private string AscensionPreview()
        {
            try
            {
                var am = AscensionManager.instance;
                if (am == null) return "-";
                var t = StripTags(am.GetAscensionRewardsText());
                t = string.Join("  ", t.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0));
                return t.Length > 160 ? t.Substring(0, 160) + "..." : t;
            }
            catch { return "-"; }
        }

        // ---------------- 8) Bonus Stage ----------------
        private void LaunchBonus(Func<BaseMap> get, string label)
        {
            var err = NotRunner(); if (err != null) { Toast(err); return; }
            BaseMap map = null;
            try { map = get(); } catch { }
            if (map == null) { Toast(label + " not found"); return; }
            try { if (!map.IsAvailable()) { Toast(label + " not unlocked yet"); return; } } catch { }
            CloseGameMenu();
            MapController.instance.SpawnPortal(map, new Il2CppSystem.Nullable<Vector2>());
            Toast("Portal to " + label + " opened: jump in!");
        }

        private float nextSphereTick;
        private void AutoSpheresTick()
        {
            if (!AutoSpheres || Time.realtimeSinceStartup < nextSphereTick) return;
            nextSphereTick = Time.realtimeSinceStartup + 0.2f;
            try
            {
                if (!GameState.IsBonus()) return;
                var player = MapController.instance?.player;
                if (player == null) return;
                var pp = player.position;
                foreach (var s in UnityEngine.Object.FindObjectsOfType<BonusSphere>())
                {
                    if (s == null || !s.gameObject.activeInHierarchy) continue;
                    var sp = s.transform.position;
                    if (sp.x < pp.x - 3f || sp.x > pp.x + 40f) continue;
                    s.transform.position = new Vector3(pp.x, pp.y + 0.5f, sp.z);
                }
            }
            catch { }
        }

        // ---------------- 9) God mode (Harmony patches, see Plugin) ----------------
        internal static bool SkipPlayerHit() => !GodMode;
        internal static void BossDamage(BossMapController ctrl, ref float damage)
        {
            if (!OneShotBoss || ctrl == null) return;
            try { damage = Math.Max(damage, ctrl.currentBossHealth + 1f); } catch { }
        }

        // ---------------- 10) Minions ----------------
        private void MinionsFinish()
        {
            var mm = MinionManager.instance;
            if (mm == null) { Toast("Minions not found (not unlocked yet?)"); return; }
            mm.UpdateMinionsTime(1e9, true);
            Toast("Minion missions completed: go claim them!");
        }

        private void MinionsClaim() { var mm = MinionManager.instance; if (mm == null) { Toast("Minions not found"); return; } mm.ClaimAll(); Toast("Minion rewards claimed"); }
        private void MinionsSend() { var mm = MinionManager.instance; if (mm == null) { Toast("Minions not found"); return; } mm.SendAll(); Toast("Minions sent on missions"); }

        // ---------------- Chest hunt: reveal the mimics ----------------
        private class ChestMark { public GameObject label; public List<(UnityEngine.UI.Image img, Color col)> tints; }
        private readonly Dictionary<IntPtr, ChestMark> chestMarks = new Dictionary<IntPtr, ChestMark>();
        private float nextChestTick;

        private static string ChestLabel(ChestType t, out Color col)
        {
            col = new Color(1f, 0.85f, 0.3f);
            switch (t)
            {
                case ChestType.Mimic: col = Color.white; return "MIMIC";
                case ChestType.Saver: col = new Color(0.4f, 1f, 0.5f); return "Saver";
                case ChestType.Multiplier: return "Multi";
                case ChestType.MultiplierIncreaser: return "Multi +";
                case ChestType.DuplicateNextPick: return "Next x2";
                case ChestType.CPS: return "Coins";
                case ChestType.SlayerPoints: return "Souls";
                case ChestType.Both: return "Coins+Souls";
                case ChestType.ArmoryChest: col = new Color(0.6f, 0.8f, 1f); return "Weapon";
                case ChestType.PermanentUpgrade: col = new Color(0.9f, 0.5f, 1f); return "Upgrade";
                case ChestType.Special: col = new Color(0.9f, 0.5f, 1f); return "Special";
                default: return t.ToString();
            }
        }

        private void ChestTick()
        {
            if (Time.realtimeSinceStartup < nextChestTick) return;
            nextChestTick = Time.realtimeSinceStartup + 0.25f;
            try
            {
                bool hunt = false;
                try { hunt = GameState.IsChestHunt(); } catch { }
                var mgr = ChestHuntManager.instance;
                if (!hunt || mgr == null || mgr.chests == null || (!RevealMimics && !RevealAll)) { ClearChestMarks(); return; }

                var alive = new HashSet<IntPtr>();
                foreach (var chest in mgr.chests)
                {
                    if (chest == null) continue;
                    var obj = chest.chestObject;
                    if (obj == null) continue;
                    var id = obj.Pointer;
                    bool show = !chest.opened && (RevealAll || chest.type == ChestType.Mimic);
                    if (!show) continue;
                    alive.Add(id);
                    if (chestMarks.ContainsKey(id)) continue;
                    chestMarks[id] = MarkChest(obj, chest.type);
                }
                foreach (var id in chestMarks.Keys.Where(k => !alive.Contains(k)).ToList()) Unmark(id);
            }
            catch (Exception e) { Plugin.L.LogWarning("Chest hunt: " + e.Message); nextChestTick = Time.realtimeSinceStartup + 5f; }
        }

        private ChestMark MarkChest(ChestObject obj, ChestType type)
        {
            var mark = new ChestMark { tints = new List<(UnityEngine.UI.Image, Color)>() };
            string text = ChestLabel(type, out var col);
            try
            {
                if (type == ChestType.Mimic)
                    foreach (var img in obj.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                    {
                        if (img == null) continue;
                        mark.tints.Add((img, img.color));
                        img.color = new Color(1f, 0.35f, 0.35f, img.color.a);
                    }
                // anchor to the chest image (the largest one), not to the root object
                // which is arranged by a game layout: otherwise the text ends up next to the chest
                RectTransform anchor = obj.transform.TryCast<RectTransform>();
                float best = -1;
                foreach (var img in obj.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                {
                    if (img == null || !img.gameObject.activeInHierarchy) continue;
                    var r = img.rectTransform.rect; float a = r.width * r.height;
                    if (a > best) { best = a; anchor = img.rectTransform; }
                }
                var go = new GameObject("ST Chest Mark");
                var rt = go.AddComponent<RectTransform>();
                go.transform.SetParent(anchor != null ? anchor : obj.transform, false);
                go.layer = obj.gameObject.layer;
                var le = go.AddComponent<UnityEngine.UI.LayoutElement>();
                le.ignoreLayout = true;
                // the text sits on the chest, at its size: it no longer spills onto its neighbours
                rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = new Vector2(-2f, 0f); rt.offsetMax = new Vector2(2f, 0f);
                go.transform.SetAsLastSibling();
                var tmp = go.AddComponent<TMPro.TextMeshProUGUI>();
                if (obj.revealText != null) { tmp.font = obj.revealText.font; tmp.fontSharedMaterial = obj.revealText.fontSharedMaterial; }
                tmp.text = text; tmp.color = col; tmp.alignment = TMPro.TextAlignmentOptions.Center;
                tmp.enableWordWrapping = text.Contains(' ');
                tmp.overflowMode = TMPro.TextOverflowModes.Overflow;
                tmp.enableAutoSizing = true; tmp.fontSizeMin = 6; tmp.fontSizeMax = type == ChestType.Mimic ? 30 : 22;
                tmp.raycastTarget = false;
                tmp.outlineWidth = 0.3f; tmp.outlineColor = new Color32(0, 0, 0, 255);
                mark.label = go;
            }
            catch (Exception e) { Plugin.L.LogWarning("Chest mark: " + e.Message); }
            return mark;
        }

        private void Unmark(IntPtr id)
        {
            if (!chestMarks.TryGetValue(id, out var m)) return;
            chestMarks.Remove(id);
            try { if (m.label != null) UnityEngine.Object.Destroy(m.label); } catch { }
            foreach (var (img, col) in m.tints) try { if (img != null) img.color = col; } catch { }
        }

        private void ClearChestMarks()
        {
            if (chestMarks.Count == 0) return;
            foreach (var id in chestMarks.Keys.ToList()) Unmark(id);
        }

        private string ChestStatus()
        {
            try
            {
                var mgr = ChestHuntManager.instance;
                if (mgr == null || !GameState.IsChestHunt() || mgr.chests == null) return "No chest hunt in progress.";
                int mim = 0, left = 0;
                foreach (var c in mgr.chests) { if (c == null || c.opened) continue; left++; if (c.type == ChestType.Mimic) mim++; }
                return $"Chest hunt in progress: {left} closed chests, {mim} of them mimic(s).";
            }
            catch { return "No chest hunt in progress."; }
        }

        // ---------------- 11) Dashboard ----------------
        private readonly List<(float t, double coins, double sp, double kills)> samples = new List<(float, double, double, double)>();
        private float nextSample, sessionStart = -1;

        private void ResetRates() => samples.Clear();

        private void SampleTick()
        {
            var inv = PlayerInventory.instance;
            if (inv == null || Time.realtimeSinceStartup < nextSample) return;
            if (sessionStart < 0) sessionStart = Time.realtimeSinceStartup;
            nextSample = Time.realtimeSinceStartup + 1f;
            try
            {
                var s = (Time.realtimeSinceStartup, inv.coins, SlayerPoints.points, inv.enemiesSlayed);
                // a drop (purchase, ascension) skews the measurement: start over
                if (samples.Count > 0 && (s.Item2 < samples[samples.Count - 1].coins * 0.5)) samples.Clear();
                samples.Add(s);
                while (samples.Count > 0 && samples[0].t < Time.realtimeSinceStartup - 120f) samples.RemoveAt(0);
            }
            catch { }
        }

        private double PerMin(Func<(float t, double coins, double sp, double kills), double> f)
        {
            if (samples.Count < 3) return double.NaN;
            var a = samples[0]; var b = samples[samples.Count - 1];
            double dt = b.t - a.t; if (dt < 2) return double.NaN;
            return Math.Max(0, (f(b) - f(a)) / dt * 60.0);
        }

        private static string Rate(double v) => double.IsNaN(v) ? "measuring..." : Fmt(v) + "/min";

        private string DashText()
        {
            var inv = PlayerInventory.instance;
            if (inv == null) return "Waiting for the game...";
            double mins = sessionStart < 0 ? 0 : (Time.realtimeSinceStartup - sessionStart) / 60.0;
            var sb = new StringBuilder();
            sb.AppendLine($"Session: {(int)(mins / 60)} h {(int)(mins % 60):00} min      Speed x{Time.timeScale.ToString("0.#", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"Coins: {Fmt(inv.coins)}   ({Rate(PerMin(x => x.coins))}, CpS {Fmt(inv.coinsPerSecond)})");
            sb.AppendLine($"Slayer Points: {Fmt(SlayerPoints.points)}   ({Rate(PerMin(x => x.sp))})");
            sb.AppendLine($"Enemies killed: {Fmt(inv.enemiesSlayed)}   ({Rate(PerMin(x => x.kills))})");
            sb.AppendLine($"Drop chance {Num(inv.dropChance)}   Giants {Num(inv.giantsDropChance)}   Purple box {Num(inv.specialRandomBoxChance)}");
            sb.AppendLine($"Ruby {Num(inv.rubyChance)}  Sapphire {Num(inv.sapphireChance)}  Emerald {Num(inv.emeraldChance)}  Diamond {Num(inv.diamondChance)}  Zynium {Num(inv.zyniumChance)}");
            sb.AppendLine($"Ascensions {Fmt(inv.ascensions)}   Ultra {Fmt(inv.ultraAscensions)}   Chest hunts {Fmt(inv.chestHunts)} (perfect {Fmt(inv.perfectChestHunts)})");
            sb.Append("Next ascension: " + AscensionPreview());
            return sb.ToString();
        }

        private static string Num(double v) => Math.Abs(v) < 1000 ? v.ToString("0.##", CultureInfo.InvariantCulture) : Fmt(v);

        // ---------------- 12) Log ----------------
        private readonly List<string> journal = new List<string>();
        private static string JournalFile => Path.Combine(Paths.BepInExRootPath, "slayer_log.txt");

        private void Journal(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + msg;
            journal.Add(line);
            if (journal.Count > 200) journal.RemoveAt(0);
            try { File.AppendAllText(JournalFile, DateTime.Now.ToString("yyyy-MM-dd ") + line + Environment.NewLine); } catch { }
        }

        private string JournalText()
        {
            if (journal.Count == 0) return "Nothing yet during this session.";
            return string.Join("\n", journal.Skip(Math.Max(0, journal.Count - 22)).Reverse());
        }

        // ---------------- 13) Version check ----------------
        internal static string VersionWarning;
        private bool versionChecked;

        private void CheckVersion()
        {
            if (versionChecked) return;
            versionChecked = true;
            try
            {
                string v = Application.version;
                if (v != ExpectedGameVersion)
                {
                    VersionWarning = $"Warning: game version {v}, Slayer Tools was made for {ExpectedGameVersion}. Some features may no longer work.";
                    Plugin.L.LogWarning(VersionWarning);
                }
                else Plugin.L.LogInfo("Game version checked: " + v);
                var missing = new List<string>();
                void need(string what, Func<object> f) { try { if (f() == null) missing.Add(what); } catch { missing.Add(what); } }
                need("MapController", () => typeof(MapController).GetMethod("SpawnRandomBox", new[] { typeof(Vector2), typeof(RandomBox.Type), typeof(bool) }));
                need("ChestHuntManager", () => typeof(ChestHuntManager).GetProperty("chests"));
                need("AscensionManager", () => typeof(AscensionManager).GetMethod("Ascend"));
                need("MinionManager", () => typeof(MinionManager).GetMethod("UpdateMinionsTime"));
                if (missing.Count > 0)
                {
                    VersionWarning = (VersionWarning ?? "The game has changed. ") + " Not found: " + string.Join(", ", missing);
                    Plugin.L.LogWarning(VersionWarning);
                }
                if (VersionWarning != null) Journal(VersionWarning);
            }
            catch (Exception e) { Plugin.L.LogWarning("Version check: " + e.Message); }
        }

        // ---------------- 14) savedata.sav backups ----------------
        internal static string SaveDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low", "Pablo Leban", "Idle Slayer");
        internal static string BackupRoot => Path.Combine(Paths.BepInExRootPath, "slayer_backups");
        internal static string RestoreMarker => Path.Combine(BackupRoot, "RESTORE_PENDING.txt");
        private float lastBackup = -1000;

        private string BackupSave(string reason, bool force = false)
        {
            try
            {
                if (!force && Time.realtimeSinceStartup - lastBackup < 600) return null;   // at most one automatic backup every 10 min
                var src = Path.Combine(SaveDir, "savedata.sav");
                if (!File.Exists(src)) { Plugin.L.LogWarning("savedata.sav not found: " + src); return null; }
                var dir = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + reason);
                Directory.CreateDirectory(dir);
                File.Copy(src, Path.Combine(dir, "savedata.sav"), true);
                var bak = Path.Combine(SaveDir, "backup.sav");
                if (File.Exists(bak)) File.Copy(bak, Path.Combine(dir, "backup.sav"), true);
                lastBackup = Time.realtimeSinceStartup;
                // keep the 20 most recent ones
                var all = Directory.GetDirectories(BackupRoot).OrderByDescending(d => d).ToList();
                foreach (var old in all.Skip(20)) try { Directory.Delete(old, true); } catch { }
                Journal("Save copied: " + Path.GetFileName(dir));
                return dir;
            }
            catch (Exception e) { Plugin.L.LogWarning("Copying the save: " + e.Message); return null; }
        }

        private List<string> Backups()
        {
            try { return Directory.Exists(BackupRoot) ? Directory.GetDirectories(BackupRoot).OrderByDescending(d => d).ToList() : new List<string>(); }
            catch { return new List<string>(); }
        }

        private int restoreIndex;
        private string RestoreLabel()
        {
            var b = Backups();
            if (b.Count == 0) return "Restore: no backup";
            restoreIndex = Math.Min(restoreIndex, b.Count - 1);
            if (File.Exists(RestoreMarker)) return "Restore scheduled for next launch (cancel)";
            return ConfirmText("restore", "Restore: " + Path.GetFileName(b[restoreIndex]));
        }

        private void RestoreClick()
        {
            if (File.Exists(RestoreMarker)) { File.Delete(RestoreMarker); Toast("Restore cancelled"); return; }
            var b = Backups();
            if (b.Count == 0) { Toast("No save backup"); return; }
            if (!Confirm("restore")) return;
            Directory.CreateDirectory(BackupRoot);
            File.WriteAllText(RestoreMarker, b[restoreIndex]);
            Toast("OK: close the game and start it again, the backup will be put back on startup.");
        }

        // called in Plugin.Load, before the game reads the save
        internal static void ApplyPendingRestore()
        {
            try
            {
                if (!File.Exists(RestoreMarker)) return;
                var dir = File.ReadAllText(RestoreMarker).Trim();
                File.Delete(RestoreMarker);
                var src = Path.Combine(dir, "savedata.sav");
                if (!File.Exists(src)) { Plugin.L.LogWarning("Backup to restore not found: " + dir); return; }
                // keep the current version anyway, just in case
                var cur = Path.Combine(SaveDir, "savedata.sav");
                if (File.Exists(cur))
                {
                    var keep = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_before-restore");
                    Directory.CreateDirectory(keep);
                    File.Copy(cur, Path.Combine(keep, "savedata.sav"), true);
                }
                File.Copy(src, cur, true);
                File.Copy(src, Path.Combine(SaveDir, "backup.sav"), true);
                Plugin.L.LogInfo("Save restored from " + dir);
            }
            catch (Exception e) { Plugin.L.LogError("Restore failed: " + e.Message); }
        }

        // ---------------- menu pages ----------------
        private void NmBuildFeaturePages()
        {
            // Events
            var ev = NmNewPage("events");
            NmButton(ev.transform, () => RecentToast() ?? "Spawn whatever you want during the run (the menu stays open: spam it!).", "news", null, ev, false).Let(g => Size(g, 54));
            var eg = NmGrid(ev.transform, "grid").transform;
            NmButton(eg, () => "< Back", "Up Arrow", () => NmShow("home"), ev);
            NmButton(eg, () => "Purple box", "Star", () => SpawnBox(RandomBox.Type.Special, "Purple box"), ev);
            NmButton(eg, () => "Silver box", "Star Silver", () => SpawnBox(RandomBox.Type.Silver, "Silver box"), ev);
            NmButton(eg, () => "Golden box", "Star", () => SpawnBox(RandomBox.Type.Gold, "Golden box"), ev);
            NmButton(eg, () => "Normal box", "Star Silver", () => SpawnBox(RandomBox.Type.Normal, "Box"), ev);
            NmButton(eg, () => "Giant", "Face", SpawnGiantNow, ev);
            NmButton(eg, () => "Chest hunt key", "Checklist", SpawnKey, ev);
            NmButton(eg, () => "Bonus Stage (yours)", "Up Arrow", () => LaunchBonus(() => MapController.instance.CurrentBonusMap(), "Bonus Stage"), ev);
            NmButton(eg, () => "Bonus Stage 1", "Up Arrow", () => LaunchBonus(() => Maps.list.BonusStage, "Bonus Stage 1"), ev);
            NmButton(eg, () => "Bonus Stage 2", "Up Arrow", () => LaunchBonus(() => Maps.list.BonusStage2, "Bonus Stage 2"), ev);
            NmButton(eg, () => "Bonus Stage 3", "Up Arrow", () => LaunchBonus(() => Maps.list.BonusStage3, "Bonus Stage 3"), ev);
            NmButton(eg, () => ConfirmText("asc", "Ascension"), "Face", () => DoAscend(false), ev);
            NmButton(eg, () => ConfirmText("ultra", "Ultra ascension"), "Face", () => DoAscend(true), ev);
            NmButton(eg, () => "Minions: finish missions", "Checklist", MinionsFinish, ev);
            NmButton(eg, () => "Minions: claim all", "Star", MinionsClaim, ev);
            NmButton(eg, () => "Minions: send all", "Up Arrow", MinionsSend, ev);
            NmButton(ev.transform, () => "Next ascension: " + AscensionPreview(), "news", null, ev, false).Let(g => Size(g, 70));

            NmBuildBag();
            NmBuildDivinities();

            // Combat and chest hunt
            var cb = NmNewPage("combat");
            NmButton(cb.transform, () => RecentToast() ?? ChestStatus(), "news", null, cb, false).Let(g => Size(g, 54));
            var cg = NmGrid(cb.transform, "grid").transform;
            NmButton(cg, () => "< Back", "Up Arrow", () => NmShow("home"), cb);
            NmButton(cg, () => "Show mimics: " + OnOff(RevealMimics), "Face", () => { RevealMimics = !RevealMimics; ClearChestMarks(); SaveOptions(); }, cb);
            NmButton(cg, () => "Show all chests: " + OnOff(RevealAll), "Checklist", () => { RevealAll = !RevealAll; ClearChestMarks(); SaveOptions(); }, cb);
            NmButton(cg, () => "God mode (bosses): " + OnOff(GodMode), "Star", () => { GodMode = !GodMode; SaveOptions(); }, cb);
            NmButton(cg, () => "One-hit bosses: " + OnOff(OneShotBoss), "Star", () => { OneShotBoss = !OneShotBoss; SaveOptions(); }, cb);
            NmButton(cg, () => "Auto bonus spheres: " + OnOff(AutoSpheres), "Up Arrow", () => { AutoSpheres = !AutoSpheres; SaveOptions(); }, cb);
            NmButton(cb.transform, () =>
                "Chest hunt: mimics are shown in red with MIMIC above them.\n" +
                "God mode: you no longer lose lives against bosses (nor on Bonus Stage saws).\n" +
                "Auto bonus spheres: spheres come to you by themselves in the Bonus Stage.",
                "news", null, cb, false).Let(g => Size(g, 110));

            // Dashboard
            var db = NmNewPage("dash");
            var dg = NmGrid(db.transform, "grid").transform;
            NmButton(dg, () => "< Back", "Up Arrow", () => NmShow("home"), db);
            NmButton(dg, () => "Reset the measurement", "drops_34", () => { ResetRates(); sessionStart = Time.realtimeSinceStartup; Toast("Measurement reset"); }, db);
            NmButton(db.transform, DashText, "news", null, db, false).Let(g => Size(g, 300));

            // Log
            var jr = NmNewPage("log");
            var jg = NmGrid(jr.transform, "grid").transform;
            NmButton(jg, () => "< Back", "Up Arrow", () => NmShow("home"), jr);
            NmButton(jg, () => "Clear the log", "drops_34", () => { journal.Clear(); }, jr);
            NmButton(jr.transform, () => "Everything is also written to BepInEx\\slayer_log.txt", "news", null, jr, false).Let(g => Size(g, 46));
            NmButton(jr.transform, JournalText, "news", null, jr, false).Let(g => Size(g, 520));
        }

        private void NmBuildSaveSettings(Transform grid, GameObject page)
        {
            NmButton(grid, () => "Copy the save now", "Checklist", () =>
            {
                var d = BackupSave("manual", true);
                Toast(d != null ? "Save copied: " + Path.GetFileName(d) : "Copy failed (see the log file)");
            }, page);
            NmButton(grid, () => "Previous / next backup", "Up Arrow", () =>
            {
                var b = Backups(); if (b.Count == 0) return;
                restoreIndex = (restoreIndex + 1) % b.Count;
            }, page);
            NmButton(grid, RestoreLabel, "drops_34", RestoreClick, page);
            NmButton(grid, () => "Names: " + (FriendlyNames ? "friendly" : "code"), "book_11", () => { FriendlyNames = !FriendlyNames; SaveOptions(); }, page);
        }

        // ---------------- persistent options ----------------
        private static string OptFile => Path.Combine(Paths.ConfigPath, "leo.idleslayer.slayertools.options.json");

        private static void LoadOptions()
        {
            try
            {
                if (!File.Exists(OptFile)) return;
                var d = JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(OptFile));
                if (d == null) return;
                bool g(string k, bool def) => d.TryGetValue(k, out var v) ? v : def;
                GodMode = g("GodMode", false); OneShotBoss = g("OneShotBoss", false); AutoSpheres = g("AutoSpheres", false);
                RevealMimics = g("RevealMimics", true); RevealAll = g("RevealAll", false); FriendlyNames = g("FriendlyNames", true);
            }
            catch { }
        }

        private void SaveOptions()
        {
            try
            {
                File.WriteAllText(OptFile, JsonSerializer.Serialize(new Dictionary<string, bool>
                {
                    ["GodMode"] = GodMode, ["OneShotBoss"] = OneShotBoss, ["AutoSpheres"] = AutoSpheres,
                    ["RevealMimics"] = RevealMimics, ["RevealAll"] = RevealAll, ["FriendlyNames"] = FriendlyNames,
                }));
            }
            catch { }
        }

        // called every frame by Update()
        private bool firstBackupDone;
        private void FeaturesTick()
        {
            if (PlayerInventory.instance != null)
            {
                CheckVersion();
                if (!firstBackupDone) { firstBackupDone = true; BackupSave("session-start", true); }
            }
            SampleTick();
            ChestTick();
            AutoSpheresTick();
        }
    }
}
