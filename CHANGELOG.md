# Patch notes

## 1.1.2 — September 13, 2026

- Fixed run totals resetting between ten-floor sets when a completion event was missed.
- Improved save-slot detection during entry while keeping the two slots separate.
- Restored accumulated score immediately when continuing a run.
- Preserved uncertain exits and recovered eligible captures affected by the previous exit handling.

## 1.1.1 — September 12, 2026

- Fixed capture startup and recovery across duties and plugin reloads.
- Kept existing game save slots separate when installing during ongoing runs.
- Corrected completion, timer, and stored score accounting.
- Improved corrupt-save recovery and validation.
- Hardened native UI reads and overlay interaction.

## 1.1.0 — September 12, 2026

- Added Pilgrim's Traverse tracking and solo/party score estimates.
- Added Pilgrim pomanders, incense, candles, and boss mappings.
- Fixed duplicate death counting and floor-transition recovery.
- Added periodic saves, backup recovery, and result comparison.
- Preserved overlay styling; diagnostics remain in settings.
- Added separate installer identity and `/ddtp` commands.

Preview for Dalamud API 15. In-game validation is pending. Pilgrim and party scoring is provisional; some party events may be missed outside client range.
