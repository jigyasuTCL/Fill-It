using Shared.GameFlow;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.Leaderboard.Editor
{
    /// <summary>
    /// Builds the two prefabs the end-of-event results need: the popup - celebration stage and
    /// prize stage - and one prize card inside it.
    ///
    /// ADDITIVE, like LeaderboardRowPrefabBuilder. Children are created only when absent, LAYOUT IS
    /// APPLIED ONLY TO WHAT IT CREATES, and serialized fields are filled only when still empty. So
    /// the first run lays out a working greybox, and every run after that adds whatever is new
    /// without touching a single thing that has since been dressed or repositioned.
    ///
    /// Lookup is by name across the whole prefab rather than by direct child, so re-running after
    /// the hierarchy has been rearranged finds what is already there instead of building a second
    /// copy of it somewhere else.
    /// </summary>
    public static class LeaderboardRewardPrefabBuilder
    {
        private const string PrefabFolder = "Assets/Shared/Features/Leaderboard/Prefab";
        private const string PopupPath = PrefabFolder + "/RewardPopup.prefab";
        private const string RewardItemPath = PrefabFolder + "/RewardItem.prefab";

        // Matches the leaderboard panel's own 860 width, so the two sit on the same grid.
        private const float CardWidth = 860f;
        private const float CardHeight = 1400f;
        private const float ItemWidth = 220f;
        private const float ItemHeight = 280f;

        [MenuItem("MangraGames/Build Leaderboard Reward Prefabs")]
        public static void Build()
        {
            LeaderboardRewardRowView item = BuildRewardItem();
            BuildPopup(item);
            AssetDatabase.SaveAssets();
        }

        // =========================================================
        // PRIZE CARD
        // =========================================================

        private static LeaderboardRewardRowView BuildRewardItem()
        {
            GameObject root = LoadOrCreate(RewardItemPath, "RewardItem", new Vector2(ItemWidth, ItemHeight));
            try
            {
                LeaderboardRewardRowView view = Ensure<LeaderboardRewardRowView>(root);

                // Required by the row's own fade - without it PlayAppear can only scale.
                CanvasGroup group = Ensure<CanvasGroup>(root);

                GameObject iconGO = EnsureChild(root, root, "Icon", out bool created);
                Image icon = EnsureImage(iconGO);
                if (created)
                {
                    AnchorCentered(iconGO, new Vector2(150f, 150f), yOffset: 40f);
                }

                // Sits under the icon, the usual place for a quantity pip. Hidden by Bind for a
                // one-off prize such as a frame.
                GameObject amountGO = EnsureChild(root, root, "AmountText", out created);
                TextMeshProUGUI amount = EnsureText(amountGO, 40f, TextAlignmentOptions.Center);
                if (created)
                {
                    AnchorCentered(amountGO, new Vector2(ItemWidth, 46f), yOffset: -50f);
                }

                GameObject labelGO = EnsureChild(root, root, "LabelText", out created);
                TextMeshProUGUI label = EnsureText(labelGO, 32f, TextAlignmentOptions.Center);
                if (created)
                {
                    AnchorBottomBand(labelGO, 40f, yOffset: 8f);
                }

                WireIfEmpty(view, "iconImage", icon);
                WireIfEmpty(view, "amountText", amount);
                WireIfEmpty(view, "labelText", label);
                WireIfEmpty(view, "canvasGroup", group);

                return SaveAndReload<LeaderboardRewardRowView>(root, RewardItemPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // =========================================================
        // POPUP
        // =========================================================

        private static void BuildPopup(LeaderboardRewardRowView rewardItemPrefab)
        {
            // Full-screen root: dropped under a Canvas and stretched, same as the other popup roots
            // under GameFlow/Prefabs/UI.
            GameObject root = LoadOrCreate(PopupPath, "RewardPopup", Vector2.zero, stretch: true);
            try
            {
                LeaderboardRewardPopupController controller = Ensure<LeaderboardRewardPopupController>(root);

                // Kept SEPARATE from the controller's own object: closing deactivates this, and a
                // controller deactivated with it could never reopen.
                GameObject panel = EnsureChild(root, root, "Panel", out bool created);
                if (created)
                {
                    Stretch(panel);
                }

                // Catches taps meant for the leaderboard behind the popup, and dims it.
                GameObject blockerGO = EnsureChild(root, panel, "Blocker", out created);
                if (created)
                {
                    Stretch(blockerGO);
                    Image blocker = blockerGO.AddComponent<Image>();
                    blocker.color = new Color(0f, 0f, 0f, 0.72f);
                }

                GameObject card = EnsureChild(root, panel, "Card", out created);
                if (created)
                {
                    AnchorCentered(card, new Vector2(CardWidth, CardHeight), yOffset: 0f);
                }

                // Scales the CARD, not the dim behind it - the house style for every popup here.
                PopupScaleAnimator animator = Ensure<PopupScaleAnimator>(card);
                WireIfEmpty(animator, "target", card.GetComponent<RectTransform>());

                LeaderboardRewardCelebrationView celebration = BuildCelebrationStage(root, card);
                GameObject celebrationRoot = celebration.gameObject;

                BuildRewardStage(root, card, controller, rewardItemPrefab);

                // Two buttons, one per stage: "Go to Collect" hands over to the prizes, "Collect"
                // pays out. The controller swaps them, so both are authored here.
                GameObject goToGO = EnsureChild(root, card, "GoToCollectButton", out created);
                if (created)
                {
                    AnchorBottomCentered(goToGO, new Vector2(520f, 140f), yOffset: 60f);
                }

                Button goTo = EnsureButton(goToGO, new Color(0.86f, 0.62f, 0.2f, 1f));
                TextMeshProUGUI goToLabel = EnsureButtonLabel(root, goToGO, "Go to Collect");

                GameObject collectGO = EnsureChild(root, card, "CollectButton", out created);
                if (created)
                {
                    AnchorBottomCentered(collectGO, new Vector2(520f, 140f), yOffset: 60f);
                }

                Button collect = EnsureButton(collectGO, new Color(0.35f, 0.75f, 0.35f, 1f));
                TextMeshProUGUI collectLabel = EnsureButtonLabel(root, collectGO, "Collect");

                GameObject closeGO = EnsureChild(root, card, "CloseButton", out created);
                if (created)
                {
                    AnchorTopRight(closeGO, new Vector2(90f, 90f), new Vector2(-20f, -20f));
                }

                Button close = EnsureButton(closeGO, new Color(0.8f, 0.35f, 0.3f, 1f));
                EnsureButtonLabel(root, closeGO, "X");

                WireIfEmpty(controller, "panelRoot", panel);
                WireIfEmpty(controller, "popupAnimator", animator);
                WireIfEmpty(controller, "celebrationRoot", celebrationRoot);
                WireIfEmpty(controller, "celebrationView", celebration);
                WireIfEmpty(controller, "goToCollectButton", goTo);
                WireIfEmpty(controller, "collectButton", collect);
                WireIfEmpty(controller, "closeButton", close);
                WireIfEmpty(controller, "collectButtonLabel", collectLabel);

                // The icon database is a plain asset, so unlike the LeaderboardManager reference it
                // CAN be resolved here - there is one in the project and every other reward UI uses
                // the same one.
                WireIfEmpty(controller, "iconDatabase", FindSingleAsset<IconDatabaseConfig>());

                // Stage one is what opens, so stage two starts hidden. The controller re-asserts
                // both every time it opens; this is only so the prefab looks right in isolation.
                celebrationRoot.SetActive(true);
                collectGO.SetActive(false);
                goToGO.SetActive(true);

                // Authored visible so the art can be edited, but saved closed so pressing Play does
                // not put it over the menu. The controller does the same defensively at runtime.
                panel.SetActive(false);

                PrefabUtility.SaveAsPrefabAsset(root, PopupPath, out bool success);
                Debug.Log(success
                    ? "LeaderboardRewardPrefabBuilder: updated " + PopupPath + "."
                    : "LeaderboardRewardPrefabBuilder: FAILED to save " + PopupPath + ".");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// The podium stage. Podiums, one standing spot per place, the winner, and empty roots for
        /// confetti and sparkles that the designer fills with their own particle art.
        ///
        /// The winner and every anchor share one parent on purpose: the view moves the winner by
        /// anchoredPosition, which is only comparable between siblings.
        /// </summary>
        private static LeaderboardRewardCelebrationView BuildCelebrationStage(GameObject root, GameObject card)
        {
            GameObject stage = EnsureChild(root, card, "Celebration", out bool created);
            if (created)
            {
                Stretch(stage);
            }

            LeaderboardRewardCelebrationView view = Ensure<LeaderboardRewardCelebrationView>(stage);

            GameObject headline = EnsureChild(root, stage, "Headline", out created);
            if (created)
            {
                AnchorTopBand(headline, 110f, yOffset: -40f);
            }

            TextMeshProUGUI headlineText = EnsureText(headline, 52f, TextAlignmentOptions.Center);
            headlineText.enableWordWrapping = true;
            CanvasGroup headlineGroup = Ensure<CanvasGroup>(headline);
            SetTextIfEmpty(headlineText, "You've won 1st place 25 times!");

            // Behind the podiums, so falling paper passes in front of the dim but not the winner.
            GameObject confetti = EnsureChild(root, stage, "Confetti", out created);
            if (created)
            {
                Stretch(confetti);
            }

            GameObject podiums = EnsureChild(root, stage, "Podiums", out created);
            if (created)
            {
                AnchorCentered(podiums, new Vector2(CardWidth, 520f), yOffset: -60f);
            }

            // Sparkles sit with the podiums rather than full-screen: they are the twinkle ON the
            // winner's platform, not weather.
            GameObject sparkles = EnsureChild(root, podiums, "Sparkles", out created);
            if (created)
            {
                Stretch(sparkles);
            }

            // Index 0 = 1st and stands centre and tallest, exactly as the board's podium reads.
            var podiumRects = new Object[3];
            podiumRects[0] = EnsurePodium(root, podiums, "Podium1st", new Vector2(0f, -60f), new Vector2(260f, 200f), new Color(0.95f, 0.72f, 0.16f, 1f));
            podiumRects[1] = EnsurePodium(root, podiums, "Podium2nd", new Vector2(-255f, -105f), new Vector2(240f, 140f), new Color(0.72f, 0.82f, 0.9f, 1f));
            podiumRects[2] = EnsurePodium(root, podiums, "Podium3rd", new Vector2(255f, -105f), new Vector2(240f, 140f), new Color(0.9f, 0.55f, 0.28f, 1f));

            var anchors = new Object[3];
            anchors[0] = EnsureAnchor(root, podiums, "WinnerAnchor1st", new Vector2(0f, 150f));
            anchors[1] = EnsureAnchor(root, podiums, "WinnerAnchor2nd", new Vector2(-255f, 75f));
            anchors[2] = EnsureAnchor(root, podiums, "WinnerAnchor3rd", new Vector2(255f, 75f));

            // Created last so it draws over every podium - the winner stands ON the platform.
            GameObject winner = EnsureChild(root, podiums, "Winner", out created);
            if (created)
            {
                AnchorCentered(winner, new Vector2(200f, 200f), yOffset: 150f);
            }

            GameObject avatarGO = EnsureChild(root, winner, "AvatarImage", out created);
            Image avatar = EnsureImage(avatarGO);
            if (created)
            {
                Stretch(avatarGO);
            }

            // Frame after the avatar, so it draws over it as a ring - same rule as the board rows.
            GameObject frameGO = EnsureChild(root, winner, "FrameImage", out created);
            Image frame = EnsureImage(frameGO);
            if (created)
            {
                Stretch(frameGO);
            }

            WireIfEmpty(view, "winnerRoot", winner.GetComponent<RectTransform>());
            WireIfEmpty(view, "winnerAvatarImage", avatar);
            WireIfEmpty(view, "winnerFrameImage", frame);
            WireIfEmpty(view, "headlineRoot", headline.GetComponent<RectTransform>());
            WireIfEmpty(view, "headlineLabel", headlineText);
            WireIfEmpty(view, "headlineGroup", headlineGroup);
            WireIfEmpty(view, "confettiRoot", confetti);
            WireIfEmpty(view, "sparkleRoot", sparkles);
            WireArrayIfEmpty(view, "podiums", podiumRects);
            WireArrayIfEmpty(view, "winnerAnchors", anchors);

            return view;
        }

        /// <summary>The prize stage: where the player finished, what it scored, and the prizes.</summary>
        private static void BuildRewardStage(
            GameObject root,
            GameObject card,
            LeaderboardRewardPopupController controller,
            LeaderboardRewardRowView rewardItemPrefab)
        {
            GameObject stage = EnsureChild(root, card, "RewardStage", out bool created);
            if (created)
            {
                Stretch(stage);
            }

            GameObject badgeGO = EnsureChild(root, stage, "RankBadge", out created);
            Image badge = EnsureImage(badgeGO);
            if (created)
            {
                SquareInTopBand(badgeGO, 220f, yOffset: -120f);
            }

            GameObject rankGO = EnsureChild(root, stage, "RankLabel", out created);
            TextMeshProUGUI rankLabel = EnsureText(rankGO, 52f, TextAlignmentOptions.Center);
            if (created)
            {
                AnchorTopBand(rankGO, 64f, yOffset: -380f);
            }

            GameObject scoreGO = EnsureChild(root, stage, "ScoreLabel", out created);
            TextMeshProUGUI scoreLabel = EnsureText(scoreGO, 38f, TextAlignmentOptions.Center);
            if (created)
            {
                AnchorTopBand(scoreGO, 46f, yOffset: -450f);
            }

            // The prize strip. Centred so one prize sits in the middle and three spread evenly,
            // rather than everything hugging the left edge.
            GameObject listGO = EnsureChild(root, stage, "RewardList", out created);
            if (created)
            {
                AnchorCentered(listGO, new Vector2(CardWidth - 80f, ItemHeight + 20f), yOffset: -40f);

                // Configured on creation only, so a re-run never undoes spacing somebody tuned.
                HorizontalLayoutGroup layout = listGO.AddComponent<HorizontalLayoutGroup>();
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.spacing = 24f;
                // Control and expand all off: each prize card carries its own authored size, and a
                // group that resized them would fight the pop-in scale.
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;
                layout.childControlWidth = false;
                layout.childControlHeight = false;
            }

            SetTextIfEmpty(rankLabel, "You finished 1st!");
            SetTextIfEmpty(scoreLabel, "0 fish");

            WireIfEmpty(controller, "rewardStageRoot", stage);
            WireIfEmpty(controller, "rankLabel", rankLabel);
            WireIfEmpty(controller, "scoreLabel", scoreLabel);
            WireIfEmpty(controller, "rankBadgeImage", badge);
            WireIfEmpty(controller, "rewardRowPrefab", rewardItemPrefab);
            WireIfEmpty(controller, "rewardRowParent", listGO.GetComponent<RectTransform>());

            stage.SetActive(false);
        }

        private static RectTransform EnsurePodium(GameObject root, GameObject parent, string name, Vector2 position, Vector2 size, Color color)
        {
            GameObject podium = EnsureChild(root, parent, name, out bool created);
            if (created)
            {
                AnchorAt(podium, size, position);
                Image image = podium.AddComponent<Image>();
                image.color = color;
                image.raycastTarget = false;
            }

            return podium.GetComponent<RectTransform>();
        }

        /// <summary>A bare marker - the view reads its position and nothing draws it.</summary>
        private static RectTransform EnsureAnchor(GameObject root, GameObject parent, string name, Vector2 position)
        {
            GameObject anchor = EnsureChild(root, parent, name, out bool created);
            if (created)
            {
                AnchorAt(anchor, new Vector2(10f, 10f), position);
            }

            return anchor.GetComponent<RectTransform>();
        }

        // =========================================================
        // HELPERS
        // =========================================================

        /// <summary>
        /// Opens the prefab for editing, creating it first if this is the initial run. Returns
        /// contents that MUST be unloaded by the caller, same contract as PrefabUtility's own API.
        /// </summary>
        private static GameObject LoadOrCreate(string path, string name, Vector2 size, bool stretch = false)
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                var created = new GameObject(name, typeof(RectTransform));
                RectTransform rect = created.GetComponent<RectTransform>();
                if (stretch)
                {
                    Stretch(created);
                }
                else
                {
                    rect.sizeDelta = size;
                }

                PrefabUtility.SaveAsPrefabAsset(created, path);
                Object.DestroyImmediate(created);
                AssetDatabase.ImportAsset(path);
            }

            return PrefabUtility.LoadPrefabContents(path);
        }

        /// <summary>
        /// Saves, then hands back the component off the SAVED asset - the loaded contents are about
        /// to be thrown away, and a reference into them would not survive being assigned anywhere.
        /// </summary>
        private static T SaveAndReload<T>(GameObject root, string path) where T : Component
        {
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            Debug.Log(success
                ? "LeaderboardRewardPrefabBuilder: updated " + path + "."
                : "LeaderboardRewardPrefabBuilder: FAILED to save " + path + ".");

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return asset != null ? asset.GetComponent<T>() : null;
        }

        private static T Ensure<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        /// <summary>
        /// Finds the object by name ANYWHERE under the prefab, creating it under
        /// <paramref name="parent"/> only when it genuinely is not there.
        ///
        /// Searching the whole tree rather than one parent's children is what makes a re-run safe
        /// after somebody has reorganised the hierarchy: the alternative is a silent second copy of
        /// a label, with the wired one still pointing at the original.
        /// </summary>
        private static GameObject EnsureChild(GameObject root, GameObject parent, string name, out bool created)
        {
            Transform existing = FindDeep(root.transform, name);
            if (existing != null)
            {
                created = false;
                return existing.gameObject;
            }

            var madeGO = new GameObject(name, typeof(RectTransform));
            madeGO.transform.SetParent(parent.transform, false);
            created = true;
            return madeGO;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            for (int index = 0; index < parent.childCount; index++)
            {
                Transform child = parent.GetChild(index);
                if (child.name == name)
                {
                    return child;
                }

                Transform found = FindDeep(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static TextMeshProUGUI EnsureText(GameObject target, float fontSize, TextAlignmentOptions alignment)
        {
            TextMeshProUGUI text = target.GetComponent<TextMeshProUGUI>();
            if (text != null)
            {
                return text;
            }

            text = target.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.color = Color.white;
            return text;
        }

        private static TextMeshProUGUI EnsureButtonLabel(GameObject root, GameObject button, string placeholder)
        {
            // Named per button: a bare "Label" would collide with every other button's label under
            // the deep-name lookup above.
            GameObject labelGO = EnsureChild(root, button, button.name + "Label", out bool created);
            TextMeshProUGUI label = EnsureText(labelGO, 46f, TextAlignmentOptions.Center);
            if (created)
            {
                Stretch(labelGO);
            }

            SetTextIfEmpty(label, placeholder);
            return label;
        }

        /// <summary>Only writes placeholder copy onto a label nobody has authored yet.</summary>
        private static void SetTextIfEmpty(TMP_Text text, string value)
        {
            if (text != null && string.IsNullOrEmpty(text.text))
            {
                text.text = value;
            }
        }

        private static Image EnsureImage(GameObject target)
        {
            Image image = target.GetComponent<Image>();
            if (image != null)
            {
                return image;
            }

            image = target.AddComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            // Enabled only once a sprite arrives; an enabled Image with no sprite is a white box.
            image.enabled = false;
            return image;
        }

        private static Button EnsureButton(GameObject target, Color color)
        {
            Image image = target.GetComponent<Image>();
            if (image == null)
            {
                image = target.AddComponent<Image>();
                image.color = color;
            }

            // A button must stay hittable whatever the placeholder art is replaced with.
            image.raycastTarget = true;

            Button button = target.GetComponent<Button>();
            if (button == null)
            {
                button = target.AddComponent<Button>();
                button.targetGraphic = image;
            }

            return button;
        }

        private static T FindSingleAsset<T>() where T : Object
        {
            string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
            if (guids.Length != 1)
            {
                // Zero or several: guessing would be worse than leaving it for the Inspector.
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static void WireIfEmpty(Object target, string propertyName, Object value)
        {
            if (target == null || value == null)
            {
                return;
            }

            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null || property.objectReferenceValue != null)
            {
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Fills an object array, but only when every slot in it is still empty - a partly assigned
        /// array is somebody's deliberate wiring, and overwriting one element of it would be worse
        /// than leaving the whole thing alone.
        /// </summary>
        private static void WireArrayIfEmpty(Object target, string propertyName, Object[] values)
        {
            if (target == null || values == null)
            {
                return;
            }

            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null || !property.isArray)
            {
                return;
            }

            for (int index = 0; index < property.arraySize; index++)
            {
                if (property.GetArrayElementAtIndex(index).objectReferenceValue != null)
                {
                    return;
                }
            }

            property.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
            {
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Stretch(GameObject target)
        {
            RectTransform rect = target.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void AnchorCentered(GameObject target, Vector2 size, float yOffset)
        {
            AnchorAt(target, size, new Vector2(0f, yOffset));
        }

        private static void AnchorAt(GameObject target, Vector2 size, Vector2 position)
        {
            RectTransform rect = target.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private static void AnchorTopBand(GameObject target, float height, float yOffset)
        {
            RectTransform rect = target.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, height);
            rect.anchoredPosition = new Vector2(0f, yOffset);
        }

        /// <summary>Squares a top-anchored band so a badge sprite is not stretched by the card width.</summary>
        private static void SquareInTopBand(GameObject target, float size, float yOffset)
        {
            RectTransform rect = target.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(0f, yOffset);
        }

        private static void AnchorBottomBand(GameObject target, float height, float yOffset)
        {
            RectTransform rect = target.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(0f, height);
            rect.anchoredPosition = new Vector2(0f, yOffset);
        }

        private static void AnchorBottomCentered(GameObject target, Vector2 size, float yOffset)
        {
            RectTransform rect = target.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(0f, yOffset);
        }

        private static void AnchorTopRight(GameObject target, Vector2 size, Vector2 offset)
        {
            RectTransform rect = target.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = offset;
        }
    }
}
