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
ToolsBehaviour (MonoBehaviour)       partial, 6 files
 ├─ Plugin.cs      pinning, stat editing (SetProp / ApplyTo), presets & quest helpers, actions, bridge commands, Update()
 ├─ Ui.cs          the menu framework: building blocks, page anatomy, navigation, memory, paged lists, refresh loop, entry points
 ├─ Pages.cs       every page of the menu, built on Ui.cs
 ├─ Features.cs    undo, favorites, friendly names, events, god mode, minions, mimics, dashboard, log, version check, backups
 ├─ Bag.cs         resource logic (add / set / fill)
 └─ Divinities.cs  divinity logic (points, activate / deactivate)
```

## Lifecycle

`ToolsBehaviour.Update()` runs every frame:

1. first time in game: `CalculateValues()` so pinned stats are re-applied;
2. overlay commands (`Bridge.Commands` queue);
3. freezes (portal, rage, coins);
4. `FeaturesTick()`: version check, session-start backup, dashboard sampling, chest marks, auto spheres;
5. `NmUpdate()`: builds the menu as soon as `InfoPanel.instance` exists, runs the visible page's `Tick` (list searches), restores the scroll position, refreshes labels / enabled / visible states (every 0.5 s), detects typing, saves the menu memory;
6. hotkeys (`MenuHotkeys()`: F2 and the mouse back button, then the action hotkeys).

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

## The native menu (v6 framework, `Ui.cs`)

Instead of drawing our own UI (IMGUI was invisible under Unity 6), the mod **clones the game's own UI objects** and arranges them with Unity layout groups.

### Building blocks
| Block | Made from | Method |
|---|---|---|
| text button | the *News* button of the Info panel, background stretched, icon hidden | `NmButton(parent, label, onClick, page, height, width, flex)` |
| tile (icon + text) | same clone, icon moved to the left | `NmTile(parent, label, icon, onClick, page)` |
| plain text | a new `TextMeshProUGUI` with the game's font | `NmText`, `NmSection` |
| text field | the equipment rename `TMP_InputField` | `NmInput` |
| ON / OFF switch | two buttons in a row (title + description, state) | `NmToggle` |
| row / column | `HorizontalLayoutGroup` / `VerticalLayoutGroup` | `NmBox` |
| fixed-column grid | `GridLayoutGroup`, cell width computed from the panel width | `NmCols(parent, cols, cellHeight)` |
| paged list | toolbar (search + filter) + pool of 15 rows + pager | `NmList(page, PagedList, hint, makeRow)` |

After cloning, `Strip()` removes what belongs to the game: `StartingText` (auto-translation that would replace our text), `onClick` listeners, alert icons. Input fields also get `onEndEdit`, `onSubmit`, `onValueChanged`, `onSelect` and `onDeselect` cleared.

### Dynamic state
Nothing is rebuilt while the menu is open. Instead every widget is **bound to functions** that `NmRefresh()` re-evaluates (immediately after a click, then every 0.5 s, only for the visible page):

| Binding | Effect |
|---|---|
| `Func<string>` label | text of a button or text block |
| `EnableWhen(go, Func<bool>)` | `CanvasGroup` alpha 0.4 + not interactable when false |
| `ShowWhen(go, Func<bool>)` | `SetActive` |

```csharp
NmToggle(page.transform, "God mode", "no lives lost against bosses or Bonus Stage saws",
         () => GodMode, v => GodMode = v, page);
NmTile(grid, () => "Purple box", () => NmSprite("Star"), () => SpawnBox(RandomBox.Type.Special, "Purple box"), page)
    .Let(t => EnableWhen(t, IsRunnerNow, page));
```

### Page anatomy
`NmPage(key, hint)` creates a column inside `InfoPanel` with the shared **header** (Back · breadcrumb title · Home) and **status bar** (last toast coloured by `ToastColor()`, or the page hint · Undo). It returns the column; the page builder adds its content below. `PageInfo.OnShow` runs every time the page opens (re-bind lists to live data), `PageInfo.Tick` every frame while visible (live search).

### Navigation and memory
- `Go(key)` pushes the current page onto a back history; `Back()` pops it (Home → closes the menu).
- `NmShow` hides the Info panel's original children (remembered in `nmHidden`) and shows the page; `NmClose()` restores them.
- `UiState` (saved to `leo.idleslayer.slayertools.ui.json`, at most every 2 s and on close): last page, recent pages, scroll offset per page, search / filter / page per list, rows per page, bag step, open-at-home, confirmations, last seen version.
- The scroll offset of the game's `ScrollRect` content is saved when leaving a page and restored two frames after showing one (once the layout is rebuilt), clamped to the new height.

### Paged lists
A `PagedList` has a `Source` (live game data), `Match` (search), `Filters` + `Filter`, and `Sort`. `Rebind()` recomputes the items, clamps the page, shows only the rows that have an item, and runs the `OnBind` callbacks (set icons, refresh input values). Row widgets read their item through `Func<object> item`, so the same 15 rows serve every page of results.

### Confirmations and undo
`Confirm(key)` returns true on the second click within 4 s (or immediately if confirmations are off); `ConfirmText(key, label)` turns the label orange meanwhile. `BeginUndoBatch()` / `EndUndoBatch(label)` turn several changes into one undo step.

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
