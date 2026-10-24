# User guide

## Opening the menu

- **F2** from anywhere in the game (configurable),
- **the star tab** in the bottom bar of the game menu (between *Info* and the close button),
- **the star button** at the top left, next to Ascension and the Bag (opens / closes).

The menu **reopens where you left it**: same page, same scroll position, same searches and filters (Settings → *Reopen where I left it* to change this). After an update it shows **What's new** once.

Switching to another game tab or closing the game menu closes Slayer Tools. While you type in a box, the game receives no keys (no accidental jump / boost / rage).

## Every page looks the same

```
[< Back]        Edit  >  Inventory              [ Home ]
Ruby chance = 100                               [Undo 3]
```

- **Back** goes to the previous page (the mouse back button does too); on Home it closes the menu.
- **Home** jumps to the Home page (on Home, this is **Settings**).
- The **status bar** shows the last message for 4 seconds — green = done, orange = warning or "click again to confirm", red = error — and otherwise a short hint about the page.
- **Undo** reverts the last change (10 steps), from any page. Greyed out when there is nothing to undo.
- Buttons that cannot work right now are **greyed out** (e.g. spawns outside the normal run).
- **Dangerous actions** (ascension, restore, unpin all, deactivate all, delete preset, forget memory) need a **second click within 4 seconds**; the label turns orange meanwhile.

## Lists

Inventory, Slayer Points, Boost, Bag, Divinities, Quests, Enemies, Presets and Search results are **paged lists**:

- a **search box** (filters as you type),
- a **Show:** button that cycles filters (Favorites, Pinned, Found, Active, Daily…),
- **< Prev / Next >** with the page number and item count.

Search, filter and page are remembered per list. Rows per page: Settings → *Rows per page* (6 to 15).

## Pages

### Home
- Summary: coins, CpS, Slayer Points, souls.
- **Active:** everything currently switched on (god mode, freezes, coin lock, speed…) with **All off**.
- **Search** across stats, resources, divinities, enemies and pages.
- **Recent**: your last 3 pages.
- Tiles grouped in **Play**, **Edit** and **Tools**, each with a live badge ("12 pinned", "3 open", "run only"…).

### Search
Results from every page, filterable (Stats, Bag, Divinities, Enemies, Pages). Clicking a result opens its page with the search already filled in.

### Actions
| Section | Content |
|---|---|
| Coins | +1 hour / +1 day / +1 week of production, Coins x2, **Lock coins** switch |
| Slayer Points | +10%, +50%, x2 |
| Cooldowns | Portal / Rage / Boost ready now (with the seconds left), and *always ready* switches |
| Game speed | x1, x2, x5, x10, x20 (current one in gold) |

### Events
The menu **stays open**: spam the buttons. Everything here only works **during the normal run** (otherwise the tiles are greyed out).

- **Spawn**: purple, silver, golden and normal boxes, giant, chest hunt key.
- **Bonus Stage**: yours, 1, 2 or 3 — opens a portal and closes the menu so you can jump in.
- **Ascension**: preview of the rewards, Ascension / Ultra ascension (confirm; the save is backed up first).
- **Minions**: finish missions, claim all, send all.

### Combat & chests
Live chest hunt status, then switches (remembered between sessions):

| Switch | Effect |
|---|---|
| Show mimics | mimics turn **red** with "MIMIC" written on them |
| Show every chest | labels each chest with its content (Multi, Saver, Souls, Coins, Weapon…) |
| God mode | no lives lost against bosses or on Bonus Stage saws |
| One-hit bosses | every hit kills the boss |
| Auto bonus spheres | spheres fly to you by themselves in the Bonus Stage |

