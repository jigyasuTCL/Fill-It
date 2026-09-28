using System;
using System.Collections.Generic;
using CatFlap.Models;
using CatFlap.Views;
using Shared.GameConfig;
using UnityEngine;

namespace CatFlap.UnityAdapters
{
    public sealed class CatFlapBoardViewAdapter : MonoBehaviour, ICatFlapBoardView
    {
        [SerializeField] private Transform cellContainer;
        [SerializeField] private CatFlapCellViewAdapter cellPrefab;

        private readonly List<CatFlapCellViewAdapter> cells = new List<CatFlapCellViewAdapter>();
        private GameRulesConfig rulesConfig = new GameRulesConfig().CloneNormalized();
        private int columnCount;

        public event Action<int, int> CellSelected = delegate { };

        public void ApplyRulesConfig(GameRulesConfig config)
        {
            rulesConfig = (config ?? new GameRulesConfig()).CloneNormalized();
            for (int index = 0; index < cells.Count; index++)
            {
                cells[index]?.ApplyRulesConfig(rulesConfig);
            }
        }

        public void Build(int rows, int columns)
        {
            columnCount = columns;
            int required = rows * columns;
            EnsureCellCount(required);

            for (int index = 0; index < cells.Count; index++)
            {
                bool active = index < required;
                cells[index].gameObject.SetActive(active);
                if (active)
                {
                    int row = index / columns;
                    int column = index % columns;
                    cells[index].Bind(row, column, HandleCellSelected);
                }
            }
        }

        public void SetCell(int row, int column, CatFlapCellValue value, bool isLocked, bool isNote, bool useLockedColor, CatFlapCellVisualState visualState, bool isGiven, bool suppressValueChangeAnimation = false, bool forcePlayFlip = false, float speedMultiplier = 1f, bool suppressPostFlipPop = false)
        {
            int index = row * columnCount + column;
            if (index < 0 || index >= cells.Count)
            {
                return;
            }

            // No tile-flip animation on this legacy Canvas-UI cell view, so speedMultiplier/
            // suppressPostFlipPop (which only affect flip playback) have nothing to apply to here.
            cells[index].SetState(value, isLocked, isNote, useLockedColor, visualState);
        }

        // This legacy Canvas-UI cell view has no tile-flip animation to stagger or speed up -
        // reveal every cell in every group instantly, in order, at a flat 1x, then complete
        // immediately.
        public void PlayStaggeredReveal(IReadOnlyList<IReadOnlyList<CatFlapCellPosition>> groups, float staggerSeconds, float speedIncreasePerStep, float maxSpeedMultiplier, Action<CatFlapCellPosition, float> revealCell, Action onComplete)
        {
            if (groups != null && revealCell != null)
            {
                for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                {
                    IReadOnlyList<CatFlapCellPosition> group = groups[groupIndex];
                    for (int cellIndex = 0; cellIndex < group.Count; cellIndex++)
                    {
                        revealCell(group[cellIndex], 1f);
                    }
                }
            }

            onComplete?.Invoke();
        }

        // This legacy Canvas-UI cell view's PlayStaggeredReveal above is synchronous/instant -
        // nothing is ever left running to cancel.
        public void CancelStaggeredReveal()
        {
        }

        public void PlayCompletedParticle(int row, int column)
        {
            // Legacy Canvas-UI cell view has no particle FX.
        }

        public void PlayMistakeBlink(int row, int column)
        {
            int index = row * columnCount + column;
            if (index < 0 || index >= cells.Count)
            {
                return;
            }

            cells[index].PlayMistakeBlink();
        }

        // This legacy Canvas-UI cell view has no dedicated scale-pulse effect - reuse the
        // existing mistake blink so the interface call still reads as "something broke" here.
        public void PlayRuleBreakBlink(IReadOnlyList<CatFlapCellPosition> cellsToBlink)
        {
            if (cellsToBlink == null)
            {
                return;
            }

            for (int i = 0; i < cellsToBlink.Count; i++)
            {
                int index = cellsToBlink[i].Row * columnCount + cellsToBlink[i].Column;
                if (index < 0 || index >= cells.Count)
                {
                    continue;
                }

                cells[index].PlayMistakeBlink();
            }
        }

        // This legacy Canvas-UI cell view has no blink/shake mechanism and is not the board wired
        // into the current gameplay scene - the world-space CatFlapSpriteBoardViewAdapter is. Safe
        // no-op so the interface stays satisfied without fabricating visuals here.
        public void PlayWarningBlink(IReadOnlyList<CatFlapCellPosition> cells, bool playShake = true)
        {
        }

        public void StopWarningBlink(int row, int column)
        {
        }

        public void PlayLockedTapShake(int row, int column)
        {
        }

        // This legacy Canvas-UI board view has no world-space FX layer to spawn a HeartBreak
        // prefab into (it's not the board wired into the current gameplay scene). Safe no-op.
        public void PlayHeartBreak(int row, int column)
        {
        }

        // This legacy Canvas-UI board view has no per-cell highlight mechanism (its
        // CatFlapCellViewAdapter never got one) and is not the board wired into the current
        // gameplay scene - the world-space CatFlapSpriteBoardViewAdapter is. Safe no-op so the
        // interface stays satisfied without fabricating highlight visuals here.
        public void ShowHintTarget(int row, int column, CatFlapCellValue value)
        {
        }

        public void ClearHintTarget()
        {
        }

        public bool TryGetCellWorldPosition(int row, int column, out Vector3 position)
        {
            position = default;
            return false;
        }

        public void Clear()
        {
            columnCount = 0;
            for (int index = 0; index < cells.Count; index++)
            {
                if (cells[index] != null)
                {
                    cells[index].gameObject.SetActive(false);
                }
            }
        }

        private void EnsureCellCount(int required)
        {
            if (cellContainer == null || cellPrefab == null)
            {
                return;
            }

            while (cells.Count < required)
            {
                CatFlapCellViewAdapter cell = Instantiate(cellPrefab, cellContainer);
                cell.ApplyRulesConfig(rulesConfig);
                cells.Add(cell);
            }
        }

        private void HandleCellSelected(int row, int column)
        {
            CellSelected?.Invoke(row, column);
        }
    }
}
