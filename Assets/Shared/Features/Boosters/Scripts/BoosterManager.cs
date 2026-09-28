using UnityEngine;
using UnityEngine.Events;
using System;
using System.Collections;
using TMPro;
using Shared.GameConfig;

namespace Shared.Boosters
{
    public sealed class BoosterManager : MonoBehaviour
    {
        [SerializeField] private BoosterButtonUI[] boosterButtons = new BoosterButtonUI[0];
        [SerializeField] private TMP_Text boosterInfoLabel;
        [SerializeField] private float boosterInfoVisibleSeconds = 2f;
        [SerializeField] private float boosterInfoFadeSeconds = 0.35f;
        [SerializeField] private string hintOneMissingLineText = "Hint: completed the row/column with one cell left.";
        [SerializeField] private string hintMaxTwoAdjacentText = "Hint: used the Max 2 Adjacent rule.";
        [SerializeField] private string hintLeastEmptyLineText = "Hint: picked the row/column with fewer empty cells.";
        [SerializeField] private string hintRandomText = "Hint: picked a random available cell.";
        [SerializeField] private string hintFallbackText = "Hint revealed.";

        private BoosterWallet wallet;
        private Func<int> currentLevelNumberProvider;
        private BoosterType lastRequestedBoosterType;
        private int lastRequestFrame = -1;
        private Coroutine boosterInfoRoutine;
        private Color boosterInfoTextColor = Color.white;

        // Set the instant a win is recorded (CatFlapGameplayManager.HandleLevelWon calls
        // SuppressUntilNextConfigure) - currentLevelNumberProvider already reflects the NEXT level
        // at that point (currentLevelIndex advances immediately on win, well before the next level
        // actually loads), so every RefreshAllBoosterUI() call in between (level win HUD refresh,
        // hint/fix/undo, popups closing, etc.) would otherwise repaint booster buttons still on
        // screen with the wrong level's unlock/count state. Cleared by the next real Configure()
        // (a fresh level actually loading), which repaints once, correctly, right there.
        private bool refreshSuppressed;

        public BoosterImplementationEvent HintImplementationRequested = new BoosterImplementationEvent();
        public BoosterImplementationEvent FixImplementationRequested = new BoosterImplementationEvent();
        public BoosterImplementationEvent UndoImplementationRequested = new BoosterImplementationEvent();
        public BoosterLifecycleEvent BoosterImplementationStarted = new BoosterLifecycleEvent();
        public BoosterLifecycleEvent BoosterImplementationCompleted = new BoosterLifecycleEvent();
        public BoosterLifecycleEvent BoosterPurchaseRequested = new BoosterLifecycleEvent();

        public void Configure(BoosterWallet boosterWallet, Func<int> levelNumberProvider)
        {
            wallet = boosterWallet;
            currentLevelNumberProvider = levelNumberProvider;
            refreshSuppressed = false;
            ConfigureButtons();
            RefreshAllBoosterUI();
        }

        // Called once a win is recorded (see field comment on refreshSuppressed) - freezes booster
        // button state exactly as it looked for the level just won until the next level is
        // actually configured, so nothing about them changes while still on the win celebration/
        // popup even though currentLevelNumberProvider already points at the next level.
        public void SuppressUntilNextConfigure()
        {
            refreshSuppressed = true;
        }

        public void ApplyRulesConfig(GameRulesConfig rulesConfig)
        {
            if (rulesConfig == null)
            {
                return;
            }

            GameRulesConfig config = rulesConfig.CloneNormalized();
            boosterInfoVisibleSeconds = config.BoosterInfoVisibleSeconds;
            boosterInfoFadeSeconds = config.BoosterInfoFadeSeconds;
            hintOneMissingLineText = config.HintOneMissingLineText;
            hintMaxTwoAdjacentText = config.HintMaxTwoAdjacentText;
            hintLeastEmptyLineText = config.HintLeastEmptyLineText;
            hintRandomText = config.HintRandomText;
            hintFallbackText = config.HintFallbackText;
        }

        // allowUnlockAnimation defaults to false - only CatFlapGameplayManager.FinishLevelStartEntrance
        // (once the new level's entrance sequence, board startup animation included, has actually
        // finished) should ever pass true. See BoosterButtonUI.Render for why.
        public void RefreshAllBoosterUI(bool allowUnlockAnimation = false)
        {
            if (refreshSuppressed)
            {
                return;
            }

            int currentLevelNumber = currentLevelNumberProvider?.Invoke() ?? 1;
            if (boosterButtons == null)
            {
                return;
            }

            for (int index = 0; index < boosterButtons.Length; index++)
            {
                if (boosterButtons[index] != null)
                {
                    boosterButtons[index].Render(wallet, currentLevelNumber, allowUnlockAnimation);
                }
            }
        }

