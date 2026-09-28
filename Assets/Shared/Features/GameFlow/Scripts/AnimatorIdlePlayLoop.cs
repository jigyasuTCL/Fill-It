using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace Shared.GameFlow
{
    /// <summary>
    /// Generic attention-loop for a group of Animators: plays a one-shot "PlayAnimation" state,
    /// waits for it to finish, rests on "Idle" for a random beat, then repeats - for as long as
    /// this component is enabled. Add to any button/menu element and assign its animators; no
    /// other wiring required.
    /// </summary>
    public sealed class AnimatorIdlePlayLoop : MonoBehaviour
    {
        [SerializeField] private string playAnimationStateName = "PlayAnimation";
        [SerializeField] private string idleStateName = "Idle";

        [Tooltip("All animators play/idle together in lockstep.")]
        [SerializeField] private List<Animator> animators = new List<Animator>();

        [Tooltip("Random range (min, max) for how long to rest on Idle between plays.")]
        [SerializeField] private Vector2 idleWaitRange = new Vector2(3f, 6f);

        private Coroutine loopRoutine;

        // True whenever the last SetLocked/StartLoop call wants the loop running - lets OnEnable
        // pick it back up once this GameObject actually finishes activating (see StartLoop).
        private bool wantsToPlay;

        private void OnEnable()
        {
            if (wantsToPlay)
            {
                StartLoop();
            }
        }

        private void OnDisable()
        {
            if (loopRoutine != null)
            {
                StopCoroutine(loopRoutine);
                loopRoutine = null;
            }
        }

        // Callers (e.g. a main-menu button that should only idle-loop while locked) drive this
        // directly instead of the loop auto-starting on enable - lets one function decide whether
        // the loop should be running right now instead of duplicating that if/else at every call site.
        public void SetLocked(bool isLocked)
        {
            if (isLocked)
            {
                StopLoop();
            }
            else
            {
                StartLoop();
            }
        }

        public void StartLoop()
        {
            wantsToPlay = true;

            // Already running - a caller invoking StartLoop repeatedly (e.g. every hover/frame,
            // or SetLocked(false) firing more than once) must not keep restarting the cycle back
            // at PlayAnimation each time; let the in-flight loop keep running undisturbed.
            if (loopRoutine != null)
            {
                return;
            }

            // A caller (e.g. SetState -> SetLocked) can run mid-activation-cascade of an ancestor,
            // before this GameObject is actually active in the hierarchy yet - StartCoroutine
            // throws in that case. Bail out here and let our own OnEnable (which Unity guarantees
            // to call once this object is genuinely active) start it instead.
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (animators.Count > 0)
            {
                loopRoutine = StartCoroutine(LoopRoutine());
            }
        }

        public void StopLoop()
        {
            wantsToPlay = false;

            if (loopRoutine != null)
            {
                StopCoroutine(loopRoutine);
                loopRoutine = null;
            }
        }

        private IEnumerator LoopRoutine()
        {
            while (true)
            {
                for (int index = 0; index < animators.Count; index++)
                {
                    if (animators[index] != null)
                    {
                        animators[index].Play(playAnimationStateName);
                    }
                }

                // Let Play() take effect before reading the state's length back.
                yield return null;

                float playDuration = 0f;
                for (int index = 0; index < animators.Count; index++)
                {
                    if (animators[index] != null)
                    {
                        playDuration = Mathf.Max(playDuration, animators[index].GetCurrentAnimatorStateInfo(0).length);
                    }
                }
                yield return new WaitForSeconds(playDuration);

                for (int index = 0; index < animators.Count; index++)
                {
                    if (animators[index] != null)
                    {
                        animators[index].Play(idleStateName);
                    }
                }

                float idleSeconds = Random.Range(idleWaitRange.x, idleWaitRange.y);

                yield return new WaitForSeconds(Mathf.Max(0f, idleSeconds));
            }
        }
    }
}
