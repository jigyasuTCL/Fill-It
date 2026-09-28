using UnityEngine;

namespace TLC.SpriteAnimation
{
    /// <summary>
    /// Plays a <see cref="SpriteAnimationSO"/> on a SpriteRenderer by swapping its sprite.
    ///
    /// Three behaviours, all chosen by the clip:
    ///
    ///   Interval    hold frame 0, play once, rest a random delay, repeat. The idle default.
    ///   Continuous  loop forever, no pauses.
    ///   Hold        play up to the clip's hold frame and STAY there. Used by look-at poses,
    ///               which turn the head and keep looking until something else is asked for.
    ///
    /// Handing over from one hold pose to another is not a cut. The new clip does not start at
    /// its frame 0 — the current one first plays BACKWARDS to neutral, then the new one runs
    /// forwards. Author every look-at sheet neutral-to-turned and reversing it is exactly the
    /// return motion, so the head sweeps through centre instead of snapping. Interrupting a turn
    /// mid-way reverses from wherever it is, so rapid direction changes do not stutter.
    ///
    /// This component owns no tuning: everything but the renderer reference lives on the clip.
    ///
    /// Purely cosmetic. Nothing here should ever be read back by game logic — whether it is
    /// enabled, and which frame it is on, must not be observable by anything that matters.
    /// </summary>
    [DisallowMultipleComponent]
    public class SpriteAnimator : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private SpriteRenderer targetRenderer;

        private SpriteAnimationSO clip;
        private float timer;
        private int frame;
        private bool playing;

        // +1 forwards, -1 while reversing a hold pose out to make way for another.
        private int playDirection = 1;

        // Set while reversing: the pose to start once the current one reaches neutral.
        private SpriteAnimationSO pendingClip;

        private Vector3 baseScale = Vector3.one;
        private bool baseScaleCaptured;

        // World width of the STATIC sprite every clip should match. Held explicitly rather than
        // read off the renderer, which shows an animation frame once anything is running.
        private float referenceWidth;

        /// <summary>The clip currently driving the renderer, or null.</summary>
        public SpriteAnimationSO Current => clip;

        /// <summary>
        /// Playback rate multiplier on top of the clip's own frame rate. 1 plays as authored.
        ///
        /// Exists so a clip can be fitted into a time budget the clip itself knows nothing about —
        /// a reaction that has to finish inside a fixed animation window. The alternative, a second
        /// frame rate on the asset, would make the authored one a lie.
        ///
        /// Not serialized: it is a per-instance runtime dial, never a saved property.
        /// </summary>
        [System.NonSerialized] public float SpeedScale = 1f;

        /// <summary>
        /// The renderer's un-normalised scale, i.e. what it measures before a clip stretches it to
        /// match the static sprite.
        ///
        /// Needed because a view can be cloned mid-animation: the clone's transform carries an
        /// already-normalised scale but none of the private state that says so, so it would
        /// normalise a second time and come out visibly wrong. Handing the original's base across
        /// with <see cref="SetBaseScale"/> makes the clone start from the same place.
        /// </summary>
        public Vector3 BaseScale => baseScaleCaptured
            ? baseScale
            : (targetRenderer != null ? targetRenderer.transform.localScale : Vector3.one);

        /// <summary>Adopts a known base scale and snaps the renderer back to it.</summary>
        public void SetBaseScale(Vector3 scale)
        {
            baseScale = scale;
            baseScaleCaptured = true;
            if (targetRenderer != null) targetRenderer.transform.localScale = scale;
        }

        /// <summary>
        /// Declares the sprite whose on-screen size every clip should match — normally the
        /// character's static icon. Call before Play, whenever the displayed content changes.
        /// </summary>
        public void SetSizeReference(Sprite reference)
        {
            referenceWidth = reference != null ? reference.bounds.size.x : 0f;
        }

        /// <summary>
        /// Drives the renderer with this clip, or stops cleanly if it is null or empty.
        ///
        /// Repeating the current clip is ignored, so this is safe to call every frame. Swapping
        /// one hold pose for another starts a reverse-then-forward transition rather than cutting.
        /// </summary>
        public void Play(SpriteAnimationSO animation)
        {
            if (animation == null || !animation.IsValid)
            {
                Stop();
                return;
            }

            // Already showing it, or already on the way to it.
            if (clip == animation && pendingClip == null && enabled) return;
            if (pendingClip == animation) return;

            enabled = true;

            // Hold pose -> different hold pose: sweep out through neutral instead of cutting.
            // Only meaningful between two holds; anything else has its own resting state.
            if (clip != null && clip.holdLastFrame && animation.holdLastFrame && clip != animation)
            {
                pendingClip = animation;
                playDirection = -1;
                playing = true;
                timer = 0f;
                return;
            }

            StartClip(animation);
        }

