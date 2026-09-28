# Cat Flap Codebase Overview

Generated from a code-only exploration pass. Unity Editor state (prefab/scene wiring) is not
verified here — see `CLAUDE.md` at the repo root for the authoritative working rules.

## Layering

- `Assets/0_Game/CatFlap/Scripts/Gameplay/` — Cat Flap-specific code.
  - `Models`, `Levels`, `Validation`, `Boosters`, `Progress`, `Runtime` — plain C#, no `UnityEngine`
    dependency (testable in isolation).
  - `UnityAdapters` — thin `MonoBehaviour` wrappers that bind the plain-C# runtime to Unity scene
    objects (views, buttons, popups).
- `Assets/Shared/Features/` — reusable systems shared across games: Audio, Vibration, PlayerData,
  GameConfig, GameFlow (popups/input-blocking), Boosters, Notes, Store, DailyChallenges,
  DailyRewards, SpinTheWheel, CheatUI, Lives, LevelProgression, LevelMistakeLimit, Input,
  FileStorage, CollectScreen, UnlockScreen, Boot.

Rule of thumb: game-specific board/rule logic and effects belong under `0_Game/CatFlap`; anything
reusable (button state, popup animation, input blocking, player data, config, audio/vibration,
cheat panel) belongs under `Shared/Features` and should not be duplicated or moved.

## Core runtime flow

`CatFlapRuntimeController` (+ its `.VisualState` partial) is the view-agnostic core: it drives
`CatFlapRuntimeSession` (board state, mistakes, boosters, progress) and talks to views only through
interfaces — `ICatFlapBoardView`, `ICatFlapStatusView`, `IGameFlowView`, `ICatFlapAnimationView`.
This is what lets the UI-canvas board (`CatFlapBoardViewAdapter`) and the world-space sprite board
(`CatFlapSpriteBoardViewAdapter`) both work through the same controller.

`CatFlapGameplayManager` (`UnityAdapters/`, split into `.Validation` and `.Cheat` partials) is the
scene-level orchestrator MonoBehaviour. It owns:

- Level loading, both regular campaign levels (`levelSequence`/`levelJson`) and Daily Challenge
  levels (`dc_{puzzle}_{sub}.json` via `Resources.Load`), each with its own board-save row so they
  never collide.
- Booster wiring (Hint/Fix/Undo/Buy) between `Shared.Boosters.BoosterManager` and Cat Flap's
  `CatFlapBoosterManager`/`CatFlapHintService`/`CatFlapFixService`.
- Popup state machines: settings, out-of-lives, buy-booster, store — each sets a `*PopupOpen` bool
  that gates `controller.Tick` and `Shared.GameFlow.InputManager.GamePlayInputEnabled`.
- Notes mode / Apply All (single atomic batch: one validation pass, one sound/vibration, per-cell
  `RecordMove` for correct undo ordering).
- Autosave, save-on-pause/quit, and level-win/fail/out-of-mistakes event handling.

## Conventions worth remembering

- Validation delays: `wrongValidationDelaySeconds` vs `correctValidationDelaySeconds` are distinct
  and must not cross-wire (this has broken before — see CLAUDE.md).
- Booster config fields are `BuyGemCost` / `BuyCount` (not `GemCount` / `BoosterBuyCount`).
- Vibration goes through `VibrationManager.Instance.PlayVibration(ImpactFeedbackStyle.X)` directly —
  no wrapper methods.
- Sound goes through `SoundName` constants + `AudioManager.Instance.PlaySoundAction`.
- Gameplay input is gated by `Shared.GameFlow.InputManager.GamePlayInputEnabled`, generally
  re-enabled a frame after a popup/animation completes to avoid click-through.

## Current branch state (as of last check)

Branch: `release/release_0_1_0/dev/Jigyasu/FeedBackChangse`. Uncommitted work in progress is level-win
VFX/feedback polish:

- `LevelWinPopupController.cs` and `LevelWinUIRoot.prefab` changed (coin/stat display, win
  animation).
- `Assets/Win_Confetti.prefab` (old, top-level) deleted; replaced by new prefabs under
  `Assets/0_Game/CatFlap/Prefabs/Vfx/` (`Win_Confetti.prefab`, `Sparkles_Trail.prefab`), backed by a
  newly imported "Lana Studio Hyper Casual FX" asset pack (materials/textures for circles, dust,
  flares, squares).
- `MainMenuHandler.cs`, `AudioManager.cs`, `MainMenuBottomButtons.prefab`, `MainMenuUIRoot.prefab`
  also touched, matching the most recent commit ("Setting Button Haptics and Sound").
- New untracked `Assets/0_Game/CatFlap/Prefabs/UI/` folder — UI prefab work in progress, not yet
  reviewed.
- `GamePlay.unity` and the Nunito SDF font assets show large diffs, likely just Editor-touched
  serialized data rather than intentional content changes — worth confirming before commit.

## Existing docs in this folder

- `CatFlapRuntimeIntegration.md`
- `CatFlapScenePrefabRequirements.md`
- `CatFlapFinalValidationReport.md`
