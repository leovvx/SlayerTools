using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace SlayerTools
{
    // Bag: add resources (enemy drops) with their real icons
    public partial class ToolsBehaviour
    {
        private TMPro.TMP_InputField nmBagSearch;
        private readonly List<(GameObject go, Drop d)> nmBagRows = new List<(GameObject, Drop)>();
        private string nmBagLastFilter;
        internal static bool BagOverLimit;

        private static string DName(Drop d) => (!string.IsNullOrEmpty(d.localizedName) ? d.localizedName : d.name) ?? "?";

        private static double DMax(Drop d) { try { return d.GetMaxAmount(); } catch { return d.maxAmount; } }

        private string DLabel(Drop d)
        {
            double max = DMax(d);
            string lim = max > 0 && max < 1e300 ? " / " + Fmt(max) : "";
            return DName(d) + "   " + Fmt(d.amount) + lim + (d.locked ? "   <color=#999999>(not found yet)</color>" : "");
        }

        private void BagRefreshGame(Drop d)
        {
            try { DropsManager.instance?.CheckShow(d); } catch { }
            try { BagManager.instance?.UpdateBagAmount(); } catch { }
        }

        private void BagAdd(Drop d, double qty)
        {
            if (PlayerInventory.instance == null) { Toast("Not in game yet"); return; }
            double old = d.amount;
            var dm = DropsManager.instance;
            double got;
            if (dm != null) got = dm.AddDrop(qty, d, false, false, false, true, BagOverLimit, false);
            else got = d.AddDrop(qty, BagOverLimit);
            if (d.amount == old && qty > 0)
            {
                // the game refused (bag full): force it if "exceed the limit" is on
                if (BagOverLimit) d.amount = old + qty;
                else { Toast(DName(d) + ": bag full (" + Fmt(DMax(d)) + ")  -> turn on \"Exceed the limit\""); return; }
            }
            PushUndo(DName(d) + " +" + Fmt(qty), () => { d.amount = old; BagRefreshGame(d); });
            BagRefreshGame(d);
            Toast(DName(d) + " : " + Fmt(d.amount));
        }

        private void BagSet(Drop d, double v)
        {
            if (PlayerInventory.instance == null) { Toast("Not in game yet"); return; }
            double old = d.amount;
            double max = DMax(d);
            if (!BagOverLimit && max > 0 && v > max) v = max;
            d.amount = Math.Max(0, v);
            PushUndo(DName(d) + " = " + Fmt(d.amount), () => { d.amount = old; BagRefreshGame(d); });
            BagRefreshGame(d);
            Toast(DName(d) + " = " + Fmt(d.amount));
        }

        private void NmBuildBag()
        {
            var page = NmNewPage("bag");
            var top = NmGrid(page.transform, "grid").transform;
            NmButton(top, () => "< Back", "Up Arrow", () => NmShow("home"), page);
            NmButton(top, UndoLabel, "Up Arrow", DoUndo, page);
            NmButton(top, () => "Exceed the limit: " + OnOff(BagOverLimit), "drops_34", () => BagOverLimit = !BagOverLimit, page);
            NmButton(top, () => "Fill everything", "Star", () =>
            {
                var inv = PlayerInventory.instance; if (inv?.drops == null) return;
                var olds = new List<(Drop, double)>();
                int n = 0;
                foreach (var d in inv.drops)
                {
                    if (d == null || d.locked) continue;
                    double max = DMax(d); if (max <= 0 || max > 1e15 || d.amount >= max) continue;
                    olds.Add((d, d.amount)); d.amount = max; BagRefreshGame(d); n++;
                }
                PushUndo("fill everything", () => { foreach (var (d, a) in olds) { d.amount = a; BagRefreshGame(d); } });
                Toast(n + " resource(s) filled to the max");
            }, page);
            NmButton(page.transform, () => RecentToast() ?? "+10 / +100 / +1000, or type a number then Enter.  Full = bag maximum.", "news", null, page, false).Let(g => Size(g, 54));

            if (nmInputTemplate != null)
            {
                var searchRow = NmBox(page.transform, "search", false, 10, 54);
                NmButton(searchRow.transform, () => "Search", "news", null, page, false).Let(g => Size(g, 54, 150, 0));
                nmBagSearch = NmInput(searchRow.transform, "e.g. egg, scale, key...");
            }

            nmBagList = NmBox(page.transform, "list", true, 6);
        }

        private GameObject nmBagList;

        private void NmFillBag()
        {
            if (nmBagList == null || nmBagRows.Count > 0) return;
            var page = nmPages["bag"];
            var list = nmBagList;
            var inv0 = PlayerInventory.instance;
            if (inv0?.drops == null) { Toast("Resources not loaded yet, try again in game"); return; }
            var drops = inv0.drops.Where(d => d != null)
                .OrderBy(d => d.locked ? 1 : 0)
                .ThenBy(d => { try { return d.category != null ? d.category.sortingOrder : 999; } catch { return 999; } })
                .ThenBy(d => DName(d), StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var drop in drops)
            {
                var d = drop;
                var row = NmBox(list.transform, "drop " + d.name, false, 8, 56);
                var nameBtn = NmButton(row.transform, () => DLabel(d), "drops_34", null, page, true);
                Size(nameBtn, 56, -1, 1);
                try
                {
                    var img = nameBtn.transform.Find("Background/Content/Image")?.GetComponent<UnityEngine.UI.Image>();
                    if (img != null && d.icon != null) { img.sprite = d.icon; img.preserveAspect = true; img.color = d.locked ? new Color(1, 1, 1, 0.4f) : Color.white; }
                }
                catch { }
                NmButton(row.transform, () => "+10", "news", () => BagAdd(d, 10), page, false).Let(g => Size(g, 56, 80, 0));
                NmButton(row.transform, () => "+100", "news", () => BagAdd(d, 100), page, false).Let(g => Size(g, 56, 90, 0));
                NmButton(row.transform, () => "+1000", "news", () => BagAdd(d, 1000), page, false).Let(g => Size(g, 56, 100, 0));
                if (nmInputTemplate != null)
                {
                    var input = NmInput(row.transform, "number");
                    Size(input.gameObject, 56, 150, 0);
                    input.onSubmit.AddListener((Action<string>)(txt =>
                    {
                        if (!TryParseNumber(txt, out double v)) { Toast("Invalid number"); return; }
                        BagSet(d, v); input.text = ""; NmRefresh(true);
                    }));
                    nmBagInputs.Add(input);
                }
                NmButton(row.transform, () => "Full", "Star", () =>
                {
                    double max = DMax(d);
                    if (max <= 0 || max > 1e15) { Toast(DName(d) + ": no known limit"); return; }
                    BagSet(d, max);
                }, page, false).Let(g => Size(g, 56, 90, 0));
                nmBagRows.Add((row, d));
            }
        }

        private readonly List<TMPro.TMP_InputField> nmBagInputs = new List<TMPro.TMP_InputField>();

        private bool BagTyping()
        {
            if (nmBagSearch != null && nmBagSearch.isFocused) return true;
            foreach (var i in nmBagInputs) if (i != null && i.isFocused) return true;
            return false;
        }

        private void NmFilterBag()
        {
            if (nmBagSearch == null) return;
            string q = (nmBagSearch.text ?? "").Trim();
            if (q == nmBagLastFilter) return;
            nmBagLastFilter = q;
            foreach (var (go, d) in nmBagRows)
                if (go != null) go.SetActive(q.Length == 0 || DName(d).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || (d.name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void ResetBagUi() { nmBagList = null; nmBagRows.Clear(); nmBagInputs.Clear(); nmBagSearch = null; nmBagLastFilter = null; }
    }
}
