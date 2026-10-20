# Code architecture

The whole plugin is a startup class (`Plugin`) and one big `MonoBehaviour` (`ToolsBehaviour`) split across several files with `partial`.

```
Plugin (BasePlugin)                  src/Plugin.cs
 ├─ Load()
 │   ├─ ApplyPendingRestore()        restores a save requested during the previous session
 │   ├─ Harmony patches              see the table below
 │   ├─ ClassInjector.RegisterTypeInIl2Cpp<ToolsBehaviour>()
 │   └─ Bridge.Start(port)           small local HTTP server (legacy overlay API)
 │
ToolsBehaviour (MonoBehaviour)       partial, 4 files
 ├─ Plugin.cs      pinning, native menu (Nm* building blocks), editors, presets, quests, enemies, entry points, actions
 ├─ Features.cs    undo, favorites, friendly names, events, god mode, minions, mimics, dashboard, log, version check, backups
 ├─ Bag.cs         Bag tab
 └─ Divinities.cs  Divinities tab
```

## Lifecycle

`ToolsBehaviour.Update()` runs every frame:

1. first time in game: `CalculateValues()` so pinned stats are re-applied;
2. overlay commands (`Bridge.Commands` queue);
3. freezes (portal, rage, coins);
4. `FeaturesTick()`: version check, session-start backup, dashboard sampling, chest marks, auto spheres;
5. `NmUpdate()`: builds the menu as soon as `InfoPanel.instance` exists, filters searches, refreshes labels (every 0.5 s), detects typing;
6. hotkeys.

Every block is wrapped in `try/catch`: a feature broken by a game update must not take the others down.

## Harmony patches

| Game method | Kind | Why |
|---|---|---|
| `Application.Quit()` / `Quit(int)` | prefix | the game quits by itself when it detects mod files; only real exits are let through |
| `UIManager.ExitGame` / `WaitForUploadAndQuit` | prefix | marks "the player really wants to quit" |
| `Input.GetKey*` | prefix | blocks game input while typing in the menu |
| `ShopManager.Open*`, `UIManager.ToggleShop` | postfix | closes our pages when another tab opens / the menu closes |
| `PlayerInventory.CalculateValues` | prefix + postfix | snapshot pinned stats before, re-apply after |
| `BossMapController.PlayerHit` / `PlayerPoison`, `BonusMapController.SawHitted` | prefix | god mode (skip the method) |
| `BossMapController.TakeDamage` | prefix | one-hit bosses (`damage = boss health + 1`) |

## Stat pinning

The game recalculates almost all stats in `PlayerInventory.CalculateValues()` (achievements, purchases, map changes…), which used to wipe edits.

- Every edited inventory stat is stored in `pinned` (name → value) and saved as JSON.
- `SnapshotPinned()` (before the recalculation) records the current values.
- `ReapplyPinned()` (after) rewrites **only** the stats the recalculation changed. Counters the game does not recalculate (coins, for example) stay free, so purchases cost the normal price.

## The native menu

Instead of drawing our own UI (IMGUI was invisible under Unity 6), the mod **clones the game's own UI objects**:

| Building block | Cloned from | Method |
|---|---|---|
| button | the *News* button of the Info panel | `NmButton(parent, label, sprite, onClick, page, icon)` |
| button grid | the Info panel's `Buttons` container (`AutoExpandGridLayoutGroup`) | `NmGrid` |
| text field | the equipment rename `TMP_InputField` | `NmInput` |
| row / column | `GameObject` + `HorizontalLayoutGroup` / `VerticalLayoutGroup` | `NmBox` |
| menu tab | the *Info* tab of `Shop Panel/Wrapper/Buttons` | `NmBuildEntryPoints` |
| round button | `Left Buttons/Ascension Button` | `NmBuildEntryPoints` |

After cloning, `Strip()` removes what belongs to the game: the `StartingText` component (auto-translation that would replace our text), `onClick` listeners, alert icons. Input fields also need `onEndEdit`, `onSubmit`, `onValueChanged`, `onSelect` and `onDeselect` cleared.

### Pages
- `NmNewPage(key)` creates a column inside `InfoPanel`; `nmPages[key]` finds it again.
- `NmShow(key)` hides the Info panel's original children (remembered in `nmHidden`) and shows the page; `NmClose()` restores everything.
- Heavy lists (quests, bag, divinities, presets) are filled **the first time they open** (`NmFill*`), once game data is loaded.

### Dynamic labels
Every button takes a `Func<string>` instead of fixed text. `NmRefresh()` re-evaluates them every 0.5 s for the visible page:

```csharp
NmButton(grid, () => "God mode (bosses): " + OnOff(GodMode), "Star",
         () => { GodMode = !GodMode; SaveOptions(); }, page);
```

`RecentToast()` returns the last message for 4 s: many banners show `RecentToast() ?? normalText`.

## Undo
`PushUndo(label, action)` pushes a function that restores the previous state (10 max). `RememberProp` (one stat + its pin) and `RememberCoinsAndSP` cover the common cases; the bag, divinities, presets and kill counters push their own actions.

## Chest hunt (mimics)
Chest contents are drawn **as soon as the hunt starts** (`ChestHuntManager.GenerateChests`, called from `StartEventDelayed`). Every `Chest` has a `type` (`ChestType.Mimic = 1`…) and a `chestObject` (its UI object).

`ChestTick()` (every 0.25 s):
1. for every closed chest to show, creates a `TextMeshProUGUI` as a child of **the chest image** (not the root object, which a game layout arranges), with `LayoutElement.ignoreLayout = true`, stretched over the image;
2. tints mimic images red (original colours remembered);
3. removes marks and tints as soon as the chest is opened or the hunt ends.

## Save backups
- `BackupSave(reason)` copies `%LOCALAPPDATA%Low\Pablo Leban\Idle Slayer\savedata.sav` (+ `backup.sav`) into `BepInEx\slayer_backups\<date>_<reason>\`.
- "Restore" only writes `RESTORE_PENDING.txt`; `Plugin.Load()` does the copy on the **next launch**, before the game reads its save. Copying during play would be pointless: the game rewrites its file when it quits.

## Bridge (legacy overlay API)
`Bridge` is a tiny HTTP server (`127.0.0.1:47823`, `TcpListener`): `GET /state`, `/props/inv`, `/props/sp`, `POST /cmd`. It never touches the game from its own thread: commands go through a queue processed in `Update()`. It used to drive an external Python overlay before the native menu existed, and stays available for anyone who wants to control the mod from outside.
