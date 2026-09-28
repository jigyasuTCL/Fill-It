# Cat Flap Scene And Prefab Requirements

This document translates the Phase 4 adapter code into Unity scene and prefab requirements. It does not require Level Editor tooling. The board must be world-space sprites; only page UI lives on Canvas.

## Scene Requirement

Create or update a gameplay scene with these scene-owned objects.

### Boot Scene

Object: `BootLoader`

Required component:

- `Shared.Boot.BootLoader`

Inspector assignments:

- `Game Config`: assign `Assets/Shared/Features/GameConfig/Data/GameConfig.asset`.
- `Player Data File Name`: use `player-data.json`.
- `Studio Loading Image Root`: assign the first loading image root.
- `Game Splash Image Root`: assign the second loading image root.
- `Studio Loading Seconds`: `2`.
- `Game Splash Seconds`: `2`.
- `Next Scene Name`: `GamePlay`.

Scene generation:

- Run `MangraGames > Build Boot Scene`.
- Replace the fallback `Image` visuals under `StudioLoadingImageRoot` and `GameSplashImageRoot` with production sprites.
- Confirm build settings list `BootScene` before `GamePlay`.

### Runtime Root

Object: `CatFlapGameManager/GameplayManager`

Required component:

- `CatFlapGameplayManager`

Inspector assignments:

- `Game Config`: assign `Assets/Shared/Features/GameConfig/Data/GameConfig.asset`.
- `Level Json`: optional fallback. Assign `Assets/0_Game/CatFlap/Data/Json/Levels/CatFlapSampleLevel01.json` for single-level smoke testing if `Level Sequence` is empty.
- `Level Sequence`: assign production level `TextAsset`s in order, from `level_001.json` through `level_100.json`. The scene builder populates this automatically.
- `Board View`: assign the scene object with `CatFlapSpriteBoardViewAdapter`.
- `Status View`: assign the scene object with `CatFlapStatusViewAdapter`.
- `UI Flow Manager`: assign the scene object with `CatFlapUIFlowManager`.
- `Animation View`: optional but recommended. Assign the scene object with `CatFlapAnimationViewAdapter`. The scene builder creates `AnimationFlow` and assigns it automatically.
- `Progress Save Manager`: assign `CatFlapGameManager/GameProgressSaveManager`.
- `Board Save Manager`: assign `CatFlapGameManager/CurrentLevelBoardSaveManager`.
- `Validation Delay Seconds`: Cat Flap-specific gameplay timing; use `0.75` unless design explicitly chooses another value.
- `Load On Start`: enabled for smoke testing.
  - Generated Phase 9 scenes set this disabled so `MainMenuUIRoot` appears first.

### Main Menu Root

Object: `CatFlapCanvas/MainMenuUIRoot`

Required UI controls:

- Play button.

Button event hooks:

- Play: `CatFlapGameplayManager.StartGame`

### Board Root

Object: `BoardRoot`

Required component:

- `CatFlapSpriteBoardViewAdapter`

Inspector assignments:

- `Cell Container`: assign the transform that will hold generated cell instances.
- `Cell Prefab`: assign the `CatFlapSpriteCell` prefab described below.
- `Cell Spacing`: start with `1.15`.

Recommended child:

- `CellContainer`: a layout container for board cells.

Layout requirement:

- Use world-space transforms, not Canvas UI layout, for puzzle cells.
- Use `SpriteRenderer` visuals for board cells.
- Use 2D colliders for cell selection.
- The adapter instantiates cells in row-major order.
- Rows are zero-based from top to bottom.
- Columns are zero-based from left to right.

### Status Root

Object: `GamePlayUIRoot/StatusRoot`

Required component:

- `CatFlapStatusViewAdapter`

Inspector assignments:

- `Level Label`: TMP label for level name/difficulty.
- `Lives Label`: TMP label currently displays `remaining/max` level mistakes.
- `State Label`: TMP label for runtime state.
- `Notes Label`: TMP label for notes on/off state.
- `Feedback Label`: TMP label for load errors, rule errors, and mistake feedback.

### Controls Root

Object: `GamePlayUIRoot/ControlsRoot`

Required UI controls:

- Notes toggle button.
- Hint button.
- Fix button.
- Clear button.
- Restart button.
- Optional manual validate button for debugging.

Button event hooks:

- Notes toggle: `CatFlapGameplayManager.ToggleNotesMode`
- Hint: `CatFlapGameplayManager.UseHint`
- Fix: `CatFlapGameplayManager.UseFix`
- Clear: `CatFlapGameplayManager.ClearBoard`
- Restart: `CatFlapGameplayManager.RestartLevel`
- Manual validate: `CatFlapGameplayManager.ValidateNow`
- Win page Next: `CatFlapGameplayManager.LoadNextLevel`
- Win/fail page Menu: `CatFlapGameplayManager.ShowMainMenu`

### Animation Flow

Object: `CatFlapGameManager/AnimationFlow`

Required component:

- `CatFlapAnimationViewAdapter`

Inspector assignments:

- No object references are required for the default adapter.
- Optional fallback delays can be set for cell changes, row/column locks, mistakes, boosters, and page-state transitions.

Behavior:

- Gameplay input is blocked while an animation command is active.
- Restart, clear, and level loading cancel active animation sequencing.
- Duplicate completion callbacks are ignored by the runtime sequencer.
- Developers can replace or extend this adapter with real animations, VFX, and SFX while preserving the `ICatFlapAnimationView` contract.

## Prefab Requirement

### CatFlapSpriteCell Prefab

Prefab name: `CatFlapSpriteCell`

Required component:

- `CatFlapSpriteCellViewAdapter`

