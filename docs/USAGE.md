# User guide

## Opening the menu

Two entry points are added to the game's own UI:

- **the star tab** in the bottom bar of the game menu (between *Info* and the close button);
- **the star button** at the top left, next to Ascension and the Bag (opens / closes directly).

Switching to another game tab or closing the menu closes Slayer Tools.

While you type in a menu field, the game no longer receives the keyboard (no accidental jump / boost / rage).

## Pages

### Home
Summary (coins, CpS, Slayer Points, souls) and access to every page. An **orange message** appears here if the game version does not match (see [Game version check](#game-version-check)). The **Undo** button reverts the last change.

### Actions
| Button | Effect |
|---|---|
| +1 hour / +1 day / +1 week of coins | adds `CpS × duration` coins |
| Coins x2 | doubles your coins |
| Coins locked | coins can no longer go down |
| Slayer Points +10% / +50% / x2 | adds SP |
| Portal / Rage ready | resets the cooldown |
| Portal / Rage always ready | cooldown permanently at zero |
| Game speed | x1 → x2 → x5 → x10 → x20 |

### Events
The menu **stays open**: you can spam the buttons.

- Purple, silver, golden and normal boxes
- Giant
- Chest hunt key
- Bonus Stage (yours, 1, 2 or 3): opens a portal, jump in (closes the menu)
- Ascension / Ultra ascension: **click twice** to confirm, the save is backed up first
- Minions: finish missions, claim all, send all

Spawns only work during the normal run.

### Combat & chests
| Option | Effect |
|---|---|
| Show mimics | during a chest hunt, mimics turn **red** with "MIMIC" written on them |
| Show all chests | shows what every chest contains (Multi, Saver, Souls, Coins, Weapon…) |
| God mode (bosses) | you no longer lose lives against bosses, nor on Bonus Stage saws |
| One-hit bosses | every hit kills the boss |
| Auto bonus spheres | in the Bonus Stage, spheres fly to you by themselves |

These options are remembered between sessions.

### Inventory · Slayer Points · Boost
Editors for **every** numeric stat.

- Type a value in the box, then press **Enter**.
- Accepted numbers: `1500`, `2.5M`, `3B`, `1.2T`, `4Qa`, `1e30`…
- **Max** (on chance stats) = 100.
- **fav** moves the row to the top of the list (remembered).
- **Search** by friendly name or code name (`ruby` or `rubyChance`).
- Edited inventory stats are **pinned** (`*`): the game no longer overwrites them when it recalculates (achievements, purchases, rage…), even after a restart. **Unpin** gives the stat back to the game.
- Some stats are **read-only**: everything related to Jewels of Soul and purchases (see [LIMITATIONS.md](LIMITATIONS.md)).

### Bag (resources)
Every resource (enemy drops) with its real icon.

- `+10` / `+100` / `+1000`, exact number (Enter), **Full** = bag maximum
- **Fill everything**
- **Exceed the limit**: allows going past the bag size
- Resources you have not found yet are at the bottom, greyed out

### Divinities
- Points: `+1` / `+10` / `+100`, or an exact total
- Every divinity (dark ones included): **Activate** / **Deactivate**
- Missing points are added automatically; deactivating refunds the points
- Activate all / Deactivate all

### Quests
Complete one quest (or all of them) in one click. Claim the reward in the game as usual.

### Enemies killed
Change an enemy's kill counter (for "Kill X …" achievements).

### Presets
Save the current set of pinned stats under a name and re-apply it in one click.

### Dashboard
Coins/min, SP/min, enemies/min (measured over the last 2 minutes), drop and gem chances, counters, preview of the next ascension.

### Log
Everything the mod did during the session. Also written to `BepInEx\slayer_log.txt`.

### Settings
- Pinned stats, Unpin all
- Copy the save now, pick a backup, **Restore**
- Names: friendly / code
- Analyze the UI (for developers, see [TOOLS.md](TOOLS.md))

## Undo
The **last 10** changes can be undone: stats, coins / SP, presets, kill counts, resources, divinities.

## Save backups
A copy of `savedata.sav` (and `backup.sav`) is made automatically:
- at the start of every session,
- before an ascension / ultra ascension,
- before a preset is applied.

They live in `BepInEx\slayer_backups\` (the 20 most recent ones).

**Restore**: Settings → pick the backup → click twice → **close and restart the game**. The backup is put back on startup, before the game reads its save. The current save is copied first (`…_before-restore`).

> ⚠️ The game's cloud sync keeps the save with **the most Slayer Points**. An older backup can therefore be overwritten by the cloud one.

## Game version check
On startup the mod compares the game version with the expected one (`7.2.0`) and checks that the game methods it needs still exist. If something changed, an orange message is shown on the Home page and in the log instead of failing silently.

## Hotkeys
| Key | Action |
|---|---|
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
| `…slayertools.pinned.json` | pinned stats |
| `…slayertools.presets.json` | presets |
| `…slayertools.favorites.json` | favorites |
| `…slayertools.options.json` | options (mimics, god mode…) |
| `BepInEx\slayer_backups\` | save backups |
| `BepInEx\slayer_log.txt` | log |
