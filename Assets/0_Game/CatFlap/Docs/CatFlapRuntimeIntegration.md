# Cat Flap Runtime Integration

Phase 3 adds code-only runtime APIs. Unity developers still own scenes, prefabs, materials, animations, Inspector wiring, and Play Mode testing.

## Level JSON

`CatFlapLevelRepository` loads JSON from a `string` or `TextAsset`, parses it with `CatFlapLevelJsonParser`, then validates it with `CatFlapLevelValidator` before creating runtime state.

```json
{
  "id": "level_001",
  "name": "Level 1",
  "difficulty": "Easy",
  "size": 4,
  "lives": 5,
  "solution": [
    { "cells": [0, 0, 1, 1] },
    { "cells": [0, 1, 0, 1] },
    { "cells": [1, 0, 1, 0] },
    { "cells": [1, 1, 0, 0] }
  ],
  "clues": [
    { "cells": [0, -1, -1, 1] },
    { "cells": [-1, -1, -1, -1] },
    { "cells": [-1, -1, -1, -1] },
    { "cells": [1, -1, -1, 0] }
  ]
}
```

Cell values:

- `-1`: empty clue only
- `0`: Fish
- `1`: Cat

`solution` cells must be `0` or `1`. `clues` cells may be `-1`, `0`, or `1`.

## Game Config

One-time game/economy values live in the shared GameConfig module:

- `Assets/Shared/Features/GameConfig/Data/GameConfig.asset`
- `Assets/Shared/Features/GameConfig/Scripts`

Current fields:

- `totalLevels`: maximum playable levels from the assigned level sequence.
- `gameJoinGems`: initial gem balance for player/game start.
- `levelWinGems`: configured level-win economy value reserved for result flow.
- `levelReviveGems`: configured retry/revive cost reserved for fail/retry flow.
- `levelMistakeLimit`: per-level mistake allowance before the level fail popup.
- `hardLevels` and `loopLevels`: generic level grouping data for progression/looping.
- `boosterConfigList`: generic ordered booster config list. Index `0` is Undo, `1` is Hint, `2` is Fix. Each item contains `unLockLevel`, `unLockCount`, `buyGemCost`, and `buyCount`.
- `ResumeSavedLevelOnPlay`: when true, Play resumes the saved level index.
- `RestoreCurrentBoardOnLoad`: when true, reload restores saved current-board state.
- `CurrentLevelIndexSaveKey`: shared player-data key for current level progress.
- `BoardStateSaveKeyPrefix`: per-level board state key prefix used inside shared level progression records.

`Shared.GameConfig.GameConfigAsset` stores and normalizes this config. If the config asset is not assigned, `CatFlapGameplayManager` falls back to default values. Cat Flap-specific timing, such as delayed validation, remains on `CatFlapGameplayManager`.

## Runtime Flow

Startup flow:

1. `BootScene` opens first.
2. `BootLoader` reads shared `GameConfig.asset`.
3. `BootLoader` loads or creates generic player data at `Application.persistentDataPath/player-data.json`.
4. `BootLoader` shows `StudioLoadingImageRoot` for `2` seconds.
5. `BootLoader` shows `GameSplashImageRoot` for `2` seconds.
6. `BootLoader` loads `GamePlay`.
7. `GamePlay` opens on the menu page.

Shared player data currently stores:

- `PlayerId`
- `Username`
- `ProfilePictureId`
- `ProfileFrameId`
- `CurrentLevelIndex`
- `TotalGems`
- `CreatedUtc`
- `LastUpdatedUtc`
- `Variables`: extensible key/value entries for future projects or new game features.

Cat Flap currently saves shared player data when progress changes, when a level is won, when a level fails, and when gems are successfully spent by Hint/Fix boosters.

Shared level progression data is separate from shared player data. `Shared.LevelProgression.LevelProgressionSaveSystem` stores in-level resume state at `Application.persistentDataPath/level-progression.json`. Each record has `Scope`, `ProgressionId`, `LevelId`, `LevelIndex`, and generic `PayloadJson`, so normal campaign play, today's daily challenge, and a selected previous-day challenge can each preserve independent progress. Cat Flap currently uses this module for current board restore instead of `PlayerPrefs`. Normal mode keeps only the current level record for its progression id; daily challenge flows can enable history keeping so incomplete previous-day records remain available.

Use `CatFlapRuntimeController` from a scene-owned MonoBehaviour adapter.

```csharp
var controller = new CatFlapRuntimeController(boardView, statusView);
CatFlapRuntimeLevelLoadResult result = controller.LoadJson(levelTextAsset.text);

if (result.IsLoaded)
{
    controller.SelectCell(row, column, notesMode);
    controller.ValidateBoard();
}
```

The controller owns:

