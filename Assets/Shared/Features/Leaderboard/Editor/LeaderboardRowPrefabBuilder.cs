using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.Leaderboard.Editor
{
    /// <summary>
    /// Fills in the leaderboard row prefabs' missing UI - rank, avatar + frame, name, score - and
    /// wires them onto LeaderboardRowView.
    ///
    /// ADDITIVE, not a regeneration: it only creates children whose names are not already there,
    /// and only assigns serialized fields that are still empty. Whatever background art and layout
    /// the prefab already carries is left exactly as it is, so re-running after dressing a row is
    /// safe, and so is running it again once new fields appear on the view.
    ///
    /// Positions are plain anchors rather than a HorizontalLayoutGroup on purpose: a layout group
    /// rewrites its children every layout pass, and these rows are moved by
    /// LeaderboardPanelController's own slide animation - the same reason LeaderboardRowView's
    /// ApplyListLayout avoids one on the scroll content.
    /// </summary>
    public static class LeaderboardRowPrefabBuilder
    {
        private const string PrefabFolder = "Assets/Shared/Features/Leaderboard/Prefab";
        private const string ListRowPath = PrefabFolder + "/Player.prefab";
        private const string PodiumRowPath = PrefabFolder + "/PlayerPodium.prefab";

        // Left-to-right bands across the row, as insets from each edge. A list row reads
        // rank | avatar | name .......... score.
        private const float RankWidth = 80f;
        private const float AvatarSize = 90f;
        private const float ScoreWidth = 120f;
        private const float Gap = 12f;

        [MenuItem("MangraGames/Build Leaderboard Row Prefabs")]
        public static void Build()
        {
            BuildRow(ListRowPath, podium: false);
            BuildRow(PodiumRowPath, podium: true);
            AssetDatabase.SaveAssets();
        }

        private static void BuildRow(string path, bool podium)
        {
            if (!File.Exists(path))
            {
                Debug.LogError($"LeaderboardRowPrefabBuilder: {path} does not exist.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                LeaderboardRowView view = root.GetComponent<LeaderboardRowView>();
                if (view == null)
                {
                    view = root.AddComponent<LeaderboardRowView>();
                }

                // The row fades in on first appearance (see LeaderboardRowView.PlayAppear). The
                // view adds one itself at runtime when missing, but having it on the asset keeps
                // the prefab honest about what it needs.
                if (root.GetComponent<CanvasGroup>() == null)
                {
                    root.AddComponent<CanvasGroup>();
                }

                RectTransform rootRect = root.GetComponent<RectTransform>();
                float rowHeight = Mathf.Max(1f, rootRect.sizeDelta.y);
                float avatar = podium ? Mathf.Min(rowHeight * 0.8f, AvatarSize * 1.6f) : AvatarSize;

                // Rank sits hard left on a list row. A podium place already says which place it is
                // through its own slot and art, so the number goes under the avatar instead.
                GameObject rankGO = EnsureChild(root, "RankLabel");
                TextMeshProUGUI rank = EnsureText(rankGO, podium ? 40 : 34, TextAlignmentOptions.Center);
                if (podium)
                {
                    AnchorBottomBand(rankGO.GetComponent<RectTransform>(), 34f);
                }
                else
                {
                    AnchorLeftBand(rankGO.GetComponent<RectTransform>(), 0f, RankWidth);
                }

                // Avatar container: the portrait, with the frame drawn OVER it as a ring. Frame is
                // created second so it is the later sibling - in Unity UI that is what draws on
                // top, and a frame under its avatar is invisible.
                GameObject avatarRootGO = EnsureChild(root, "Avatar");
                RectTransform avatarRect = avatarRootGO.GetComponent<RectTransform>();
                if (podium)
                {
                    AnchorCenteredSquare(avatarRect, avatar, yOffset: 18f);
                }
                else
                {
                    AnchorLeftBand(avatarRect, RankWidth + Gap, avatar);
                    SquareInBand(avatarRect, avatar);
                }

                GameObject avatarImageGO = EnsureChild(avatarRootGO, "AvatarImage");
                Image avatarImage = EnsureImage(avatarImageGO);
                Stretch(avatarImageGO.GetComponent<RectTransform>());

                GameObject frameImageGO = EnsureChild(avatarRootGO, "FrameImage");
                Image frameImage = EnsureImage(frameImageGO);
                Stretch(frameImageGO.GetComponent<RectTransform>());
                // Re-asserted every run: an additive pass cannot assume the two were created in
                // this order on a prefab somebody has since rearranged.
                frameImageGO.transform.SetAsLastSibling();

                GameObject nameGO = EnsureChild(root, "NameLabel");
                TextMeshProUGUI name = EnsureText(nameGO, podium ? 32 : 36, podium ? TextAlignmentOptions.Center : TextAlignmentOptions.Left);
                if (podium)
                {
                    AnchorBottomBand(nameGO.GetComponent<RectTransform>(), 32f, yOffset: 36f);
                }
                else
                {
                    AnchorMiddleBand(nameGO.GetComponent<RectTransform>(), RankWidth + Gap + avatar + Gap, ScoreWidth + Gap);
                }

                GameObject scoreGO = EnsureChild(root, "ScoreLabel");
                TextMeshProUGUI score = EnsureText(scoreGO, podium ? 34 : 36, podium ? TextAlignmentOptions.Center : TextAlignmentOptions.Right);
                if (podium)
                {
                    AnchorBottomBand(scoreGO.GetComponent<RectTransform>(), 34f, yOffset: 2f);
                }
                else
                {
                    AnchorRightBand(scoreGO.GetComponent<RectTransform>(), 0f, ScoreWidth);
                }

                // Marks the player's own row. Starts inactive - Bind turns it on only for the row
                // whose entry IsPlayer.
                GameObject highlightGO = EnsureChild(root, "PlayerHighlight");
                Stretch(highlightGO.GetComponent<RectTransform>());
                if (highlightGO.transform.childCount == 0 && highlightGO.GetComponent<Image>() == null)
                {
                    Image highlight = highlightGO.AddComponent<Image>();
                    highlight.color = new Color(1f, 0.85f, 0.35f, 0.22f);
                    highlight.raycastTarget = false;
                }

                highlightGO.transform.SetAsFirstSibling();
                highlightGO.SetActive(false);

                WireIfEmpty(view, "rankLabel", rank);
                WireIfEmpty(view, "nameLabel", name);
                WireIfEmpty(view, "scoreLabel", score);
                WireIfEmpty(view, "avatarImage", avatarImage);
                WireIfEmpty(view, "frameImage", frameImage);
                WireIfEmpty(view, "playerHighlightRoot", highlightGO);
                WireIfEmpty(view, "canvasGroup", root.GetComponent<CanvasGroup>());

                PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                Debug.Log(success
                    ? $"LeaderboardRowPrefabBuilder: updated {path}."
                    : $"LeaderboardRowPrefabBuilder: FAILED to save {path}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // =========================================================
        // HELPERS
        // =========================================================

        /// <summary>Finds a direct child by name, creating it only when it is genuinely absent - this is what makes re-running non-destructive.</summary>
        private static GameObject EnsureChild(GameObject parent, string name)
        {
            Transform existing = parent.transform.Find(name);
            if (existing != null)
            {
                return existing.gameObject;
            }

            GameObject created = new GameObject(name, typeof(RectTransform));
            created.transform.SetParent(parent.transform, false);
            return created;
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
            // A long rival name should shrink rather than spill into the score column.
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.color = Color.white;
            return text;
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
            // Bind enables it only once a sprite actually arrives; an enabled Image with no sprite
            // draws a white box.
            image.enabled = false;
            return image;
        }

        private static void WireIfEmpty(Object target, string propertyName, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null || property.objectReferenceValue != null)
            {
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>Full-height band measured from the row's left edge.</summary>
        private static void AnchorLeftBand(RectTransform rect, float leftInset, float width)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(leftInset, 0f);
            rect.offsetMax = new Vector2(leftInset + width, 0f);
        }

        private static void AnchorRightBand(RectTransform rect, float rightInset, float width)
        {
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(-(rightInset + width), 0f);
            rect.offsetMax = new Vector2(-rightInset, 0f);
        }

        /// <summary>Stretches between a left and a right inset - the name column, which takes whatever is left over.</summary>
        private static void AnchorMiddleBand(RectTransform rect, float leftInset, float rightInset)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(leftInset, 0f);
            rect.offsetMax = new Vector2(-rightInset, 0f);
        }

        /// <summary>Squares a full-height band about its centre, so the avatar is not stretched by row height.</summary>
        private static void SquareInBand(RectTransform rect, float size)
        {
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
        }

        private static void AnchorCenteredSquare(RectTransform rect, float size, float yOffset)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(0f, yOffset);
        }

        /// <summary>Full-width strip pinned to the bottom - the podium's stacked name/score/rank.</summary>
        private static void AnchorBottomBand(RectTransform rect, float height, float yOffset = 0f)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(0f, height);
            rect.anchoredPosition = new Vector2(0f, yOffset);
        }
    }
}
