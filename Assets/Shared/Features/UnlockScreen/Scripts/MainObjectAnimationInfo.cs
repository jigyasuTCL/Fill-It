using System.Collections.Generic;
using UnityEngine;

namespace Shared.GameFlow
{
    /// <summary>
    /// Lives on an unlock's own mainObjectPrefab (see UnlockData.mainObjectPrefab) - carries
    /// everything about how THAT specific object wants to play its scale-up/animate/scale-down
    /// beat, so different unlocks (different levels/features) can each save their own GameObject
    /// here with its own Animator state name and scale timings, instead of every unlock being
    /// forced through one shared animation recipe. A prefab with no MainObjectAnimationInfo on it
    /// just falls back to UnlockRevealSequencer's own default values.
    /// </summary>
    public sealed class MainObjectAnimationInfo : MonoBehaviour
    {
        [SerializeField] private string stateName = "PlayAnimation";
        [Tooltip("Animator state the main object holds on in between Play Animation bursts.")]
        [SerializeField] private string idleStateName = "Idle";
        [Tooltip("How long the main object stays on the idle state before playing its Play " +
                 "Animation state again.")]
        [SerializeField] private float idleHoldDuration = 2f;
        [SerializeField] private float scaleUpDuration = 0.25f;
        [Tooltip("Multiple of this object's own resting scale reached at the top of the " +
                 "scale-up, before it settles back down to exactly its resting size.")]
        [SerializeField] private float scaleUpOvershoot = 1.15f;
        [SerializeField] private float scaleDownDuration = 0.2f;
        [SerializeField] private List<Animator> Animators = new List<Animator>();

        [Header("Move To Target - optional per-prefab override")]
        [Tooltip("False (default): plain fallback - just replays StateName/IdleStateName (the " +
                 "same pair used for the reveal) during the fly-to-target flight, same as every " +
                 "unlock without this feature (e.g. Spin Wheel). True: switches to the two-pair " +
                 "Transition flow below - StateName/IdleStateName still plays the reveal, but the " +
                 "flight instead plays TransitionStateName once as a bridge, then switches to the " +
                 "second animating/idle pair, ButtonStateName/ButtonIdleStateName (e.g. Daily " +
                 "Challenge's \"turning into a button\" beat).")]
        [SerializeField] private bool useTransitionAnimation = false;

        [Tooltip("Only used when useTransitionAnimation is true. Played once (and waited out) " +
                 "right before the fly-to-target movement starts, bridging from the reveal's " +
                 "StateName/IdleStateName pair into the ButtonStateName/ButtonIdleStateName pair " +
                 "below.")]
        [SerializeField] private string transitionStateName = string.Empty;

        [Tooltip("Only used when useTransitionAnimation is true. Second animating state, played " +
                 "for the fly-to-target flight itself once transitionStateName finishes.")]
        [SerializeField] private string buttonStateName = string.Empty;

        [Tooltip("Only used when useTransitionAnimation is true. Second idle state, looped after " +
                 "buttonStateName finishes for as long as the flight continues. Leave blank to " +
                 "just hold buttonStateName's own last frame instead.")]
        [SerializeField] private string buttonIdleStateName = string.Empty;

        [Tooltip("Canvas units per second this object travels along its curved path to Move To " +
                 "Target's destination - overrides UnlockRevealSequencer's own " +
                 "defaultMoveToTargetSpeed, so different unlocks can fly at different speeds.")]
        [SerializeField] private float moveToTargetSpeed = 1500f;

        [Tooltip("Local scale this object settles to once it arrives at its Move To Target " +
                 "destination - used instead of UnlockRevealSequencer's automatic world-size " +
                 "matching, since a fixed author-chosen scale is more reliable across objects " +
                 "with different padding/art bounds.")]
        [SerializeField] private Vector3 finalScale = Vector3.one;

        [Tooltip("False (default): the flight follows UnlockRevealSequencer's existing bowed " +
                 "side-arc path (DOPath through one curved control point, via moveToTargetArcFactor). " +
                 "True: a classic parabolic toss/jump feel instead (DOJump), arcing straight up and " +
                 "down over arcJumpPower/arcNumJumps rather than bowing sideways.")]
        [SerializeField] private bool useArcMovement = false;

        [Tooltip("Only used when useArcMovement is true. Peak height (canvas units) the object " +
                 "arcs above a straight line to target - matches DOTween's DOJump jumpPower.")]
        [SerializeField] private float arcJumpPower = 300f;

        [Tooltip("Only used when useArcMovement is true. Number of arc bounces during the flight - " +
                 "1 for a single smooth up-and-down toss.")]
        [SerializeField] private int arcNumJumps = 1;

        public string StateName => stateName;
        public string IdleStateName => idleStateName;
        public float IdleHoldDuration => idleHoldDuration;
        public float ScaleUpDuration => scaleUpDuration;
        public float ScaleUpOvershoot => scaleUpOvershoot;
        public float ScaleDownDuration => scaleDownDuration;
        public bool UseTransitionAnimation => useTransitionAnimation;
        public string TransitionStateName => transitionStateName;
        public string ButtonStateName => buttonStateName;
        public string ButtonIdleStateName => buttonIdleStateName;
        public float MoveToTargetSpeed => moveToTargetSpeed;
        public Vector3 FinalScale => finalScale;
        public bool UseArcMovement => useArcMovement;
        public float ArcJumpPower => arcJumpPower;
        public int ArcNumJumps => arcNumJumps;
        public IReadOnlyList<Animator> GetAnimators() => Animators;
    }
}
