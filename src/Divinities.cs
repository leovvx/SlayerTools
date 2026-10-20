using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlayerTools
{
    // Divinities: divinity points + activate / deactivate any divinity
    public partial class ToolsBehaviour
    {
        private GameObject nmDivList;
        private TMPro.TMP_InputField nmDivInput;
        private readonly List<GameObject> nmDivRows = new List<GameObject>();

        private static string DvName(Divinity d) => (!string.IsNullOrEmpty(d.localizedName) ? d.localizedName : d.name) ?? "?";

        private static double DivLeft()
        {
            try { var dm = DivinitiesManager.instance; if (dm != null) return dm.CalculateDivinityPointsLeft(); } catch { }
            return double.NaN;
        }

        private string DivHeader()
        {
            var inv = PlayerInventory.instance;
            if (inv == null) return "Waiting for the game...";
            double left = DivLeft();
            int on = 0, total = 0;
            if (inv.divinities != null) foreach (var d in inv.divinities) if (d != null) { total++; if (d.unlocked) on++; }
            return $"Divinity points: {(double.IsNaN(left) ? "?" : Fmt(left))} available  /  {Fmt(inv.divinityPoints)} total\n" +
                   $"Active divinities: {on} / {total}";
        }

        private void AddDivPoints(double n)
        {
            var inv = PlayerInventory.instance;
            if (inv == null) { Toast("Not in game yet"); return; }
            SetProp("inv", "divinityPoints", (inv.divinityPoints + n).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            Toast("+" + Fmt(n) + " divinity points (total " + Fmt(inv.divinityPoints) + ")");
        }

        private void ActivateDiv(Divinity d)
        {
            var inv = PlayerInventory.instance; var dm = DivinitiesManager.instance;
            if (inv == null || dm == null) { Toast("Divinities not loaded yet (open the game's Divinities panel once)"); return; }
            if (d.unlocked) { Toast(DvName(d) + " is already active"); return; }
            double left = DivLeft();
            if (!double.IsNaN(left) && left < d.cost)
            {
                double missing = Math.Ceiling(d.cost - left);
                SetProp("inv", "divinityPoints", (inv.divinityPoints + missing).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            try { dm.ActivateDivinity(d.id); } catch (Exception e) { Plugin.L.LogWarning("ActivateDivinity : " + e.Message); }
            if (!d.unlocked)
            {
                // the game refused (boss or upgrade required): activate it anyway
                d.unlocked = true;
                try { d.SaveData(); } catch { }
                try { d.divinityObjectComponent?.RefreshColors(); } catch { }
                try { inv.CalculateValues(); } catch { }
            }
            var dd = d;
            PushUndo("divinity " + DvName(d), () => { try { DivinitiesManager.instance?.DeactivateDivinity(dd.id); } catch { } if (dd.unlocked) { dd.unlocked = false; try { dd.SaveData(); PlayerInventory.instance?.CalculateValues(); } catch { } } });
            Toast(DvName(d) + " activated!");
        }

        private void DeactivateDiv(Divinity d)
        {
            var dm = DivinitiesManager.instance;
            if (!d.unlocked) { Toast(DvName(d) + " is not active"); return; }
            try { dm?.DeactivateDivinity(d.id); } catch (Exception e) { Plugin.L.LogWarning("DeactivateDivinity : " + e.Message); }
            if (d.unlocked) { d.unlocked = false; try { d.SaveData(); d.divinityObjectComponent?.RefreshColors(); PlayerInventory.instance?.CalculateValues(); } catch { } }
            Toast(DvName(d) + " deactivated (points refunded)");
        }

        private void NmBuildDivinities()
        {
            var page = NmNewPage("divin");
            var top = NmGrid(page.transform, "grid").transform;
            NmButton(top, () => "< Back", "Up Arrow", () => NmShow("home"), page);
            NmButton(top, UndoLabel, "Up Arrow", DoUndo, page);
            NmButton(top, () => "+1 point", "Star", () => AddDivPoints(1), page);
            NmButton(top, () => "+10 points", "Star", () => AddDivPoints(10), page);
            NmButton(top, () => "+100 points", "Star Silver", () => AddDivPoints(100), page);
            NmButton(top, () => "Activate all", "Face", () =>
            {
                var inv = PlayerInventory.instance; if (inv?.divinities == null) return;
                int n = 0;
                foreach (var d in inv.divinities.ToArray()) if (d != null && !d.unlocked) { ActivateDiv(d); n++; }
                Toast(n + " divinit(y/ies) activated");
            }, page);
            NmButton(top, () => "Deactivate all", "drops_34", () =>
            {
                var inv = PlayerInventory.instance; if (inv?.divinities == null) return;
                int n = 0;
                foreach (var d in inv.divinities.ToArray()) if (d != null && d.unlocked) { DeactivateDiv(d); n++; }
                Toast(n + " divinit(y/ies) deactivated, points refunded");
            }, page);
            NmButton(page.transform, () => RecentToast() ?? DivHeader(), "news", null, page, false).Let(g => Size(g, 70));
            if (nmInputTemplate != null)
            {
                var row = NmBox(page.transform, "set", false, 10, 54);
                NmButton(row.transform, () => "Total points", "news", null, page, false).Let(g => Size(g, 54, 220, 0));
                nmDivInput = NmInput(row.transform, "total number (Enter)");
                nmDivInput.onSubmit.AddListener((Action<string>)(txt =>
                {
                    if (!TryParseNumber(txt, out double v)) { Toast("Invalid number"); return; }
                    SetProp("inv", "divinityPoints", v.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    nmDivInput.text = "";
                }));
            }
            NmButton(page.transform, () => "If a divinity costs more points than you have, the missing points are added.", "news", null, page, false).Let(g => Size(g, 46));
            nmDivList = NmBox(page.transform, "list", true, 6);
        }

        private void NmFillDivinities()
        {
            if (nmDivList == null || nmDivRows.Count > 0) return;
            var page = nmPages["divin"];
            var inv = PlayerInventory.instance;
            if (inv?.divinities == null) { Toast("Divinities not loaded yet, try again in game"); return; }
            var list = inv.divinities.Where(d => d != null).OrderBy(d => d.isDark ? 1 : 0).ThenBy(d => d.cost).ThenBy(d => DvName(d)).ToList();
            foreach (var div in list)
            {
                var d = div;
                var row = NmBox(nmDivList.transform, "div " + d.name, false, 8, 56);
                var nameBtn = NmButton(row.transform, () =>
                    (d.unlocked ? "<color=#7CFF8A>ACTIVE</color>  " : "") + DvName(d) + (d.isDark ? "  <color=#B08CFF>(dark)</color>" : "") + "   " + Fmt(d.cost) + " pts",
                    "Star", null, page, true);
                Size(nameBtn, 56, -1, 1);
                try
                {
                    var img = nameBtn.transform.Find("Background/Content/Image")?.GetComponent<UnityEngine.UI.Image>();
                    if (img != null && d.icon != null) { img.sprite = d.icon; img.preserveAspect = true; }
                }
                catch { }
                NmButton(row.transform, () => d.unlocked ? "Deactivate" : "Activate", d.unlocked ? "drops_34" : "Star",
                    () => { if (d.unlocked) DeactivateDiv(d); else ActivateDiv(d); }, page, false).Let(g => Size(g, 56, 170, 0));
                nmDivRows.Add(row);
            }
        }

        private bool DivTyping() => nmDivInput != null && nmDivInput.isFocused;
        private void ResetDivUi() { nmDivList = null; nmDivRows.Clear(); nmDivInput = null; }
    }
}
