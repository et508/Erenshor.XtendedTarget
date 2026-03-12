# Xtended Target Window

Inspired by the Extended Target Window from EverQuest, this mod adds a real-time aggro tracking panel that shows every enemy currently engaged with you or your group — all in one place, without having to tab through targets.

---

## Features

### Aggro Tracking
Every enemy that has you or a group member on its hate table is listed automatically. Unlike the game's internal aggro lists, this mod reads directly from each NPC's hate table so entries persist for the full duration of a fight — enemies won't vanish from the list just because they temporarily switched targets.

### Per-Row Information
Each row in the window shows:
- **Slot number** — position in the list
- **HP bar** — color-coded green → yellow → red as health drops, with a percentage readout
- **Name** — the enemy's name, truncated if needed
- **Current target** — who the enemy is attacking right now (`YOU` in red if it's on you, or a group member's name in blue)
- **Hate rank** — your position on that enemy's hate table (`#1` in gold means you have top aggro)

### Click to Target
Click any row to instantly target that enemy, using the same targeting system the game uses natively.

### AutoHide Mode
When AutoHide is enabled, the window chrome (background, border, title bar) is hidden completely. Only the active enemy rows appear during combat, and they disappear entirely when nothing has aggro on you or your group. Enable it by clicking the **Hide** button in the title bar — to disable it, set `AutoHide = false` in the config file.

### Window Controls
- **Drag** — click and drag the title bar to reposition the window anywhere on screen. Position is saved automatically on release.
- **Lock / Unlock** — locks the window in place so it can't be accidentally moved. State persists across sessions.
- **Hide** — enables AutoHide mode, hiding the chrome and showing only combat rows.

---
## Installation
- Install [BepInEx Mod Pack](https://github.com/et508/Erenshor.BepInEx/releases/tag/e1)
- [Download the latest release](https://erenshorvault.app/)
- Extract folder into `Erenshor\BepInEx\plugins\` folder.

---

## Configuration

- Run Erenshor so the config file will be automatically created
- Open `et508.erenshor.xtarget` in your `Erenshor\BepInEx\config`
- Change values to your liking
- I recommend using a config manager like [BepInExConfigManager](https://github.com/sinai-dev/BepInExConfigManager) for easier config changes from ingame

| Setting | Default | Description |
|---|---|---|
| `ToggleKey` | `F11` | Key to show/hide the window |
| `MaxSlots` | `8` | Maximum number of enemy rows to display (1–20) |
| `PositionX` | `10` | Saved X position of the window |
| `PositionY` | `-10` | Saved Y position (negative = from top of screen) |
| `Locked` | `false` | Whether the window position is locked |
| `AutoHide` | `false` | Show only rows during combat, hide everything out of combat |

---