Required child/components:

- `SpriteRenderer` for background state color.
- `Collider2D`, preferably `BoxCollider2D`, for selection.
- Optional world-space `TextMeshPro` child for fallback symbol text.

Inspector assignments:

- `Background`: assign the `SpriteRenderer` used for state color.
- `Touch Collider`: assign the `Collider2D`.
- `Label`: assign the world-space TMP label used for fallback values.

Adapter behavior:

- `Empty`: blank label.
- `Fish`: fallback label `F`.
- `Cat`: fallback label `C`.
- Notes-mode value: fallback label prefixed with `?`.
- Locked clue cells are non-interactable.

Visual production expectation:

- Art/animation can replace fallback text with fish/cat sprites or animation states.
- Keep the `CatFlapCellViewAdapter` as the data receiver.
- Do not make gameplay decisions in the prefab view script.

## Smoke Test

The scene and prefab can be generated by opening Unity and running:

- `MangraGames > Build Gameplay Scene Objects`

This uses the existing `CatFlapSpriteCell` prefab when present. It creates a fallback prefab only if the asset is missing.

It also uses existing Phase 6 UI page prefabs when present. Missing page prefabs are created as fallback assets:

- `Assets/Shared/Features/GameFlow/Prefabs/UI/GamePlayUIRoot.prefab`
- `Assets/Shared/Features/GameFlow/Prefabs/UI/LevelWinUIRoot.prefab`
- `Assets/Shared/Features/GameFlow/Prefabs/UI/LevelFailUIRoot.prefab`

Win/fail page prefabs are upgraded non-destructively if missing expected children:

- `Background`
- `TitleLabel`
- `RestartButton`
- `NextButton` on `LevelWinUIRoot`
- `MainMenuButton` on win/fail pages

The builder wires `RestartButton` on instantiated win/fail pages to `CatFlapGameplayManager.RestartLevel`.
The builder wires `NextButton` on the instantiated win page to `CatFlapGameplayManager.LoadNextLevel`.
The builder wires `MainMenuButton` on instantiated win/fail pages to `CatFlapGameplayManager.ShowMainMenu`.

This creates or updates:

- `Assets/0_Game/CatFlap/Prefabs/Cells/CatFlapSpriteCell.prefab`
- `Assets/0_Game/CatFlap/Art/Sprites/Cells/CatFlapWhiteCell.png`
- `CatFlapCanvas`
- `GamePlayUIRoot`
- `MainMenuUIRoot`
- `LevelWinUIRoot`
- `LevelFailUIRoot`
- `CatFlapGameManager`
- `GameFlow`
- `AnimationFlow`
- `GameplayManager`
- `BoardRoot`
- `CellContainer`
- `StatusRoot`
- `ControlsRoot`
- `EventSystem`

`CatFlapCanvas` contains page roots only. Gameplay UI must live under the single `GamePlayUIRoot` RectTransform so the whole page can receive exit/entry animations. Board cells, runtime objects, and helper objects must not live under Canvas.

`CatFlapGameManager` contains non-UI helper objects:

- `GameplayManager`
- `UIFlowManager`
- `GameProgressSaveManager`
- `CurrentLevelBoardSaveManager`
- `GameFlow`
- `AnimationFlow`

`GameFlow` owns the shared `GameFlowViewAdapter` and references:

- `GamePlayUIRoot`
- `LevelWinUIRoot`
- `LevelFailUIRoot`

It switches page roots active/inactive by flow state. This shared flow module is intentionally outside Cat Flap so another gameplay module can reuse the same menu/gameplay/win/fail UI shell.

`AnimationFlow` owns `CatFlapAnimationViewAdapter` and receives runtime sequencing commands for cell changes, locks, mistakes, boosters, and page-state transitions.

1. Assign `Assets/0_Game/CatFlap/Data/Json/Levels/CatFlapSampleLevel01.json` to `CatFlapGameplayManager.Level Json`.
2. Enter Play Mode.
3. Confirm the board builds as `4x4`.
4. Confirm four clue cells are locked:
   - `(0,0)` Fish
   - `(0,3)` Cat
   - `(3,0)` Cat
   - `(3,3)` Fish
5. Tap an empty cell three times and confirm cycle:
   - `Empty -> Fish -> Cat -> Empty`
6. Toggle notes mode and tap an empty cell.
7. Confirm notes-mode values do not spend level mistakes after the validation delay.
8. Enter a wrong normal value and wait `0.75s`.
9. Confirm one life is spent, the wrong value stays in the cell, and feedback updates.
10. Press Clear and confirm the initial clues remain while player cells reset.
11. Press Restart and confirm level mistakes reset to the configured limit.
12. Confirm `CatFlapGameplayManager.Level Sequence` contains `level_001` through `level_100` after running the builder.
13. Complete `level_001`, confirm the win page appears, click Next, and confirm `level_002` loads.
14. Press Restart on `level_002` and confirm it reloads `level_002`.
15. Set a non-zero delay on `AnimationFlow`, tap rapidly, and confirm duplicate input does not resolve while sequencing is active.
16. Restart during an active delay and confirm the board is reset without stale callbacks.
17. Confirm Play Mode starts at `MainMenuUIRoot`.
18. Click Play and confirm `level_001` loads.
19. Complete a level, click Menu on the win page, and confirm `MainMenuUIRoot` returns.
20. Confirm board taps do not change gameplay while main menu, win, or fail pages are active.

## Out Of Scope

- Level Editor UI.
- Production art creation.
- Final animation setup.
- Scene camera and lighting polish.
- Store/economy flows.
- Hints, Fix booster, timer, moves, and completion percentage until a later approved phase.
