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
            if (qty < 0) { BagSet(d, d.amount + qty); return; }
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
            Toast(DName(d) + ": " + Fmt(d.amount));
        }

        private void BagFillAll()
        {
            var inv = PlayerInventory.instance; if (inv?.drops == null) { Toast("Not in game yet"); return; }
            var olds = new List<(Drop, double)>();
            foreach (var d in inv.drops)
            {
                if (d == null || d.locked) continue;
                double max = DMax(d); if (max <= 0 || max > 1e15 || d.amount >= max) continue;
                olds.Add((d, d.amount)); d.amount = max; BagRefreshGame(d);
            }
            if (olds.Count == 0) { Toast("Nothing to fill: every found resource is already full"); return; }
            PushUndo("fill everything", () => { foreach (var (d, a) in olds) { d.amount = a; BagRefreshGame(d); } });
            Toast(olds.Count + " resource(s) filled to the max");
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

    }
}
