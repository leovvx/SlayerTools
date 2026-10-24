using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlayerTools
{
    // Divinities: divinity points + activate / deactivate any divinity
    public partial class ToolsBehaviour
    {

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
            return $"Points: {Col(CGold, double.IsNaN(left) ? "?" : Fmt(left))} available  /  {Fmt(inv.divinityPoints)} total\n" +
                   $"Active divinities: {Col(CGreen, on.ToString())} / {total}";
        }

        private void AddDivPoints(double n)
        {
            var inv = PlayerInventory.instance;
            if (inv == null) { Toast("Not in game yet"); return; }
            SetProp("inv", "divinityPoints", (inv.divinityPoints + n).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            Toast("+" + Fmt(n) + " divinity points (" + Fmt(DivLeft()) + " available)");
        }

        private void ActivateDivQuiet(Divinity d)
        {
            var saved = undoBatch; undoBatch = new List<Action>();   // swallow nested undo entries
            try { ActivateDiv(d); } finally { undoBatch = saved; }
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
                Recalculate();
            }
            var dd = d;
            PushUndo("divinity " + DvName(d), () => { try { DivinitiesManager.instance?.DeactivateDivinity(dd.id); } catch { } if (dd.unlocked) { dd.unlocked = false; try { dd.SaveData(); } catch { } Recalculate(); } });
            Toast(DvName(d) + " activated!");
        }

        private void DeactivateDiv(Divinity d)
        {
            var dm = DivinitiesManager.instance;
            if (!d.unlocked) { Toast(DvName(d) + " is not active"); return; }
            try { dm?.DeactivateDivinity(d.id); } catch (Exception e) { Plugin.L.LogWarning("DeactivateDivinity : " + e.Message); }
            if (d.unlocked) { d.unlocked = false; try { d.SaveData(); d.divinityObjectComponent?.RefreshColors(); } catch { } Recalculate(); }
            var dd = d;
            PushUndo("deactivate " + DvName(d), () => { if (!dd.unlocked) ActivateDivQuiet(dd); });
            Toast(DvName(d) + " deactivated (points refunded)");
        }

    }
}
