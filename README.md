# Deep Dungeon Tracker — Pilgrim & Party

Independent community version of [Marconsou's Deep Dungeon Tracker](https://github.com/marconsou/deep-dungeon-tracker).

Tracks solo and party Deep Dungeon runs, including Pilgrim's Traverse, with floor statistics, timers, and score estimates.

## Install

Add this URL in Dalamud Settings → Experimental → Custom Plugin Repositories, save, then install **Deep Dungeon Tracker — Pilgrim & Party**:

```text
https://raw.githubusercontent.com/LunarHarbor/dalamud-plugins/main/repo.json
```

Open settings with `/ddtp`. Disable the original tracker to avoid duplicate overlays.

Preview for Dalamud API 15. In-game validation is pending; Pilgrim and party scores are provisional estimates.

## Commands

| Command | Action |
| --- | --- |
| `/ddtp` | Settings |
| `/ddtpmain` | Saved runs |
| `/ddtptracker` | Toggle tracker |
| `/ddtptime` | Toggle timer |
| `/ddtpscore` | Toggle score |
| `/ddtpload` | Last saved run |

Result comparison and local exports are under Settings → Validation. No telemetry.

## Build

Requires Windows and .NET SDK 10.0.4xx.

```powershell
./scripts/Build.ps1 -Bootstrap
```

Output: `artifacts/DeepDungeonTrackerPilgrim-1.1.2.0.zip`.

[Patch notes](CHANGELOG.md) · [Quick start](docs/QUICKSTART.md)

## License and credits

[AGPLv3](LICENSE.md). Original code and artwork by Marconsou and upstream contributors; scoring research by Alpha. Modifications by LunarHarbor, September 12, 2026. This version is maintained independently; report its issues here.
