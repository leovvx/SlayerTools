# Changelog

All notable changes to Slayer Tools. Every version is tagged `vX.Y.Z`.

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
