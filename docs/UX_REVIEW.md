# UI / UX review (v5.2 → v6.0)

A full review of the in-game menu as it was in 5.2, the use cases it has to serve, what was wrong, and what v6 changes. Written so the decisions can be checked against the code (`src/Ui.cs`, `src/Pages.cs`).

---

## 1. Context and constraints

| Constraint | Consequence |
|---|---|
| The menu lives **inside the game's Info panel**: about **528 × 471 UI units** visible, inside the game's own `ScrollRect`. | Very little room. Anything below ~470 units needs a scroll; every row height matters. |
| It is built by **cloning the game's widgets** (News button, rename text field). | Looks native, but we inherit their sizes (News button = 254 × 100) and behaviours (`AnimatedButton`, `StartingText`). |
| Pixel font (**Nokia FC**), TextMeshPro. | Long labels must auto-size; rich text (`<color>`, `<size>`) is available for hierarchy. |
| The player is usually **mid-run**: the menu is a quick detour, then back to the game. | Speed of "open → do one thing → close" is the main metric. |
| Some actions only work in a given game state (spawns during the run, chest marks during a hunt). | The UI must show *when* something can work, not just fail. |

## 2. Who uses it, and for what

Léo (and friends) — players who know the game well, tinker with their own save, and come back to the same few things again and again.

| # | Use case | Frequency | v5.2 path | v6 path |
|---|---|---|---|---|
| U1 | Spam boxes / giants during a run | very high | tab → Home → Events → click × N (menu stays open) | **F2** reopens Events directly → click × N |
| U2 | Change one stat (e.g. ruby chance) | high | tab → Home → Inventory → scroll 150 rows or search → type → Enter | F2 → Home search "ruby" → result → type → Enter; or F2 reopens Inventory **with the last search still there** |
| U3 | Undo a mistake | medium | find a page that has an Undo button (Home, Actions, editors, Settings) | **Undo is on every page**, in the status bar |
| U4 | Check what is currently switched on (god mode, speed, freezes) | medium | visit Actions, Combat, Boost one by one | **Home "Active:" line** lists everything, with **All off** |
| U5 | Chest hunt with a key | medium | nothing to do once "Show mimics" is on | same, plus the hunt status at the top of Combat & chests |
| U6 | Give resources / activate divinities | medium | long un-paged lists, one big row each | paged, filterable lists with icons; batch actions are one undo step |
| U7 | Complete quests | low | list rebuilt on open, no filter | paged list, filter Daily / Weekly / To claim, "Complete all (n)" |
| U8 | Back up / restore the save | rare but critical | Settings, unclear state | Settings → Save backups: selected backup, scheduled state, confirm |
| U9 | Find a feature you forgot | rare | read every page | Home search also finds pages |

## 3. Heuristic review of v5.2

Severity: **4** blocks the task · **3** major · **2** minor · **1** cosmetic.

