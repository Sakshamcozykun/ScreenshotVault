# ScreenshotVault

Windows 10/11 context-aware screenshot manager built with **C# / .NET 8 / WinUI 3**.

---

## Prerequisites

| Requirement | Version |
|---|---|
| Windows | 10 (19041+) or 11 |
| Visual Studio | 2022 17.8+ |
| .NET SDK | 8.0+ |
| Windows App SDK workload | 1.5+ |
| Architecture | x64 only |

Install the Windows App SDK workload in Visual Studio Installer:
`Individual Components → Windows App SDK C# Templates`

---

## Setup

```bash
git clone <repo>
cd ScreenshotVault
dotnet restore
dotnet build -c Debug
```

### Run
Open `ScreenshotVault.sln` in Visual Studio 2022.
Set `ScreenshotVault.App` as the startup project.
Press **F5**.

### Run Tests
```bash
dotnet test tests/ScreenshotVault.Tests/
```

---

## Data Location

All runtime data is stored in:
```
%LocalAppData%\ScreenshotVault\
  screenshots\
    Miscellaneous\
    Work\
    Development\
    Browser\
    .trash\           ← soft-deleted (auto-purged after 30 days)
  vault.db            ← SQLite metadata
  rules.json          ← classification rules (auto-updated by active learning)
  theme.txt           ← saved theme preference
```

---

## Architecture Overview

```
PrtScn keypress
    └─ GlobalHookService (WH_KEYBOARD_LL)
           └─ ScreenCaptureService (GDI BitBlt)
           └─ ContextExtractor (UIAutomation STA thread)
                  └─ CaptureOrchestrator
                         └─ RulesEngine.Classify()
                                └─ ActiveLearner.Rules (live list)
                         └─ ScreenshotRepository.SaveAsync()
                         └─ MainShell toast notification (UI thread)

User classifies Misc screenshot
    └─ MiscClassifyViewModel.ClassifyAsync()
           └─ CaptureOrchestrator.ReclassifyAsync()
                  └─ ScreenshotRepository.UpdateCategoryAsync()
                  └─ ActiveLearner.RecordCorrection()
                         └─ Penalise wrong rules
                         └─ Reinforce correct rules
                         └─ Synthesise new rule if no match
                         └─ PersistRules() → rules.json (atomic write)
```

---

## Active Learning

Rules are stored in `rules.json` as weighted predicates.
Each user correction adjusts weights:

- **Reinforcement**: +0.10 when a rule correctly predicted the user's choice
- **Penalty**: −0.20 when a rule fired but was overridden
- **Synthesis**: New rule created at weight 0.15 when no rule covered the context
- **Pruning**: Rules below weight 0.15 are removed on next save

A rule needs ~3 reinforcements (at +0.10 each, starting at 0.15) to
confidently classify (threshold: 0.30) without user confirmation.

---

## Themes

Toggle via the **🎨 Theme** button or `ThemeService.Toggle()`.
Preference is persisted to `theme.txt` and restored on next launch.

| Theme | Description |
|---|---|
| Modern | Dark Fluent/Mica-inspired, Segoe UI Variable |
| Windows XP | ECE9D8 grey, Tahoma, 3-D raised buttons, Luna title bar gradient |

Both themes define identical ResourceDictionary keys — all controls
re-render automatically via `{ThemeResource}` bindings when the
dictionary is swapped.

---

## Keyboard Shortcuts (Swipe Mode)

| Key | Action |
|---|---|
| `←` Left Arrow | Delete (soft) |
| `→` Right Arrow | Keep |
| `Ctrl+Z` | Undo last action (up to 20) |

---

## Notes

- The app installs a **system-wide** `WH_KEYBOARD_LL` hook. This requires
  the app to have an active message pump (it does, via WinUI 3).
- UIAutomation URL extraction is guarded by a **3-second timeout**. Hung
  automation trees (common with some Electron apps) will not block capture.
- Screenshots go to `.trash/` on soft delete and are permanently deleted
  after 30 days (implement a background `TrashPurgeService` using
  `PeriodicTimer` for production).
- The `asInvoker` manifest ensures the hook works for **non-elevated**
  foreground windows. Running as admin would break URL extraction for
  standard-privilege browser processes.
