using System.Collections;
using System.Collections.Generic;
using FillIt.Runtime;
using Shared.Vibration;
using UnityEngine;

namespace FillIt.UnityAdapters
{
    public partial class FillItGameplayManager
    {
        private Coroutine pendingMistakeRoutine;
        private int pendingMistakeAnchorIndex = -1;
        // Wrong numbers the player placed that are waiting out the grace period (see
        // FillItRuntimeController.FindMistakeCells for what counts).
        private readonly HashSet<int> pendingMistakeCells = new HashSet<int>();

        // A broken region shows red straight away, but a heart is only lost once
        // wrongValidationDelaySeconds have passed with the player's own wrong number still sitting
        // in it - fixing or undoing it inside that grace period is free. Only drags, taps and
        // number-pad entries can cost a heart (never Hint/Fix/Undo), and FTUE levels never do.
        // At most one heart per check, however many wrong numbers are pending.
        // Returns true when the move put down a wrong number that is now waiting out the grace period.
        private bool ScheduleMistakeCheck(FillItMoveResult result)
        {
            // Timer mode: mistakes cost nothing - the clock is the only pressure.
            if (controller == null || !controller.Session.IsPlaying || IsFtueLevelSession() || controller.Session.TimerEnabled)
            {
                return false;
            }

            List<int> cells = controller.FindMistakeCells(result);
            if (cells.Count == 0)
            {
                return false;
            }

            pendingMistakeCells.UnionWith(cells);
            pendingMistakeAnchorIndex = cells[cells.Count - 1];
            RestartMistakeTimer();
            return true;
        }

        // Settings pause: the grace timer stops but the pending wrong numbers are kept, so they're
        // re-checked (with a fresh grace period) when play resumes rather than escaping the charge.
        private void PausePendingMistake()
        {
            if (pendingMistakeRoutine != null)
            {
                StopCoroutine(pendingMistakeRoutine);
                pendingMistakeRoutine = null;
            }
        }

        private void RestartMistakeTimer()
        {
            if (pendingMistakeRoutine != null)
            {
                StopCoroutine(pendingMistakeRoutine);
            }

            pendingMistakeRoutine = pendingMistakeCells.Count > 0 ? StartCoroutine(ResolveMistakeAfterDelay()) : null;
        }

        private IEnumerator ResolveMistakeAfterDelay()
        {
            yield return new WaitForSeconds(Mathf.Max(0f, wrongValidationDelaySeconds));

            // Never charge mid-gesture - the drag's own release reschedules this check.
            while (controller != null && controller.IsDragging)
            {
                yield return null;
            }

            pendingMistakeRoutine = null;
            if (controller == null || !controller.Session.IsPlaying || settingsPopupOpen || outOfLivesPopupOpen)
            {
                yield break;
            }

            var cells = new List<int>(pendingMistakeCells);
            pendingMistakeCells.Clear();
            if (controller.ResolveMistakes(cells, pendingMistakeAnchorIndex))
            {
                VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Heavy);
                PlaySound(global::SoundName.RuleBreakError);
                PlaySound(global::SoundName.HeartLose);
                SaveAll();
            }
        }

        // Cancels a pending heart charge outright - level load/restart/quit/Undo/Fix, where the
        // board is about to change underneath it.
        private void StopPendingMistake()
        {
            if (pendingMistakeRoutine != null)
            {
                StopCoroutine(pendingMistakeRoutine);
                pendingMistakeRoutine = null;
            }

            pendingMistakeCells.Clear();
        }
    }
}