        private void StartClip(SpriteAnimationSO animation)
        {
            NormaliseScaleTo(animation);

            clip = animation;
            pendingClip = null;
            playDirection = 1;
            frame = 0;

            if (clip.holdLastFrame)
            {
                // A reaction, not ambience: start moving at once.
                if (targetRenderer != null) targetRenderer.sprite = clip.FirstFrame;
                playing = true;
                timer = 0f;
                return;
            }

            ShowRest();
            playing = clip.playContinuously;
            timer = clip.playContinuously ? 0f : RollInterval();
        }

        /// <summary>
        /// Freezes on whatever sprite/scale is currently showing, without resetting either - unlike
        /// <see cref="Stop"/>. For a clip whose final frame is meant to stay on screen permanently
        /// (e.g. a one-shot reaction ending on a terminal state), the last frame belongs to the same
        /// sheet as the rest of the clip and needs its SO-normalised scale, not the pre-animation
        /// base scale.
        /// </summary>
        public void Pause()
        {
            playing = false;
            enabled = false;
        }

        /// <summary>Stops animating and leaves the renderer alone for others to drive.</summary>
        public void Stop()
        {
            if (baseScaleCaptured && targetRenderer != null)
                targetRenderer.transform.localScale = baseScale;

            clip = null;
            pendingClip = null;
            playing = false;
            playDirection = 1;
            enabled = false;
        }

        private void NormaliseScaleTo(SpriteAnimationSO animation)
        {
            if (targetRenderer == null) return;

            if (!baseScaleCaptured)
            {
                baseScale = targetRenderer.transform.localScale;
                baseScaleCaptured = true;
            }

            Sprite target = animation.FirstFrame;
            if (target == null) return;

            float from = referenceWidth > 0.0001f
                ? referenceWidth
                : (targetRenderer.sprite != null ? targetRenderer.sprite.bounds.size.x : 0f);

            float to = target.bounds.size.x;
            if (from <= 0.0001f || to <= 0.0001f) return;

            // Rect-matching alone is not enough: sheet frames are mostly padding while the static
            // sprite is not, so artFitMultiplier carries the measured art-to-art correction.
            targetRenderer.transform.localScale =
                baseScale * (from / to) * Mathf.Max(0.01f, animation.artFitMultiplier);
        }

        private float RollInterval()
        {
            if (clip == null) return 0f;
            float min = Mathf.Max(0f, clip.restBetweenPlays.x);
            float max = Mathf.Max(min, clip.restBetweenPlays.y);
            return Random.Range(min, max);
        }

        private void ShowRest()
        {
            if (targetRenderer != null && clip != null) targetRenderer.sprite = clip.RestFrame;
        }

        private void Update()
        {
            if (clip == null || targetRenderer == null) return;

            float fps = Mathf.Max(0.01f, clip.framesPerSecond * Mathf.Max(0.01f, SpeedScale));
            if (playDirection < 0) fps *= Mathf.Max(0.1f, clip.transitionSpeedMultiplier);

            timer -= Time.deltaTime;

            if (!playing)
            {
                if (timer > 0f) return;
                playing = true;
                frame = 0;
                timer = 0f;
            }

            // Catch up whole frames rather than one per Update, so playback keeps the authored
            // rate on a slow frame instead of stretching.
            float frameTime = 1f / fps;
            while (timer <= 0f)
            {
                if (playDirection < 0)
                {
                    if (frame < 0)
                    {
                        // Back at neutral: hand over to the pose that was waiting.
                        if (pendingClip != null) { StartClip(pendingClip); return; }
                        playDirection = 1;
                        playing = false;
                        frame = 0;
                        if (targetRenderer != null) targetRenderer.sprite = clip.FirstFrame;
                        timer = float.PositiveInfinity;
                        return;
                    }
                }
                else if (frame >= clip.FrameCount || (clip.holdLastFrame && frame > clip.HoldFrame))
                {
                    if (clip.playContinuously)
                    {
                        frame = 0;
                    }
                    else
                    {
                        playing = false;
                        ShowRest();

                        // A hold pose stops for good; anything else waits and replays.
                        timer = clip.holdLastFrame ? float.PositiveInfinity : RollInterval();
                        return;
                    }
                }

                targetRenderer.sprite = clip.frames[Mathf.Clamp(frame, 0, clip.FrameCount - 1)];
                frame += playDirection;
                timer += frameTime;
            }
        }

        private void Reset()
        {
            targetRenderer = GetComponent<SpriteRenderer>();
        }
    }
}
