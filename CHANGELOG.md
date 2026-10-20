# Changelog

All notable changes to Slayer Tools. Every version is tagged `vX.Y.Z`.

## 5.1.1 - 2026-10-20

- Chest hunt: labels are now placed **on** each chest image. A game layout used to push them next to the chest (one chest to the right) and hide them on the second row.
- "MIMIC" in white on the red chest; shorter labels ("Next x2", "Coins+Souls").

## 5.1.0 - 2026-10-20

- New **Bag (resources)** page: every enemy drop with its real icon, +10 / +100 / +1000, exact amount, Full, Fill everything, Exceed the limit.
- Events: the menu stays open after a spawn so you can spam boxes; a counter shows how many you spawned.
- Removed the x2 / x10 buttons from the editors (long stat names had no room); Max stays on chance stats.

## 5.0.0 - 2026-10-04

Big feature release.

- **Undo** the last 10 changes, **favorites** pinned to the top of the editors, **friendly stat names** with search on both names, **x2 / x10 / Max** quick buttons.
- **Events** page: purple, silver, golden and normal boxes, giant, chest hunt key, Bonus Stage 1/2/3 (or yours) through a portal, ascension and ultra ascension (two-click confirmation), minions (finish missions, claim all, send all).
- **Combat & chests** page: **reveal mimics** during chest hunts, show the content of every chest, god mode against bosses, one-hit bosses, automatic bonus spheres.
- **Dashboard**: coins/min, SP/min, enemies/min, drop and gem chances, next ascension preview.
- **Log** of everything the mod does (also in `BepInEx\slayer_log.txt`).
- **Game version check**: an orange warning instead of silent failures after a game update.
- **Save backups** of `savedata.sav` (session start, before ascension, before presets) and **restore** on next launch.

## 4.3.0 - 2026-10-04

Native in-game menu (first version kept in this repository).

- Native menu built from the game's own UI: buttons, grids and text fields cloned from the Info panel, so it looks like part of the game.
- Two entry points: a star tab in the menu tab bar (between Info and Close) and a round star button next to Ascension and the Bag.
- Pages: Actions, Inventory, Slayer Points, Boost, Quests, Enemies killed, Presets, Settings.
- Editors: type a value in a row and press Enter; edited inventory stats are pinned and survive the game's recalculations and restarts; Unpin gives a stat back to the game and recalculates.
- Read-only stats: Jewels of Soul, purchases, ads, starter packs (`Blocked()`).
- Quests: complete one or all in one click. Enemies: edit kill counters. Presets: save / apply sets of pinned stats.
- Boost: ready now, always ready, and every boost stat.
- Game keyboard is blocked while typing in the menu; our tab is highlighted like a real tab.
- Anti-quit guard: the game's mod detection can no longer close the game.

Versions 4.0 to 4.2 introduced the native menu step by step; their source was not kept, so history starts here.

## Before 4.3.0

- 4.0 - 4.2: first native menu (replacing the external Python overlay). Source not kept.
- 3.x: stat pinning and an external Python overlay talking to the mod over local HTTP.
- 2.x: IMGUI menu (invisible under Unity 6, abandoned).
- 1.x: first hotkeys (coins, SP, portal, rage, speed) and the anti-quit guard.
