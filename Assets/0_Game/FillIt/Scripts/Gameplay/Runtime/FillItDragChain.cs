using System.Collections.Generic;
using FillIt.Models;

namespace FillIt.Runtime
{
    /// <summary>
    /// The drag-to-fill gesture. Every drag builds a chain of orthogonally connected cells, applied
    /// to the board live while the finger moves:
    ///  - Start on an empty cell: "count" mode. The chain shows its own length (drag across 3 cells
    ///    and all three read 3), up to 9.
    ///  - Start on a number: the chain takes that number.
    ///  - Crossing a given (clue) number: the whole chain switches to it.
    ///  - Crossing a number the player entered: if the chain is still counting it adopts that
    ///    number; otherwise the cell joins the chain and takes the chain's number.
    ///  - Given numbers are never changed.
    ///  - Dragging back onto an earlier cell of the chain unselects every cell after it (their
    ///    original numbers come back and the chain's number is recalculated); dragging back onto
    ///    the given number the drag started from unselects the whole chain. A diagonal move steps
    ///    through a shared corner cell so the chain stays connected.
    /// A drag that never leaves its first cell is a tap: an empty cell becomes 1, a player number
    /// is cleared.
    /// </summary>
    public sealed class FillItDragChain
    {
        public const int MaxCountLength = 9;

        private readonly List<int> path = new List<int>();
        private readonly Dictionary<int, int> originalValues = new Dictionary<int, int>();
        private readonly Dictionary<int, bool> originalHinted = new Dictionary<int, bool>();
        private readonly Dictionary<int, int> cameFrom = new Dictionary<int, int>();
        // Chain number (and the given it came from) at the moment each path cell joined - what the
        // chain goes back to when the drag retracts to that cell.
        private readonly List<(int value, int cell)> lockHistory = new List<(int value, int cell)>();
        private int initialLockValue;
        private int initialLockCell = -1;
        private FillItBoardModel board;
        private ISet<int> allowedCells;

        public bool IsActive { get; private set; }
        public int StartIndex { get; private set; } = -1;
        public int StartValue { get; private set; }
        public bool Moved { get; private set; }
        public int LastIndex { get; private set; } = -1;
        /// <summary>The chain's number, or 0 while it is still counting its own length.</summary>
        public int LockedValue { get; private set; }
        /// <summary>The given cell the chain took its number from, or -1.</summary>
        public int LockCell { get; private set; } = -1;
        public IReadOnlyList<int> Path => path;

        public bool TryGetCameFrom(int index, out int from)
        {
            return cameFrom.TryGetValue(index, out from);
        }

        /// <param name="allowed">Optional whitelist (FTUE guided moves) - empty or player cells outside it can't join.</param>
        public void Begin(FillItBoardModel targetBoard, int index, ISet<int> allowed = null)
        {
            Reset();
            board = targetBoard;
            allowedCells = allowed;
            IsActive = true;
            StartIndex = index;
            LastIndex = index;
            StartValue = board.GetValue(index);
            bool given = board.IsGiven(index);
            if (!given)
            {
                path.Add(index);
                originalValues[index] = StartValue;
                originalHinted[index] = board.IsHinted(index);
            }

            LockedValue = StartValue;
            LockCell = given ? index : -1;
            initialLockValue = LockedValue;
            initialLockCell = LockCell;
            if (!given)
            {
                lockHistory.Add((LockedValue, LockCell));
            }
        }

        /// <summary>Feeds the next distinct cell under the finger. Returns true when any board value changed.</summary>
        public bool Enter(int index)
        {
            if (!IsActive || board == null || !board.IsValidIndex(index) || index == LastIndex)
            {
                return false;
            }

            int size = board.Size;
            int lastRow = LastIndex / size;
            int lastColumn = LastIndex % size;
            int row = index / size;
            int column = index % size;
            bool changed = false;
            if (System.Math.Abs(lastRow - row) == 1 && System.Math.Abs(lastColumn - column) == 1)
            {
                // Cut across a corner: step through a shared neighbour so the chain stays connected.
                int optionA = lastRow * size + column;
                int optionB = row * size + lastColumn;
                changed |= EnterStep(CanPassThrough(optionA) || !CanPassThrough(optionB) ? optionA : optionB);
            }

            changed |= EnterStep(index);
            return changed;
        }

        /// <summary>
        /// Finishes the gesture and returns every cell whose value it changed (tap rules included).
        /// The board already holds the final values.
        /// </summary>
        public List<FillItCellChange> End(out FillItMoveKind kind)
        {
            var changes = new List<FillItCellChange>();
            kind = FillItMoveKind.None;
            if (!IsActive || board == null)
            {
                Reset();
                return changes;
            }

            if (!Moved && StartIndex >= 0 && !board.IsGiven(StartIndex))
            {
                bool wasHinted = board.IsHinted(StartIndex);
                if (StartValue != FillItBoardModel.EmptyValue)
                {
                    board.SetValue(StartIndex, FillItBoardModel.EmptyValue);
                    changes.Add(new FillItCellChange(StartIndex, StartValue, FillItBoardModel.EmptyValue, wasHinted));
                    kind = FillItMoveKind.TapClear;
                }
                else if (allowedCells == null || allowedCells.Contains(StartIndex))
                {
                    board.SetValue(StartIndex, 1);
                    changes.Add(new FillItCellChange(StartIndex, FillItBoardModel.EmptyValue, 1, wasHinted));
                    kind = FillItMoveKind.TapPlaceOne;
                }

                Reset();
                return changes;
            }

            for (int index = 0; index < path.Count; index++)
            {
                int cell = path[index];
                int before = originalValues[cell];
                int after = board.GetValue(cell);
                if (before != after)
                {
                    changes.Add(new FillItCellChange(cell, before, after, originalHinted[cell]));
                }
            }

            kind = changes.Count > 0 ? FillItMoveKind.Drag : FillItMoveKind.None;
            Reset();
            return changes;
        }