        public void CheckForUnLockAnimationState()
        {
             int currentLevelNumber = currentLevelNumberProvider?.Invoke() ?? 1;
            if (boosterButtons == null)
            {
                return;
            }
            for (int index = 0; index < boosterButtons.Length; index++)
            {
                if (boosterButtons[index] != null)
                {
                    boosterButtons[index].CheckForUnLockAnimationState(wallet,currentLevelNumber);
                }
            }

        }
        public void SetUndoAvailabilityProvider(Func<bool> provider)
        {
            if (boosterButtons == null)
            {
                return;
            }

            for (int index = 0; index < boosterButtons.Length; index++)
            {
                if (boosterButtons[index] != null && boosterButtons[index].BoosterType == BoosterType.Undo)
                {
                    boosterButtons[index].SetUndoAvailabilityProvider(provider);
                }
            }
        }

        // Lets a caller (e.g. an idle-attention highlight animation on a specific booster button)
        // find and animate that button's own RectTransform without this manager needing to expose
        // the whole boosterButtons array.
        public BoosterButtonUI GetBoosterButton(BoosterType boosterType)
        {
            if (boosterButtons == null)
            {
                return null;
            }

            for (int index = 0; index < boosterButtons.Length; index++)
            {
                if (boosterButtons[index] != null && boosterButtons[index].BoosterType == boosterType)
                {
                    return boosterButtons[index];
                }
            }

            return null;
        }

        // Read-only query version of RequestBoosterImplementation's own gating checks (unlock
        // level + active flag + remaining count) - "would a tap on this button actually do
        // something right now", without any of that method's side effects (purchase popup,
        // implementation events, UI refresh). Used to gate an idle-attention highlight so it never
        // pulses a booster the player can't actually use yet.
        public bool IsBoosterAvailable(BoosterType boosterType)
        {
            if (wallet == null || !wallet.Config.IsActive(boosterType))
            {
                return false;
            }

            int unlockLevel = wallet.Config.GetUnlockLevel(boosterType);
            int currentLevelNumber = currentLevelNumberProvider?.Invoke() ?? 1;
            if (unlockLevel > 0 && currentLevelNumber < unlockLevel)
            {
                return false;
            }

            return boosterType == BoosterType.Undo || wallet.GetCount(boosterType) > 0;
        }

        public void HideBoosterInfo()
        {
            ResolveInfoReferences();
            if (boosterInfoRoutine != null)
            {
                StopCoroutine(boosterInfoRoutine);
                boosterInfoRoutine = null;
            }

            if (boosterInfoLabel != null)
            {
                boosterInfoLabel.gameObject.SetActive(false);
            }
        }

        public void SetBoostersInputLocked(bool isLocked)
        {
            if (boosterButtons == null)
            {
                return;
            }

            for (int index = 0; index < boosterButtons.Length; index++)
            {
                boosterButtons[index]?.SetInputLocked(isLocked);
            }
        }

        public void OnBoosterButtonClick(BoosterType boosterType)
        {
            switch (boosterType)
            {
                case BoosterType.Hint:
                    OnHintBoosterButtonClick();
                    break;
                case BoosterType.Fix:
                    OnFixBoosterButtonClick();
                    break;
                case BoosterType.Undo:
                    OnUndoBoosterButtonClick();
                    break;
            }
        }

        public void ShowHintInfo(string message)
        {
            ShowBoosterInfo(BoosterType.Hint, ResolveHintInfoText(message));
        }

