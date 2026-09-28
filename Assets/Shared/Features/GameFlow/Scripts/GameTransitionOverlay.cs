using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.GameFlow
{
    /// <summary>
    /// Fake-loading cover used to smooth out instant panel swaps (menu/gameplay/win/fail/
    /// out-of-lives all flip GameObjects active on the same frame with nothing in between).
    /// Fades to opaque, hides the old page once fully covered, then reveals the new page and
    /// fades back out - the new page's own entry animation plays underneath, in parallel with
    /// the fade-out, rather than after it.
    ///
    /// Prefer authoring <see cref="PrefabResourcePath"/> as a real prefab (swap the background
    /// art, add extra decoration, tweak the CanvasGroup) - <see cref="Instance"/> loads and
    /// instantiates it from Resources when present. Use MangraGames > Build Game Transition
    /// Overlay Prefab to (re)generate a starting-point prefab there from the code-built default
    /// below. If no prefab exists yet (fresh checkout, or it was deleted), falls back to building
    /// the same default layout entirely in code so nothing breaks.
    /// </summary>
    public sealed class GameTransitionOverlay : MonoBehaviour
    {
        private static GameTransitionOverlay instance;

        // Resources-relative path (no extension) - Resources.Load only finds assets that live
        // under a "Resources" folder.
        public const string PrefabResourcePath = "GameFlow/GameTransitionOverlayRoot";

        public static GameTransitionOverlay Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = CreateFromPrefabOrDefault();
                }

                return instance;
            }
        }

        /// <summary>
        /// True from the moment a Play() fade-in starts until its fade-out finishes. Checked by
        /// CatFlapGameplayManager.Update() to stop Tick() (and the RenderStatus -> ShowState
        /// re-assertion it triggers every frame) from running while a transition is covering the
        /// screen - otherwise, e.g., GameState.Current still reading Won during the hold before
        /// revealNewPage runs makes RenderStatus call ShowState(Win) every frame, which
        /// re-activates winRoot and undoes hideOldPage's deactivation on the very next frame.
        /// Deliberately doesn't touch <see cref="Instance"/> (which lazily creates the overlay) -
        /// checking this should never itself spawn one.
        /// </summary>
        public static bool IsTransitioning => instance != null && instance.routine != null;

        private static GameTransitionOverlay CreateFromPrefabOrDefault()
        {
            GameTransitionOverlay prefab = Resources.Load<GameTransitionOverlay>(PrefabResourcePath);
            GameTransitionOverlay created = prefab != null
                ? Instantiate(prefab)
                : new GameObject(nameof(GameTransitionOverlay)).AddComponent<GameTransitionOverlay>();

            created.gameObject.name = nameof(GameTransitionOverlay);
            DontDestroyOnLoad(created.gameObject);
            return created;
        }

        // Resources-relative path (no extension) - Resources.Load only finds assets that live
        // under a "Resources" folder, which is why this is a copy of the boot splash art
        // (Assets/Shared/Features/GameFlow/Art/Sprites/Game_Splash/Loading_Screen.png) rather
        // than a reference to that asset directly.
        private const string BackgroundResourcePath = "GameFlow/TransitionLoadingScreen";

        [SerializeField] private float fadeInSeconds = 0.2f;
        // A pause BEFORE revealNewPage (see PlayRoutine) - never between it and the fade-out, so
        // this can be tuned freely without ever eating into the reveal animation's visible time.
        [SerializeField] private float holdSeconds = 0f;
        [SerializeField] private float fadeOutSeconds = 0.25f;
        // Falls back to this flat color if the background sprite fails to load, so the overlay
        // never renders as a see-through/broken cover.
        [SerializeField] private Color fallbackColor = Color.black;

        // Assigned automatically by the runtime fallback build; assign these in the Inspector
        // instead when authoring PrefabResourcePath as a real prefab, so this script just drives
        // whatever CanvasGroup/hierarchy the prefab provides rather than building its own.
        [SerializeField] private CanvasGroup canvasGroup;

        private Coroutine routine;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            if (canvasGroup == null)
            {
                BuildUI();
            }
        }

        // Only runs when this instance wasn't built from a prefab (canvasGroup unassigned) -
        // reproduces the same default layout the "Build Game Transition Overlay Prefab" editor
        // command bakes into PrefabResourcePath, so behavior is identical either way.
        private void BuildUI()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            gameObject.AddComponent<CanvasScaler>();
            gameObject.AddComponent<GraphicRaycaster>();

            canvasGroup = gameObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;

            GameObject backgroundGO = new GameObject("Background", typeof(RectTransform));
            backgroundGO.transform.SetParent(transform, false);
            RectTransform rect = backgroundGO.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = backgroundGO.AddComponent<Image>();
            Sprite backgroundSprite = Resources.Load<Sprite>(BackgroundResourcePath);
            if (backgroundSprite != null)
            {
                image.sprite = backgroundSprite;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
            }
            else
            {
                image.color = fallbackColor;
            }
        }

        /// <summary>
        /// Fades to opaque; once fully covered, invokes <paramref name="hideOldPage"/> (guaranteed
        /// hidden from view), optionally holds, then invokes <paramref name="revealNewPage"/> and
        /// starts fading back out in that SAME beat - <paramref name="revealNewPage"/> is expected
        /// to kick off the new page's own entry animation (board enter animation, home slide-in,
        /// etc.) as fire-and-forget, so that animation plays out underneath while this overlay
        /// fades away, instead of finishing hidden behind a still-opaque cover before the fade-out
        /// ever reveals it. <paramref name="holdSeconds"/> deliberately sits BEFORE
        /// <paramref name="revealNewPage"/>, never between it and the fade-out, so tuning that
        /// value can never eat into the reveal animation's visible time. Invokes
        /// <paramref name="onComplete"/> once the fade-out finishes.
        /// Ignored while a transition is already in flight - e.g. a double-tapped Play/Retry
        /// button - since running this again would re-invoke <paramref name="hideOldPage"/> /
        /// <paramref name="revealNewPage"/> a second time on top of the first. Callers are
        /// expected to also guard their own trigger (see MainMenuHandler.RequestPlay's
        /// playRequested flag) so a double-tap never reaches here in the first place - relying on
        /// this alone would still leave the button visibly clickable/interactable for the whole
        /// fade-in.
        /// </summary>
        public void Play(Action hideOldPage, Action revealNewPage, Action onComplete = null)
        {
            if (routine != null)
            {
                return;
            }

            routine = StartCoroutine(PlayRoutine(hideOldPage, revealNewPage, onComplete));
        }

        private IEnumerator PlayRoutine(Action hideOldPage, Action revealNewPage, Action onComplete)
        {
            canvasGroup.blocksRaycasts = true;

            yield return Fade(0f, 1f, fadeInSeconds);

            hideOldPage?.Invoke();

            if (holdSeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(holdSeconds);
            }

            revealNewPage?.Invoke();
            yield return Fade(1f, 0f, fadeOutSeconds);

            canvasGroup.blocksRaycasts = false;
            routine = null;
            onComplete?.Invoke();
        }

        private IEnumerator Fade(float from, float to, float duration)
        {
            duration = Mathf.Max(0.01f, duration);
            float elapsed = 0f;
            canvasGroup.alpha = from;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(from, to, elapsed / duration);
                yield return null;
            }

            canvasGroup.alpha = to;
        }
    }
}
