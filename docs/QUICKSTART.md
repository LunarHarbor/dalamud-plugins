# Quick start

1. Add `https://raw.githubusercontent.com/LunarHarbor/dalamud-plugins/main/repo.json` under Dalamud Settings → Experimental → Custom Plugin Repositories.
2. Save, open the plugin installer, and install **Deep Dungeon Tracker — Pilgrim & Party**.
3. Disable the original tracker to avoid duplicate overlays.
4. Open `/ddtp` and enable the tracker, score, and timer windows you want.

The live score is an estimate. Pilgrim and party formulas remain provisional; in-game validation is pending.

The tracker’s Floor and Set columns restart with each new floor or set. Total and the score estimate retain the tracked run’s history across sets on the same game save slot.

Settings → Validation contains game-result comparison, manual result entry, and local exports. If automatic save-slot selection is unavailable, associate the game save slot there before the next set.

You can install with both game save slots already in progress. Resume each slot normally; the tracker records each separately from the first observed floor. Earlier floors, kills, and bonuses cannot be recovered, so that run's estimate covers an incomplete capture. See Settings → Validation for its capture notes. If the slot cannot be identified, the capture is saved separately instead of replacing either slot's history.

Installing or reloading inside a duty does not identify which game slot is active. Use Settings → Validation → Associate game save slot during that duty if needed. Existing tracker history is archived before replacement. Unassociated captures and archived attempts are available in `/ddtpmain` under Other captures.

Run data is stored separately from the original plugin. Exports omit character names and identifiers. Nothing is uploaded automatically.

For manual installation, extract the complete release ZIP and add `DeepDungeonTrackerPilgrim.dll` to Dalamud's Dev Plugin Locations.