| # | Finding (v5.2) | Heuristic | Sev. | v6 fix |
|---|---|---|---|---|
| F1 | Every open starts on Home; scroll, searches and the editor you were in are lost. | Recognition rather than recall / efficiency | 3 | Reopens the last page, restores scroll, searches, filters and list pages (`UiState`). Option to always open on Home. |
| F2 | "< Back" always goes to Home, not to where you came from. | Match with real-world navigation | 2 | Real back history; mouse back button too. Header always has Back + Home. |
| F3 | Messages appear in a different place on every page (a banner that replaces some other text for 4 s; some pages have none). | Visibility of system status / consistency | 3 | One status bar on every page, same place, coloured by kind (green ok, orange warning, red error), page hint when idle. |
| F4 | Buttons that cannot work (spawns outside the run, Undo with nothing to undo, Unpin all with nothing pinned) look enabled and only fail after the click. | Error prevention | 3 | `EnableWhen`: greyed out and not clickable, with the reason shown on the page. |
| F5 | Home: 14 equal 254 × 100 buttons, no grouping, needs scrolling, no live information. | Aesthetic & minimalist design / IA | 2 | Sections **Play / Edit / Tools**, 3-column compact tiles with a live badge ("12 pinned", "3 open", "run only"), summary at the top. |
| F6 | Toggles shown as "Thing: YES/no" inside a normal button; the effect is not explained. | Match / help | 2 | Switch rows: title + one-line description + green **ON** / grey **OFF**; the whole row is clickable. |
| F7 | Editors build ~150 rows at once; slow to open, very long scroll, and rows jump when favourites are added. | Efficiency / flexibility | 3 | Paged lists (6–15 rows, setting) over a pool of reused rows; filters (Favorites, Pinned…); stable sort (favourites first). |
| F8 | Each row carried 3–5 buttons (Unpin shown as "-" when not pinned, Max, x2/x10…). Long names had no room. | Minimalist design | 2 | Row = name · value box · only the buttons that apply now (Max only on chance stats, Unpin only when pinned, ON/OFF for booleans). |
| F9 | Read-only stats look editable until you try. | Error prevention | 2 | No input box; value shown in the name with "(read-only)". |
| F10 | Dangerous actions are inconsistent: Ascension asks twice, "Unpin all", "Delete all presets", "Deactivate all" do not. | Consistency / error prevention | 3 | One confirmation pattern (second click within 4 s, label turns orange), setting to turn it off. |
| F11 | Batch actions ("Activate all", "Fill everything") flood the 10-step undo history. | User control & freedom | 2 | Undo batches: one click = one undo step. Unpin / Unpin all / divinity deactivation / preset deletion are now undoable too. |
| F12 | No way to search across pages; the user must know where a stat lives. | Recognition / flexibility | 2 | Global search (stats, resources, divinities, enemies, pages) with "jump there, already filtered". |
| F13 | Enemies page: pick an enemy, then type in a separate box, then Apply (3 steps, hidden state). | Efficiency | 2 | Inline box on each row, Enter applies. |
| F14 | No overview of active effects; it is easy to forget x10 speed or god mode on. | Visibility of system status | 2 | Home "Active:" line + **All off**. |
| F15 | The legacy F2 key toggled an overlay that is no longer shipped. | Consistency | 1 | F2 opens / closes the menu from anywhere (configurable). |
| F16 | After an update, nothing tells the player what changed. | Help & documentation | 1 | "What's new" page shown once per version. |

## 4. Information architecture

```
v5.2                                 v6.0
Home (14 big buttons)                Home
 ├ Actions                            ├ summary · active effects (All off) · search · recent pages
 ├ Events                             ├ PLAY   Actions · Events · Combat & chests
 ├ Combat & chests                    ├ EDIT   Inventory · Slayer Points · Boost · Bag · Divinities · Quests · Enemies
 ├ Dashboard                          └ TOOLS  Presets · Dashboard · Log · Settings · What's new
 ├ Bag (resources)                   Search (results from every page)
 ├ Divinities
 ├ Journal → Log
 ├ Settings
 ├ Undo
 ├ Inventory / SP / Boost
 ├ Quests / Enemies / Presets
```

Grouping follows intent: **Play** (things that act on the current run), **Edit** (data you change), **Tools** (meta). Each tile shows a live badge so the Home page doubles as a dashboard.

## 5. Page anatomy (every page)

```
┌──────────────────────────────────────────────────────────┐
│ [< Back]        Edit  >  Inventory              [ Home ] │  header (44)
│ Ruby chance = 100                               [Undo 3] │  status bar (40)
├──────────────────────────────────────────────────────────┤
│ [ Search (e.g. ruby, chance, damage) ] [Show: Favorites] │  list toolbar
│ * Ruby chance                        [Max] [   100   ]   │  rows (pooled)
│ Sapphire chance                      [Max] [    12   ]   │
│ ...                                                      │
│ [< Prev]          Page 2 / 13  (150 items)     [Next >]  │  pager
└──────────────────────────────────────────────────────────┘
```

- **Header**: Back (history) · breadcrumb title · Home (Settings on Home itself).
- **Status bar**: the last message for 4 s, coloured by kind; otherwise the page's hint. **Undo** is always at the same place, greyed out when there is nothing to undo.
- **Lists**: search + filter + pager; everything is remembered per list.

