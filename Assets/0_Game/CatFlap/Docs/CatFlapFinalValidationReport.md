# Cat Flap Final Validation Report

Phase 12 status: implemented by Codex documentation and non-Unity validation. Unity Play Mode testing and HTML/reference comparison still require developer execution.

## Scope

This report summarizes the current Cat Flap Unity implementation, the runtime data contract, integration requirements, test cases, and the final HTML-versus-Unity validation plan.

## Architecture Summary

Cat Flap gameplay is split into a gameplay module and shared flow modules.

- Gameplay-specific logic lives under `Assets/0_Game/CatFlap/Scripts/Gameplay`.
- Shared page flow UI logic lives under `Assets/Shared/Features/GameFlow`.
- Shared lives logic lives under `Assets/Shared/Features/Lives` for future global life/energy scope.
- Runtime rules and board state are plain C# classes.
- Unity-facing classes are adapter MonoBehaviours.
- Board gameplay uses world-space sprite objects.
- Canvas contains UI page roots only.
- Non-UI helpers are under `CatFlapGameManager`.

Main runtime path:

1. `CatFlapGameplayManager` receives Unity button/input calls.
2. `CatFlapRuntimeController` coordinates level loading, player moves, boosters, validation, state, and view rendering.
3. `CatFlapRuntimeSession` owns current level, board, level mistake limit, progress, boosters, and win/fail transitions.
4. `CatFlapRuleValidator` validates Cat/Fish puzzle rules.
5. View interfaces keep gameplay independent from concrete UI, sprite, and flow objects.

## Runtime Level-Data Contract

Level files are JSON `TextAsset`s.

Required fields:

- `size`: positive even board size.
- `solution`: square grid matching `size`; each cell must be `0` or `1`.
- `clues`: square grid matching `size`; each cell may be `-1`, `null`, `0`, or `1`.

Optional fields:

- `id`: level identifier. If missing and `index` is provided, parser derives `level_###`.
- `index`: numeric fallback source for generated ids.
- `name`: display name.
- `difficulty`: display difficulty.
- `lives`: legacy level value; current fail behavior uses shared `GameConfig.LevelMistakeLimit`.

Cell values:

- `-1` or `null` in `clues`: empty playable cell.
- `0`: Fish.
- `1`: Cat.

Validation:

- Clues must match the solution.
- Solution must obey all active Cat Flap board rules.
- Rows and columns cannot have more than two adjacent matching values.
- Rows and columns cannot exceed half Fish or half Cat.
- Full rows and full columns must be unique.

Production sequence:

- `Assets/0_Game/CatFlap/Data/Json/Levels/level_001.json` through `level_100.json`.
- The scene builder assigns these files to `CatFlapGameplayManager.levelSequence` in filename order.

## Implemented Mechanics

- Main menu to gameplay flow.
- Loading `level_001` through `level_100`.
- Win page with next-level loading.
- Fail page.
- Restart current level.
- Cell cycling: `Empty -> Fish -> Cat -> Empty`.
- Notes mode, treated as empty by validation.
- Delayed validation after normal moves.
- Wrong answers spend a life and stay visible.
- Fix booster corrects the first wrong non-note value.
- Hint booster reveals and locks the next empty cell.
- Gem costs for Hint and Fix.
- Row/column lock after a complete valid line.
- Progress tracking for moves, timer, and completion.
- Input blocked during animation sequencing.
- Duplicate same-frame sprite selections blocked.
- Delayed validation and active sequences canceled on restart, clear, level load, destroy, and app pause.
- Completion events guarded against duplicate win/fail firing.

## Public APIs

`CatFlapGameplayManager` public methods for Unity events:

- `StartGame`
- `ShowMainMenu`
- `LoadAssignedLevel`
- `LoadNextLevel`
- `RestartLevel`
- `ClearBoard`
- `ValidateNow`
- `ToggleNotesMode`
- `SetNotesMode`
- `UseHint`
- `UseFix`

`CatFlapRuntimeController` main runtime methods:

- `LoadJson`
- `LoadLevel`
- `ShowMainMenu`
- `SelectCell`
- `ValidateBoard`
- `Clear`
- `Restart`
- `RevealHint`
- `UseFix`
- `ResolveMistake`
- `CancelActiveSequence`
- `Tick`
- `Dispose`