### Inventory · Slayer Points · Boost
- Type a value in the box, then **Enter**. Numbers: `1500`, `2.5M`, `3B`, `1.2T`, `4Qa`, `1e30`…
- **Click a name** to add it to your favorites (`*`, always listed first; filter *Favorites*).
- **Max** (chance stats only) sets 100. Yes / no stats show an **ON / OFF** button.
- Edited inventory stats are **pinned** (shown in blue): the game no longer overwrites them when it recalculates, even after a restart. **Unpin** gives the stat back to the game; **Unpin all** (top of the page) does it for every stat. Both can be undone.
- **Read-only** stats (Jewels of Soul, purchases…) show their value but cannot be edited — see [LIMITATIONS.md](LIMITATIONS.md).

### Bag
- **Step** (1, 10, 100, 1K, 10K, 1M) used by the **−** and **+** buttons of every row.
- A box per row to set an exact amount (Enter), and **Full** to fill to the bag maximum.
- **Over limit** switch to go past the bag size; **Fill everything** fills every found resource (one undo step).
- Filters: Found, Not found (greyed icons), Not full.

### Divinities
- Points available / total and active count.
- +1 / +10 / +100 / +1000 points.
- **Activate** / **Deactivate** each divinity (dark ones included). Missing points are added automatically; deactivating refunds them.
- **Activate all** (one undo step), **Deactivate all** (confirm).
- Filters: Active, Inactive, Dark.

### Quests
**Complete all (n)**, or one quest at a time, then claim the reward in the game. Filters: Quests, Daily, Weekly, To claim.

### Enemies
Type a kill count in the row's box and press Enter (for "Kill X …" achievements). Undoable.

### Presets
Save the current pinned stats under a name, apply a preset in one click, delete one (confirm, undoable).

### Dashboard
Coins/min, SP/min, enemies/min (last 2 minutes), drop and gem chances, counters, preview of the next ascension.

### Log
Everything the mod did, newest first. Also written to `BepInEx\slayer_log.txt`.

### Settings
| Section | Content |
|---|---|
| Menu | Reopen where I left it · Confirm dangerous actions · Friendly stat names · Rows per page · Forget menu memory |
| Pinned stats | count, Unpin all |
| Save backups | selected backup, Back up now, Older backup, **Restore** |
| Hotkeys | the current keys (read from the config) |
| Advanced | Analyze the game UI (F11), What's new, version line |

## Save backups
A copy of `savedata.sav` (and `backup.sav`) is made automatically:
- at the start of every session,
- before an ascension / ultra ascension,
- before a preset is applied.

They live in `BepInEx\slayer_backups\` (the 20 most recent ones).

**Restore**: Settings → *Older backup* until the right one is selected → **Restore** (twice) → **close and restart the game**. The backup is put back on startup, before the game reads its save. The current save is copied first (`…_before-restore`). Click again to cancel a scheduled restore.

> ⚠️ The game's cloud sync keeps the save with **the most Slayer Points**. An older backup can therefore be overwritten by the cloud one.

## Game version check
On startup the mod compares the game version with the expected one (`7.2.0`) and checks that the game methods it needs still exist. If something changed, an orange warning is shown on Home and in the log instead of failing silently.

## Hotkeys
| Key | Action |
|---|---|
| F2 | open / close the menu (where you left it) |
| Mouse back button | previous page (menu open) |
| F6 | cycle game speed |
| F7 | add coins |
| F8 | add SP |
| F9 | portal ready |
| F10 | rage ready |
| F11 | UI analysis → `BepInEx\slayer_ui_dump.txt` |

Configurable in `BepInEx\config\leo.idleslayer.slayertools.cfg`.

## Files created by the mod
| File | Purpose |
|---|---|
| `BepInEx\config\leo.idleslayer.slayertools.cfg` | hotkeys and settings |
| `…slayertools.ui.json` | menu memory (last page, scroll, searches, filters, settings) |
| `…slayertools.pinned.json` | pinned stats |
| `…slayertools.presets.json` | presets |
| `…slayertools.favorites.json` | favorites |
| `…slayertools.options.json` | switches (mimics, god mode, over limit…) |
| `BepInEx\slayer_backups\` | save backups |
| `BepInEx\slayer_log.txt` | log |
