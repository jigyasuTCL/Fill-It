using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DailyChallenges
{
    public sealed partial class DailyChallengeController : MonoBehaviour
    {
        [SerializeField]
        private int year = DateTime.Now.Year;

        [SerializeField]
        private int month = DateTime.Now.Month;

        [SerializeField]
        private TMP_Text monthLabel = default;

        [SerializeField]
        private DailyChallengeManager dailyChallengeManager;
        public GameObject cheat;
        private DailyChallengeDateButton selectedButton = null;

        /// <summary>Currently selected calendar day's data, if any - lets late subscribers (e.g.
        /// DailyChallengePlayButtonController on its own OnEnable) get the current selection
        /// instead of only ever reacting to the next SelectedDayChanged.</summary>
        public DailyChallengeData SelectedData => selectedButton?.Data;
        public DailyChallengeDateButton dailyChallengeDateButtonPrefab;
        [SerializeField]
        private List<DailyChallengeDateButton> dayButtons = new List<DailyChallengeDateButton>();
        [SerializeField]
        private List<Transform> weekdayContainers = new List<Transform>();
        [Tooltip("True: place each day under its real weekday column, padding blank cells " +
                 "before Day 1 so it lines up under a Mon-Sun header row. False: ignore the " +
                 "actual weekday and just fill the 7 containers in a repeating loop, with " +
                 "Day 1 always starting in the first container.")]
        [SerializeField]
        private bool alignDaysToActualWeekday = true;
        private static int TotalLevels =>
            Shared.Boot.GlobalConfigRuntimeContext.HasConfig
                ? Math.Max(1, Shared.Boot.GlobalConfigRuntimeContext.Config.DailyChallenge.TotalLevels)
                : 40;

        private static int TotalSubLevels =>
            Shared.Boot.GlobalConfigRuntimeContext.HasConfig
                ? Math.Max(1, Shared.Boot.GlobalConfigRuntimeContext.Config.DailyChallenge.TotalSubLevels)
                : 3;

        private DailyChallengeJsonConfig jsonConfig = null;
        private IReadOnlyList<DailyChallengeData> dailyChallengeData;

        public IReadOnlyList<DailyChallengeData> DailyChallengeData => dailyChallengeData;

        public bool HasWatchedAdForDailyChallenge { get; private set; } = false;

        public void Initialize(int year, int month)
        {
            this.year = year;
            this.month = month;

            ResetAdWatchStateForNewDay();

            jsonConfig = LoadOrCreateJsonConfig(year);

            RecoverInterruptedAttempt();

            dailyChallengeData = CreateDailyChallengeData(year, month);

            UpdateMonthLabel(year, month);

            CreateDayButtons();

            UpdateViews();

            SelectDefaultDay();

            EnsureUnlockNotificationScheduled();
        }
        private void CreateDayButtons()
        {
            // Clear existing date buttons
            foreach (var button in dayButtons)
            {
                if (button != null)
                    Destroy(button.gameObject);
            }

            dayButtons.Clear();

            // Clear previously created empty cells
            foreach (Transform container in weekdayContainers)
            {
                for (int i = container.childCount - 1; i >= 0; i--)
                {
                    Destroy(container.GetChild(i).gameObject);
                }
            }

            if (dailyChallengeData == null || dailyChallengeData.Count == 0)
                return;

            if (weekdayContainers == null || weekdayContainers.Count != 7)
            {
                Debug.LogError(
                    "DailyChallengeController: weekdayContainers must contain exactly 7 containers."
                );
                return;
            }

            if (alignDaysToActualWeekday)
            {
                // Get the weekday of the first day of the month.
                // Convert Sunday-based DayOfWeek to Monday-based index.
                DateTime firstDate = new DateTime(year, month, 1);

                int firstWeekdayIndex = ((int)firstDate.DayOfWeek + 6) % 7;

                // --------------------------------------------------
                // Create EMPTY cells before Day 1
                // --------------------------------------------------

                for (int i = 0; i < firstWeekdayIndex; i++)
                {
                    CreateEmptyDayCell(weekdayContainers[i]);
                }
            }

            // --------------------------------------------------
            // Create actual day buttons
            // --------------------------------------------------

            int loopIndex = 0;

            foreach (DailyChallengeData data in dailyChallengeData)
            {
                if (data == null)
                    continue;

                int weekdayIndex;

                if (alignDaysToActualWeekday)
                {
                    DateTime date = new DateTime(year, month, data.Day);
                    weekdayIndex = ((int)date.DayOfWeek + 6) % 7;
                }
                else
                {
                    weekdayIndex = loopIndex % 7;
                    loopIndex++;
                }

                Transform container = weekdayContainers[weekdayIndex];

                DailyChallengeDateButton button =
                    Instantiate(
                        dailyChallengeDateButtonPrefab,
                        container
                    );

                button.Initialize(data, this);

                dayButtons.Add(button);
            }
        }
        private void CreateEmptyDayCell(Transform parent)
        {
            GameObject emptyCell = new GameObject(
                "EmptyDayCell",
                typeof(RectTransform)
            );
            emptyCell.transform.SetParent(parent, false);

            RectTransform rectElement = emptyCell.GetComponent<RectTransform>();
            RectTransform prefabRect = dailyChallengeDateButtonPrefab.GetComponent<RectTransform>();

            float width = prefabRect.rect.width;
            float height = prefabRect.rect.height;

            rectElement.sizeDelta = new Vector2(width, height);
            rectElement.localScale = prefabRect.localScale;
        }
        public void InitializeCurrentMonth()
        {
            Initialize(DailyChallengeClock.Now.Year, DailyChallengeClock.Now.Month);
        }

        public void TrySelectDay(DailyChallengeData data, DailyChallengeDateButton button)
        {
            if (data == null || button == null)
            {
                return;
            }

            if (!data.IsUnlocked)
            {
                return;
            }

            if (data.HasWin)
            {
                return;
            }

            AssignDailyChallengeLevel(data, button);
        }


        public void enableCheat()
        {
            
cheat.gameObject.SetActive(!cheat.gameObject.activeSelf);
        }

    }
}