Main events:

- `CatFlapRuntimeController.LevelWon`
- `CatFlapRuntimeController.LevelFailed`
- `CatFlapRuntimeSession.BoardChanged`
- `CatFlapRuntimeSession.StateChanged`
- `CatFlapRuntimeSession.LevelWon`
- `CatFlapRuntimeSession.LevelFailed`
- `CatFlapRuntimeSession.RulesValidated`

## Scene Setup Guide

Open `Assets/0_Game/CatFlap/Scenes/GamePlay.unity`.

Run `MangraGames > Build Gameplay Scene Objects` when scene references need to be generated or repaired.

Expected generated scene roots:

- `CatFlapCanvas`
- `CatFlapGameManager`
- `BoardRoot`
- `EventSystem`

Canvas should contain UI page roots only:

- `MainMenuUIRoot`
- `GamePlayUIRoot`
- `LevelWinUIRoot`
- `LevelFailUIRoot`

`CatFlapGameManager` should contain non-UI helpers:

- `GameplayManager`
- `UIFlowManager`
- `GameProgressSaveManager`
- `CurrentLevelBoardSaveManager`
- `GameFlow`
- `AnimationFlow`

`BoardRoot` should contain world-space board objects only.

## Prefab Setup Guide

Required gameplay prefab:

- `Assets/0_Game/CatFlap/Prefabs/Cells/CatFlapSpriteCell.prefab`

Required shared UI prefabs:

- `Assets/Shared/Features/GameFlow/Prefabs/UI/GamePlayUIRoot.prefab`
- `Assets/Shared/Features/GameFlow/Prefabs/UI/LevelWinUIRoot.prefab`
- `Assets/Shared/Features/GameFlow/Prefabs/UI/LevelFailUIRoot.prefab`

The scene builder creates fallback prefabs only when required prefabs are missing. Existing UI prefabs are repaired non-destructively when expected children are missing.

## Inspector Checklist

`GameplayManager` / `CatFlapGameplayManager`:

- `Level Sequence` contains `level_001` through `level_100`.
- `Board View` references `BoardRoot` with `CatFlapSpriteBoardViewAdapter`.
- `Status View` references `GamePlayUIRoot/StatusRoot`.
- `Game Flow View` references `CatFlapGameManager/GameFlow`.
- `Animation View` references `CatFlapGameManager/AnimationFlow`.
- `Next Level Button Root` references win page `NextButton`.
- `Validation Delay Seconds` is `0.75` unless design chooses another timing.
- `Load On Start` is disabled for menu-first flow.

`BoardRoot` / `CatFlapSpriteBoardViewAdapter`:

- `Cell Container` assigned.
- `Cell Prefab` assigned.
- Input camera assigned or available as the main camera.
- Generated cells use `SpriteRenderer` and `Collider2D`.

`GameFlow` / `GameFlowViewAdapter`:

- Main menu page assigned.
- Gameplay page assigned.
- Win page assigned.
- Fail page assigned.

## HTML-Versus-Unity Validation

Status: blocked until the working HTML/JavaScript reference package or accessible URL is provided in this workspace.

Codex searched the Unity project for local `.html` or `.htm` files and did not find a reference game package. Because the source instructions require the HTML game as the working-behavior authority, final behavior parity cannot be honestly marked passed from repository files alone.

When the HTML reference is available, compare these cases using identical moves:

| Case | Level | Move sequence | Expected comparison |
| --- | --- | --- | --- |
| Simple solve | `level_001` or nearest matching HTML level | Fill all non-clue cells with solution values | Initial clues, lock timing, win state, win page, next button |
| Wrong answer | Same simple level | Place one incorrect normal value and wait validation delay | Wrong value remains, one life lost, feedback shown, Fix can correct |
| Notes mode | Same simple level | Toggle notes and place note values | Notes do not spend level mistakes and validate as empty |
| Row/column lock | Medium level | Complete one correct row and one correct column | Matching lines lock only after valid completion |
| Rule violation | Medium level | Create three adjacent values or too many of one value | Same rule feedback and blocked/allowed timing |
| Booster hint | Medium level | Use Hint once | Same revealed cell policy or documented difference |
| Booster fix | Medium level | Place wrong value, then use Fix | Same correction policy or documented difference |
| Failure | Any level | Spend all level mistakes with wrong normal values | Same fail state and page behavior |
| Restart | Any level | Make moves, use restart | Same level reloads, clues/level mistakes/gems reset as designed |
| Next level | `level_001` to `level_002` | Win, then click Next | Next level loads and gameplay page returns |