- `LoadJson`: parse, validate, create board/level mistake state, render views
- `LoadLevel`: create runtime state from already validated `CatFlapLevelData`
- `SelectCell`: tap cycle `Empty -> Fish -> Cat -> Empty`
- `ValidateBoard`: run current rule validation and update win state when full and valid
- `Clear`: restore the current level to initial clues
- `Restart`: reload the current level

## View Mapping

Implement `ICatFlapBoardView` on a Unity adapter that knows the board prefab/container.

- `Build(rows, columns)`: create or reuse visual cells for a square board
- `SetCell(row, column, value, isLocked, isNote)`: update each visual cell
- `row`: zero-based top-to-bottom row index
- `column`: zero-based left-to-right column index
- `value`: `Empty`, `Fish`, or `Cat`
- `isLocked`: initial clue or completed locked line
- `isNote`: notes-mode value; validation treats this as empty

Implement `ICatFlapStatusView` on a Unity adapter that knows UI labels/meters/popups.

- `SetLevel(id, name, difficulty)`: display level metadata
- `SetLevelMistakes(remainingMistakes, maxMistakes)`: update per-level mistake limit UI
- `SetState(state)`: reflect `Uninitialized`, `Initializing`, `Playing`, `Resolving`, `Won`, or `Failed`
- `ShowRuleResult(result)`: show validation feedback or clear it when valid
- `ShowLoadErrors(errors)`: show invalid JSON or invalid level data feedback

No prefab paths, scene objects, Inspector assignments, or Level Editor tools are created by this phase.

## Phase 4 Adapter Setup

Phase 4 adds attachable MonoBehaviour adapters in `Assets/0_Game/CatFlap/Scripts/Gameplay/UnityAdapters`.

Detailed scene and prefab requirements are documented in `Assets/0_Game/CatFlap/Docs/CatFlapScenePrefabRequirements.md`.

Scene-owned objects to create and wire in Unity:

- `CatFlapGameplayManager`: assign the game config JSON, sample or production level JSON, board view, status view, UI flow manager, animation view, progress save manager, and current-board save manager.
- `CatFlapUIFlowManager`: assign the shared `GameFlowViewAdapter` and win page `NextButton` root.
- `CatFlapGameProgressSaveManager`: owns saved current-level index.
- `CatFlapCurrentLevelBoardSaveManager`: owns saved board values for the active level.
- `CatFlapBoardViewAdapter`: assign a cell container transform and a `CatFlapCellViewAdapter` prefab.
- `CatFlapCellViewAdapter`: assign a `Button`, optional `Image`, and optional `TMP_Text`.
- `CatFlapStatusViewAdapter`: assign TMP labels for level mistake limit, state, notes, and feedback as needed.
- `CatFlapBoosterManager`: assign on a separate non-Canvas manager object.

Button hooks:

- Cell buttons are hooked by `CatFlapCellViewAdapter` automatically.
- Notes toggle can call `CatFlapGameplayManager.ToggleNotesMode` or `SetNotesMode`.
- Hint button can call `CatFlapGameplayManager.UseHint`.
- Fix button can call `CatFlapGameplayManager.UseFix`.
- Clear button can call `CatFlapGameplayManager.ClearBoard`.
- Restart button can call `CatFlapGameplayManager.RestartLevel`.
- Manual validation button can call `CatFlapGameplayManager.ValidateNow`.

Phase 5 status labels:

- Moves label is optional and is assigned to `CatFlapStatusViewAdapter.movesLabel`.
- Timer label is optional and is assigned to `CatFlapStatusViewAdapter.timerLabel`.
- Completion label is optional and is assigned to `CatFlapStatusViewAdapter.completionLabel`.
- Timer advances while the runtime state is `Playing`.
- Completion counts non-note filled player cells against non-clue playable cells.

Flow module:

- `CatFlapRuntimeSession.ValidateCurrentBoard` locks completed rows/columns when the full line matches the solution.
- Shared `IGameFlowView` receives mapped state changes for main menu, gameplay, win, and fail page switching.
- Shared `GameFlowViewAdapter` switches `MainMenuUIRoot`, `GamePlayUIRoot`, `LevelWinUIRoot`, and `LevelFailUIRoot`.

Sample level:

- `Assets/0_Game/CatFlap/Data/Json/Levels/CatFlapSampleLevel01.json`

Delayed validation:

- `CatFlapGameplayManager.validationDelaySeconds` defaults to `0.75`.
- Normal taps schedule delayed validation.
- Notes-mode taps do not spend the level mistake limit or validate as mistakes.
- Wrong non-note values spend one level mistake, stay visible for Fix booster correction, and show mistake feedback.
- Global Lives/Energy is reserved for future fail/entry economy and is not currently spent by wrong answers.
