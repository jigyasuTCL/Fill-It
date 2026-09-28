using System.Text.RegularExpressions;
using UnityEngine;

namespace Shared.GameFlow
{
    /// <summary>
    /// Per-level "you beat X% of players" style lines for the Win and Fail popups. Levels 1..N
    /// show that level's own entry (index levelNumber - 1) in order; any level past the last
    /// entry picks a random one from the same list instead. Loaded once from
    /// Resources/Configs/CatFlapLevelResultTextConfig, alongside every other config asset.
    ///
    /// Mark whichever words should render in the popup's own highlight color by wrapping them in
    /// *asterisks*, e.g. "You beat *96%* of players" - ApplyHighlight turns that into a TMP
    /// &lt;color&gt; tag using the CALLER's already-defined highlight color, so this asset only
    /// ever chooses which words to highlight, never the color itself.
    /// </summary>
    [CreateAssetMenu(fileName = "CatFlapLevelResultTextConfig", menuName = "MangraGames/CatFlap/Level Result Text Config")]
    public sealed class CatFlapLevelResultTextConfig : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            [TextArea] public string text;
        }

        [Header("Win - shown in order for levels 1..N, randomly for any level past that")]
        [SerializeField]
        private Entry[] winEntries =
        {
            new Entry { text = "*74%* of players were slower" },
            new Entry { text = "You outpaced *78%* of players" },
            new Entry { text = "Just *30%* of players were faster" },
            new Entry { text = "Quicker than *74%* of players" },
            new Entry { text = "*Two thirds* of players were slower" },
            new Entry { text = "Faster than *79%* of players" },
            new Entry { text = "You beat *68%* of players" },
            new Entry { text = "*81%* of players took longer" },
            new Entry { text = "Quicker than *91%* of players" },
            new Entry { text = "Only *21%* of players went quicker" },
            new Entry { text = "*86%* of players were slower" },
            new Entry { text = "Clear of *92%* of the field" },
            new Entry { text = "You beat *88%* of players" },
            new Entry { text = "That's a *top 22%* run" },
            new Entry { text = "You outpaced *91%* of players" },
            new Entry { text = "Faster than *87%* of players" },
            new Entry { text = "Only *23%* of players did better" },
            new Entry { text = "Ahead of *65%* of the field" },
            new Entry { text = "Quicker than *70%* of players" },
            new Entry { text = "You're in the *top 17%*" },
            new Entry { text = "Better than *7 in 10* players" },
            new Entry { text = "*65%* of players needed more time" },
            new Entry { text = "Better than *73%* of players" },
            new Entry { text = "*86%* of players took longer" },
            new Entry { text = "Faster than *92%* of players" },
            new Entry { text = "Only *18%* of players did better" },
            new Entry { text = "*69%* of players were slower" },
            new Entry { text = "Just *27%* finished faster" },
            new Entry { text = "Better than *65%* of players" },
            new Entry { text = "*71%* of players needed more time" },
            new Entry { text = "*Top 16%* on this board" },
            new Entry { text = "You outpaced *80%* of players" },
            new Entry { text = "Quicker than *75%* of players" },
            new Entry { text = "In the *fastest 15%*" },
            new Entry { text = "Faster than *89%* of players" },
            new Entry { text = "You're in the *top 24%*" },
            new Entry { text = "Clear of *65%* of the field" },
            new Entry { text = "Only *22%* of players did better" },
            new Entry { text = "Better than *87%* of players" },
            new Entry { text = "In the *fastest 25%*" },
            new Entry { text = "You beat *65%* of players" },
            new Entry { text = "*78%* of players needed more time" },
            new Entry { text = "That's a *top 10%* run" },
            new Entry { text = "Only *22%* came in ahead" },
            new Entry { text = "Clear of *87%* of the field" },
            new Entry { text = "Better than *91%* of players" },
            new Entry { text = "*Top 21%* on this board" },
            new Entry { text = "Only *31%* of players did better" },
            new Entry { text = "Faster than *65%* of players" },
            new Entry { text = "Only *22%* of players were quicker" },
        };

        [Header("Fail - same order-then-random rule as Win")]
        [SerializeField]
        private Entry[] failEntries =
        {
            new Entry { text = "*40%* of players never clear this one" },
            new Entry { text = "*64%* clear it. Your turn." },
            new Entry { text = "*58%* of players finish this board" },
            new Entry { text = "*Two thirds* of players clear this board" },
            new Entry { text = "*61%* of players get through here" },
            new Entry { text = "This board beats *43%* of players" },
            new Entry { text = "*Half* of players clear this board" },
            new Entry { text = "*43%* make it past this board" },
            new Entry { text = "*48%* get through. One more go?" },
            new Entry { text = "*40%* of players don't get past this" },
            new Entry { text = "*49%* of players get through here" },
            new Entry { text = "*59%* of players never clear this one" },
            new Entry { text = "Just over *half* of players clear this one" },
            new Entry { text = "*45%* of players finish this board" },
            new Entry { text = "This board stops *49%* of players" },
            new Entry { text = "*47%* of players beat this level" },
            new Entry { text = "*42%* make it. Try again?" },
            new Entry { text = "*37%* of players solve this one" },
            new Entry { text = "This one turns back *51%* of players" },
            new Entry { text = "Fewer than *half* of players get through" },
            new Entry { text = "Just *1 in 3* players clear this board" },
            new Entry { text = "*57%* of players don't get past this" },
            new Entry { text = "*49%* make it past this board" },
            new Entry { text = "This board stops *60%* of players" },
            new Entry { text = "*46%* of players clear this one" },
            new Entry { text = "About *a third* of players get through here" },
            new Entry { text = "This board beats *55%* of players" },
            new Entry { text = "*41%* of players finish this board" },
            new Entry { text = "*45%* get through. One more go?" },
            new Entry { text = "This one turns back *61%* of players" },
            new Entry { text = "Only *1 in 3* players get past this" },
            new Entry { text = "*46%* make it. Try again?" },
            new Entry { text = "*54%* of players beat this level" },
            new Entry { text = "*Three in five* players beat this one" },
            new Entry { text = "*49%* of players finish this board" },
            new Entry { text = "Fewer than *half* of players clear this one" },
            new Entry { text = "*57%* get through. One more go?" },
            new Entry { text = "*66%* of players solve this one" },
            new Entry { text = "Around *three in five* players get through" },
            new Entry { text = "This board beats *48%* of players" },
            new Entry { text = "*60%* of players get through here" },
            new Entry { text = "This board stops *46%* of players" },
            new Entry { text = "*59%* of players finish this board" },
            new Entry { text = "*54%* clear it. Your turn." },
            new Entry { text = "*51%* of players never clear this one" },
            new Entry { text = "*57%* of players solve this one" },
            new Entry { text = "This board beats *47%* of players" },
            new Entry { text = "*42%* make it past this board" },
            new Entry { text = "*51%* get through. One more go?" },
            new Entry { text = "*56%* of players clear this one" },
        };

        private const string ResourcePath = "Configs/CatFlapLevelResultTextConfig";
        private static CatFlapLevelResultTextConfig cachedInstance;
        private static bool triedLoad;

        public static CatFlapLevelResultTextConfig Load()
        {
            if (!triedLoad)
            {
                cachedInstance = Resources.Load<CatFlapLevelResultTextConfig>(ResourcePath);
                triedLoad = true;
            }

            return cachedInstance;
        }

        public string GetWinText(int levelNumber)
        {
            return ResolveText(winEntries, levelNumber);
        }

        public string GetFailText(int levelNumber)
        {
            return ResolveText(failEntries, levelNumber);
        }

        private static string ResolveText(Entry[] entries, int levelNumber)
        {
            if (entries == null || entries.Length == 0)
            {
                return string.Empty;
            }

            int index = levelNumber >= 1 && levelNumber <= entries.Length
                ? levelNumber - 1
                : Random.Range(0, entries.Length);
            return entries[index].text;
        }

        public static string ApplyHighlight(string rawText, Color highlightColor)
        {
            if (string.IsNullOrEmpty(rawText))
            {
                return rawText;
            }

            string colorHex = ColorUtility.ToHtmlStringRGB(highlightColor);
            return Regex.Replace(rawText, @"\*(.+?)\*", $"<color=#{colorHex}>$1</color>");
        }
    }
}