        /// <summary>Abandons the gesture and puts every touched cell back the way it was.</summary>
        public void Cancel()
        {
            if (IsActive && board != null)
            {
                foreach (KeyValuePair<int, int> entry in originalValues)
                {
                    board.SetValue(entry.Key, entry.Value);
                    board.SetHinted(entry.Key, originalHinted[entry.Key]);
                }
            }

            Reset();
        }

        private bool CanPassThrough(int index)
        {
            return path.Contains(index) || (!board.IsGiven(index) && board.IsEmpty(index));
        }

        private bool EnterStep(int index)
        {
            int previous = LastIndex;
            LastIndex = index;
            Moved = true;
            bool adjacentToPrevious = IsNeighbor(index, previous);

            // Dragging back onto an earlier cell of the chain unselects every cell after it.
            int position = path.IndexOf(index);
            if (position >= 0)
            {
                if (position == path.Count - 1)
                {
                    // Stepping back off a given number onto the chain's end undoes the switch to it.
                    (int value, int cell) joined = lockHistory[position];
                    if (previous == LockCell && board.IsValidIndex(previous) && board.IsGiven(previous) &&
                        (joined.value != LockedValue || joined.cell != LockCell))
                    {
                        LockedValue = joined.value;
                        LockCell = joined.cell;
                        ApplyChainValue();
                        return true;
                    }

                    return false;
                }

                TruncateTo(position + 1);
                ApplyChainValue();
                return true;
            }

            // Back onto the given number the drag started from: unselect the whole chain.
            if (index == StartIndex && path.Count > 0)
            {
                TruncateTo(0);
                return true;
            }

            if (board.IsGiven(index))
            {
                // Given number: the chain switches to it.
                if (!adjacentToPrevious)
                {
                    return false;
                }

                LockCell = index;
                if (LockedValue == board.GetValue(index))
                {
                    return false;
                }

                LockedValue = board.GetValue(index);
            }
            else if (!board.IsEmpty(index))
            {
                // Player-entered number: adopt it if still counting, then it joins the chain.
                if (!adjacentToPrevious || !IsAllowed(index))
                {
                    return false;
                }

                if (LockedValue == FillItBoardModel.EmptyValue)
                {
                    LockedValue = board.GetValue(index);
                }

                Join(index, previous);
            }
            else if (adjacentToPrevious && (LockedValue != FillItBoardModel.EmptyValue || path.Count < MaxCountLength) && IsAllowed(index))
            {
                Join(index, previous);
            }
            else
            {
                return false;
            }

            return ApplyChainValue();
        }

        private void Join(int index, int previous)
        {
            path.Add(index);
            originalValues[index] = board.GetValue(index);
            originalHinted[index] = board.IsHinted(index);
            cameFrom[index] = previous;
            lockHistory.Add((LockedValue, LockCell));
        }

        // Drops every chain cell from position keepCount on, putting their original numbers back.
        private void TruncateTo(int keepCount)
        {
            for (int index = path.Count - 1; index >= keepCount; index--)
            {
                int cell = path[index];
                board.SetValue(cell, originalValues[cell]);
                board.SetHinted(cell, originalHinted[cell]);
                originalValues.Remove(cell);
                originalHinted.Remove(cell);
                cameFrom.Remove(cell);
                path.RemoveAt(index);
                lockHistory.RemoveAt(index);
            }

            (int value, int cell) state = keepCount > 0 ? lockHistory[keepCount - 1] : (initialLockValue, initialLockCell);
            LockedValue = state.value;
            LockCell = state.cell;
        }

        private bool ApplyChainValue()
        {
            int length = path.Count;
            int value = LockedValue != FillItBoardModel.EmptyValue ? LockedValue : (length >= 2 ? length : 0);
            bool changed = false;
            for (int index = 0; index < path.Count; index++)
            {
                int cell = path[index];
                int target = value != 0 ? value : originalValues[cell];
                if (board.GetValue(cell) != target)
                {
                    board.SetValue(cell, target);
                    if (target == originalValues[cell])
                    {
                        board.SetHinted(cell, originalHinted[cell]);
                    }

                    changed = true;
                }
            }

            return changed;
        }

        private bool IsAllowed(int index)
        {
            return allowedCells == null || allowedCells.Contains(index);
        }

        private bool IsNeighbor(int a, int b)
        {
            if (a < 0 || b < 0)
            {
                return false;
            }

            int[] neighbors = board.Neighbors(a);
            for (int index = 0; index < neighbors.Length; index++)
            {
                if (neighbors[index] == b)
                {
                    return true;
                }
            }

            return false;
        }

        private void Reset()
        {
            path.Clear();
            lockHistory.Clear();
            initialLockValue = 0;
            initialLockCell = -1;
            originalValues.Clear();
            originalHinted.Clear();
            cameFrom.Clear();
            IsActive = false;
            Moved = false;
            StartIndex = -1;
            StartValue = 0;
            LastIndex = -1;
            LockedValue = 0;
            LockCell = -1;
            allowedCells = null;
        }
    }
}