Differences should be recorded with severity:

- Critical: blocks solving, changes win/fail, corrupts level data, or prevents input.
- High: changes core rule behavior, level mistake limit, boosters, or level progression.
- Medium: changes timing, lock feedback, page transitions, or non-critical state display.
- Low: wording, temporary fallback visuals, or polish differences.

## Current Difference Log

No confirmed HTML-versus-Unity differences are recorded yet because HTML execution has not been performed in this workspace.

Known implementation decisions that must be compared against HTML:

- Wrong answers remain in the cell after life loss.
- Fix corrects the first wrong non-note value.
- Hint reveals and locks the next empty playable cell.
- Delayed validation default is `0.75` seconds.
- Notes-mode values are ignored by validation.
- Win/fail events fire once per level load.

## Codex Verification

Passed by automated non-Unity test:

- Focused C# compile for gameplay/shared scripts.
- Parsed and validated all 100 production level JSON files.

Latest result:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
Validated 100 level files. Failures: 0.
```

Not executed by Codex:

- Unity Editor compilation.
- Unity Play Mode.
- Device testing.
- HTML reference execution.
- Pixel/animation parity.

Blocked:

- Final HTML-vs-Unity parity signoff until the reference HTML/JavaScript files or accessible URL are supplied.

## Developer Unity Test Cases

1. Open `Assets/0_Game/CatFlap/Scenes/GamePlay.unity`.
2. Run `MangraGames > Build Gameplay Scene Objects`.
3. Enter Play Mode and confirm the main menu appears first.
4. Click Play and confirm `level_001` loads.
5. Confirm only UI page roots are inside `CatFlapCanvas`.
6. Confirm board cells are under `BoardRoot` and use world-space `SpriteRenderer` objects.
7. Complete `level_001` and confirm win page appears once.
8. Click Next and confirm `level_002` loads.
9. Restart `level_002` and confirm it reloads `level_002`.
10. Place a wrong normal answer and confirm it stays visible after one life is lost.
11. Use Fix and confirm the wrong answer is corrected.
12. Use Hint and confirm a playable cell is revealed and locked.
13. Toggle notes mode and confirm note values do not spend level mistakes.
14. Complete a valid row/column and confirm it locks.
15. Create rule violations and confirm feedback matches the rule.
16. Spend all level mistakes and confirm fail page appears once.
17. Return to the main menu from win/fail.
18. Rapid tap and multi-touch cells; confirm duplicate input does not resolve while sequencing is active.
19. Restart during an active delay and confirm stale callbacks do not alter the new board.
20. Pause/background during delayed validation or animation and confirm gameplay resumes cleanly.
21. Test smallest and largest level sizes present in production JSON.
22. Test common mobile aspect ratios and confirm no UI overlap.
23. Confirm Unity Console has no unexpected errors or repeated event warnings.

## Remaining Manual Work

- Import final production art, animation, VFX, SFX, and UI polish.
- Run Unity Editor compilation and Play Mode tests.
- Run device testing on target platforms.
- Provide HTML/JavaScript reference package or accessible URL.
- Execute the HTML-vs-Unity comparison table.
- Record any confirmed differences and requested behavior decisions.

## Out Of Scope

- Level Editor tooling.
- Store/economy meta screens beyond current booster gem cost handling.
- Production visual asset creation.
- Final animation authoring.
- Analytics, ads, cloud save, and platform services.

## Recommended Next Steps

1. Developer runs Phase 12 Unity test cases.
2. Developer supplies the HTML/JavaScript reference package or confirms the accessible reference URL.
3. Compare selected simple, medium, and complex levels against HTML with identical moves.
4. Update this report with confirmed differences and approvals.
5. After Phase 12 approval, treat Cat Flap gameplay foundation as ready for visual polish, content QA, and platform work.