## 6. Page by page

| Page | v6 decisions |
|---|---|
| Home | Two-line summary (coins / CpS, SP / souls); Active line only when something is on; search box; 3 recent pages; sectioned tiles with badges. |
| Search | Paged results with filter (Stats, Bag, Divinities, Enemies, Pages); clicking opens the page with its search pre-filled. |
| Actions | Sections Coins / Slayer Points / Cooldowns (with live seconds) / Game speed (current speed highlighted). Freezes and coin lock are switches. |
| Events | Orange notice and greyed tiles outside the run; spawn tiles with icons; Bonus Stage explains that it closes the menu; ascension preview above the two confirm buttons; minions in one row. |
| Combat & chests | Live chest hunt status; switches with descriptions; a key-spawn shortcut next to the mimic options. |
| Inventory / SP / Boost | Paged, filterable, favourites first; click a name to favourite it; pinned names in blue; Unpin per row and "Unpin all" (confirm, undoable). |
| Bag | Step selector (1 → 1M) applies to both − and +; Over-limit switch; Fill everything (one undo step); filters Found / Not found / Not full; icons dimmed for resources not found yet. |
| Divinities | Points summary; +1 / +10 / +100 / +1000; Activate all (one undo step) / Deactivate all (confirm); filters Active / Inactive / Dark. |
| Quests | "Complete all (n)" disabled when nothing is left; filters Daily / Weekly / To claim; progress under each name. |
| Enemies | Inline kill-count box per row; undoable. |
| Presets | Shows how many stats a save would store; Save disabled when nothing is pinned; per-preset delete with confirm and undo. |
| Settings | Menu behaviour switches, rows per page, forget memory; pinned stats; backups with the selected copy and scheduled state; hotkey reference read from the config; version line. |

## 7. Interaction details

- **Memory** (`leo.idleslayer.slayertools.ui.json`): last page, 6 recent pages, scroll position per page, search / filter / page per list, rows per page, bag step, open-at-home, confirmations, last seen version. Written at most every 2 s and when the menu closes.
- **Scroll restore** happens two frames after a page opens, once the layout has been rebuilt, and is clamped to the new content height.
- **Typing guard**: every input created through `NmInput` is tracked; while one is focused the game receives no keys (the mod's own hotkeys included).
- **Colour language**: gold = titles / values to notice, green = ON / success, grey = secondary / OFF, orange = warning / confirm, red = error, blue = pinned, purple = dark divinities and SP.
- **Confirmation**: second click within 4 s; the button label turns orange ("Confirm: Restore?") so the state is visible where the user is looking.

## 8. What was deliberately not done

- No custom art or animations: everything stays built from the game's own widgets so it keeps looking native and survives game updates.
- No drag-and-drop or sliders: the cloned widgets are buttons and text fields; typing numbers (`2.5M`, `1e30`) is faster anyway.
- No casino features and no editing of paid-currency stats (see [LIMITATIONS.md](LIMITATIONS.md)).

## 9. Ideas for later

- Per-stat "reset to the game's value" without unpinning everything (needs the original value captured before the first edit).
- Pin any page to a "Quick" strip on Home.
- Keyboard navigation inside lists (arrow keys + Enter) once the game's own input handling is mapped.
- A compact mode (36-unit rows) for people who prefer more rows per page.

## 10. In-game test checklist

1. First open after updating shows **What's new** once; closing and reopening goes to the last page.
2. Scroll down in Settings, close, reopen: same scroll position.
3. Inventory: search "ruby", go to page 2, close the game, restart: search and page are still there.
4. Events outside the run (e.g. in a Bonus Stage): spawn tiles greyed out with the orange notice.
5. Turn on god mode and speed x5 → Home shows "Active: God mode · Speed x5"; **All off** turns both off.
6. Divinities → Activate all → Undo once: everything goes back.
7. Unpin all asks for a second click; Undo restores the pins.
8. F2 opens and closes the menu from the game; the mouse back button goes to the previous page.
9. Type in any box: the character does not jump / boost.
10. Chest hunt: mimics still marked (unchanged code path).