        public void ShowBoosterInfo(BoosterType boosterType, string message)
        {
            if (boosterType != BoosterType.Hint || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            ResolveInfoReferences();
            if (boosterInfoLabel == null)
            {
                return;
            }

            if (boosterInfoRoutine != null)
            {
                StopCoroutine(boosterInfoRoutine);
            }

            boosterInfoRoutine = StartCoroutine(ShowBoosterInfoRoutine(message));
        }

        public void OnHintBoosterButtonClick()
        {
            RequestBoosterImplementation(BoosterType.Hint, HintImplementationRequested);
        }

        public void OnFixBoosterButtonClick()
        {
            RequestBoosterImplementation(BoosterType.Fix, FixImplementationRequested);
        }

        public void OnUndoBoosterButtonClick()
        {
            RequestBoosterImplementation(BoosterType.Undo, UndoImplementationRequested);
        }

        private void RequestBoosterImplementation(BoosterType boosterType, BoosterImplementationEvent implementationRequested)
        {
            if (lastRequestFrame == Time.frameCount && lastRequestedBoosterType == boosterType)
            {
                return;
            }

            lastRequestFrame = Time.frameCount;
            lastRequestedBoosterType = boosterType;

            int currentLevelNumber = currentLevelNumberProvider?.Invoke() ?? 1;
            int unlockLevel = wallet?.Config.GetUnlockLevel(boosterType) ?? -1;
            if (wallet == null ||
                !wallet.Config.IsActive(boosterType) ||
                (unlockLevel > 0 && currentLevelNumber < unlockLevel))
            {
                RefreshAllBoosterUI();
                return;
            }

            // Undo is free and unlimited - it has no coin count to check, and never routes to the buy popup.
            if (boosterType != BoosterType.Undo && wallet.GetCount(boosterType) <= 0)
            {
                global::AudioManager.Instance.PlaySoundAction?.Invoke(global::SoundName.ActionBlocked);
                BoosterPurchaseRequested.Invoke(boosterType);
                RefreshAllBoosterUI();
                return;
            }

            BoosterImplementationStarted.Invoke(boosterType);
            implementationRequested.Invoke();
            BoosterImplementationCompleted.Invoke(boosterType);
            RefreshAllBoosterUI();
        }

        private void ConfigureButtons()
        {
            if (boosterButtons == null)
            {
                return;
            }

            for (int index = 0; index < boosterButtons.Length; index++)
            {
                if (boosterButtons[index] != null)
                {
                    boosterButtons[index].Configure(boosterButtons[index].BoosterType);
                    boosterButtons[index].BindClick(OnBoosterButtonClick);
                }
            }
        }

        private IEnumerator ShowBoosterInfoRoutine(string message)
        {
            boosterInfoLabel.text = message;
            boosterInfoLabel.gameObject.SetActive(true);
            boosterInfoTextColor = boosterInfoLabel.color;
            SetBoosterInfoTextAlpha(1f);

            float visibleSeconds = Mathf.Max(0f, boosterInfoVisibleSeconds);
            if (visibleSeconds > 0f)
            {
                yield return new WaitForSeconds(visibleSeconds);
            }

            float fadeSeconds = Mathf.Max(0f, boosterInfoFadeSeconds);
            if (fadeSeconds > 0f)
            {
                for (float elapsed = 0f; elapsed < fadeSeconds; elapsed += Time.deltaTime)
                {
                    SetBoosterInfoTextAlpha(Mathf.Lerp(1f, 0f, Mathf.Clamp01(elapsed / fadeSeconds)));
                    yield return null;
                }

                SetBoosterInfoTextAlpha(0f);
            }

            boosterInfoLabel.gameObject.SetActive(false);
            boosterInfoRoutine = null;
        }

        private void SetBoosterInfoTextAlpha(float alpha)
        {
            if (boosterInfoLabel == null)
            {
                return;
            }

            Color color = boosterInfoTextColor;
            color.a = Mathf.Clamp01(alpha);
            boosterInfoLabel.color = color;
        }

        private void ResolveInfoReferences()
        {
            if (boosterInfoLabel == null)
            {
                boosterInfoLabel = GetComponentInChildren<TMP_Text>(true);
            }
        }

        private string ResolveHintInfoText(string reasonKey)
        {
            switch (reasonKey)
            {
                case "OneMissingLine":
                    return hintOneMissingLineText;
                case "MaxTwoAdjacent":
                    return hintMaxTwoAdjacentText;
                case "LeastEmptyLine":
                    return hintLeastEmptyLineText;
                case "Random":
                    return hintRandomText;
                default:
                    return string.IsNullOrWhiteSpace(reasonKey) ? hintFallbackText : reasonKey;
            }
        }
    }

    [System.Serializable]
    public sealed class BoosterImplementationEvent : UnityEvent
    {
    }

    [System.Serializable]
    public sealed class BoosterLifecycleEvent : UnityEvent<BoosterType>
    {
    }
}
