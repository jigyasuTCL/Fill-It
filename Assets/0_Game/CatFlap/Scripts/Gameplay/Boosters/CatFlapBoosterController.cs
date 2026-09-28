using System;
using System.Collections.Generic;
using CatFlap.Models;
using CatFlap.Runtime;
using Shared.Boosters;

namespace CatFlap.Boosters
{
    public sealed class CatFlapBoosterController
    {
        private readonly Stack<CatFlapUndoMoveState> undoHistory = new Stack<CatFlapUndoMoveState>();
        private BoosterWallet wallet;
        private Func<int> currentLevelNumberProvider;

        public event EventHandler<BoosterUseResult> BoosterUsed = delegate { };

        public void Configure(BoosterWallet boosterWallet, Func<int> currentLevelNumberProvider)
        {
            wallet = boosterWallet;
            this.currentLevelNumberProvider = currentLevelNumberProvider;
        }

        public void ClearUndoHistory()
        {
            undoHistory.Clear();
        }

        // Undo history only ever lives in memory - it is never part of the saved board record,
        // so a restored board (Home->Play resume, or a full Editor Play-mode stop/restart) comes
        // back with every previously-placed cell (right or wrong) but zero undo-history entries
        // for them. Without this, Undo reports "nothing to undo" even though the board clearly
        // shows placed cells. There is no persisted move order to rebuild exactly, so this seeds
        // one entry per undoable cell in a stable top-left-to-bottom-right scan order instead.
        public void RebuildFromBoard(CatFlapRuntimeSession session)
        {
            undoHistory.Clear();
            if (session == null || !session.HasLevel)
            {
                return;
            }

            for (int row = 0; row < session.Board.RowCount; row++)
            {
                for (int column = 0; column < session.Board.ColumnCount; column++)
                {
                    if (IsUndoable(session, row, column))
                    {
                        undoHistory.Push(new CatFlapUndoMoveState(row, column, wasMoveCounted: true));
                    }
                }
            }
        }

        // Undo is free and unlimited: every committed tap (including note taps) is recorded
        // here, and Undo simply reverses the most recent one that isn't already locked away
        // inside a completed row/column. Consecutive taps on the same cell (e.g. cycling
        // Empty -> Fish -> Cat) collapse into a single entry, since undoing always just
        // clears the cell regardless of how many taps it took to get there - without this,
        // reaching Cat costs 2 taps but would need 2 separate (and visually identical) undos.
        public void RecordMove(int row, int column, bool wasMoveCounted)
        {
            if (undoHistory.Count > 0)
            {
                CatFlapUndoMoveState top = undoHistory.Peek();
                if (top.Row == row && top.Column == column)
                {
                    top.AddMove(wasMoveCounted);
                    return;
                }
            }

            undoHistory.Push(new CatFlapUndoMoveState(row, column, wasMoveCounted));
        }

        public bool CanUndo(CatFlapRuntimeController runtimeController)
        {
            CatFlapRuntimeSession session = runtimeController?.Session;
            if (session == null || !session.HasLevel || runtimeController.IsSequencing || !IsUnlocked(session))
            {
                return false;
            }

            foreach (CatFlapUndoMoveState state in undoHistory)
            {
                if (IsUndoable(session, state.Row, state.Column))
                {
                    return true;
                }
            }

            return false;
        }

        public BoosterUseResult UseUndo(CatFlapRuntimeController runtimeController)
        {
            CatFlapRuntimeSession session = runtimeController?.Session;
            if (session == null || !session.HasLevel)
            {
                return Publish(BoosterUseResult.Failed(BoosterType.Undo, "No level is loaded."));
            }

            if (!IsUnlocked(session))
            {
                int unlockLevel = wallet?.Config.GetUnlockLevel(BoosterType.Undo) ?? -1;
                string message = unlockLevel < 0
                    ? "Undo is not active."
                    : $"Undo unlocks at level {unlockLevel}.";
                return Publish(BoosterUseResult.Failed(BoosterType.Undo, message));
            }

            if (runtimeController.IsSequencing)
            {
                // An animation (mistake blink, cell lock, etc.) is still playing - wait rather
                // than popping a move now, since UndoCell would fail and we'd rather the click
                // do nothing visible than silently consume a move for no effect.
                return Publish(BoosterUseResult.Failed(BoosterType.Undo, "Cat Flap is still busy. Try again in a moment."));
            }

            while (undoHistory.Count > 0)
            {
                // Peek first: only pop once we know the move was actually applied. Popping
                // before that would silently lose a move if the runtime can't apply it right
                // now, instead of leaving it to retry.
                CatFlapUndoMoveState candidate = undoHistory.Peek();
                if (!IsUndoable(session, candidate.Row, candidate.Column))
                {
                    // Either that row/column already completed (locked), or the player has
                    // since tapped the cell back to Empty manually - either way there's
                    // nothing left for this entry to usefully reverse. Discard and try the
                    // next one down.
                    undoHistory.Pop();
                    continue;
                }

                if (!runtimeController.UndoCell(candidate.Row, candidate.Column, candidate.MoveCount))
                {
                    return Publish(BoosterUseResult.Failed(BoosterType.Undo, "Cat Flap is still busy. Try again in a moment."));
                }

                undoHistory.Pop();
                return Publish(BoosterUseResult.Used(BoosterType.Undo, "Undo used."));
            }

            return Publish(BoosterUseResult.Failed(BoosterType.Undo, "No move is available to undo."));
        }

        // An entry is only worth undoing if the cell is unlocked AND still holds a value/note -
        // if the player has since tapped it back to Empty manually (or it got locked by
        // completing its row/column), clearing it again would be a no-op with no visible effect.
        // Also guards against stale entries left over from a differently-sized board (e.g. the
        // undo history wasn't cleared across a level switch) - such an entry can never be undone
        // on the current board, so it's treated the same as "nothing left to reverse".
        private static bool IsUndoable(CatFlapRuntimeSession session, int row, int column)
        {
            if (row < 0 || row >= session.Board.RowCount || column < 0 || column >= session.Board.ColumnCount)
            {
                return false;
            }

            CatFlapCellModel cell = session.Board.GetCell(row, column);
            return !cell.IsLocked && (cell.Value != CatFlapCellValue.Empty || cell.IsNote);
        }

        private bool IsUnlocked(CatFlapRuntimeSession session)
        {
            if (wallet == null)
            {
                return false;
            }

            int currentLevelNumber = currentLevelNumberProvider?.Invoke() ?? 1;
            int unlockLevel = wallet.Config.GetUnlockLevel(BoosterType.Undo);
            return unlockLevel >= 0 && currentLevelNumber >= unlockLevel;
        }

        private BoosterUseResult Publish(BoosterUseResult result)
        {
            BoosterUsed?.Invoke(this, result);
            return result;
        }
    }
}
