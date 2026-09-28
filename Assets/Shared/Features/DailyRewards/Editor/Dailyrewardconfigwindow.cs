using System.Text;
using UnityEditor;
using UnityEngine;
using Shared.GameConfig;

// Tools > Daily Rewards > Config Reference
//
// Read-only reference panel for designers: lists every id / type / amount /
// iconKey / requireType currently loaded into GlobalConfig.DailyReward, for
// both main rewards and each sub-reward set.
//
// This reads live runtime data (Shared.Boot.GlobalConfigRuntimeContext), so
// it only has anything to show once GlobalConfig has actually loaded -
// in practice that means Play Mode. The window auto-refreshes while
// playing and tells you clearly when there's nothing loaded yet.
public class DailyRewardConfigWindow : EditorWindow
{
    private Vector2 scrollPos;

    private const float ColId = 160f;
    private const float ColType = 90f;
    private const float ColAmount = 70f;
    private const float ColIconKey = 160f;
    private const float ColRequires = 80f;

    [MenuItem("Tools/Daily Rewards/Config Reference")]
    public static void Open()
    {
        var window = GetWindow<DailyRewardConfigWindow>("Daily Reward Config");
        window.minSize = new Vector2(640f, 360f);
    }

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    // Keeps the window live while playing, since config/state can change
    // (claims, cycle rollovers) without the window otherwise repainting.
    private void OnEditorUpdate()
    {
        if (EditorApplication.isPlaying)
        {
            Repaint();
        }
    }

    private void OnGUI()
    {
        DailyRewardConfigData data = GetDailyRewardConfig(out string statusMessage);

        DrawToolbar(data);

        if (data == null)
        {
            EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
            return;
        }

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        DrawSystemSummary(data);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Main Rewards", EditorStyles.boldLabel);
        DrawTableHeader();

        for (int i = 0; i < data.mainRewards.Count; i++)
        {
            DrawItemRow(i, data.mainRewards[i]);
        }

        if (data.mainRewards.Count == 0)
        {
            EditorGUILayout.LabelField("(no main rewards configured)", EditorStyles.miniLabel);
        }

        EditorGUILayout.Space(14f);
        EditorGUILayout.LabelField("Sub Reward Sets", EditorStyles.boldLabel);

        if (data.subRewardSets.Count == 0)
        {
            EditorGUILayout.LabelField("(no sub reward sets configured)", EditorStyles.miniLabel);
        }

        for (int setIndex = 0; setIndex < data.subRewardSets.Count; setIndex++)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField($"Set {setIndex}", EditorStyles.miniBoldLabel);
            DrawTableHeader();

            SubRewardSet set = data.subRewardSets[setIndex];

            for (int i = 0; i < set.subRewards.Count; i++)
            {
                DrawItemRow(i, set.subRewards[i]);
            }

            if (set.subRewards.Count == 0)
            {
                EditorGUILayout.LabelField("(empty set)", EditorStyles.miniLabel);
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawToolbar(DailyRewardConfigData data)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        GUILayout.Label(
            EditorApplication.isPlaying ? "Live (Play Mode)" : "Not Playing",
            EditorStyles.toolbarButton);

        GUILayout.FlexibleSpace();

        GUI.enabled = data != null;
        if (GUILayout.Button("Copy as CSV", EditorStyles.toolbarButton))
        {
            CopyCsvToClipboard(data);
        }
        GUI.enabled = true;

        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton))
        {
            Repaint();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawSystemSummary(DailyRewardConfigData data)
    {
        EditorGUILayout.LabelField(
            data.enableRestriction
                ? $"System locked until level {data.unlockLevel}"
                : "System always unlocked (no level gate)",
            EditorStyles.miniLabel);

        EditorGUILayout.LabelField(
            $"Main cooldown: {data.cooldownMinutes} min   " +
            $"Sub cooldown: {data.subCooldownMinutes} min   " +
            $"Notify after: {data.NotificationMinutes} min",
            EditorStyles.miniLabel);
    }

    private void DrawTableHeader()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Id", EditorStyles.boldLabel, GUILayout.Width(ColId));
        GUILayout.Label("Type", EditorStyles.boldLabel, GUILayout.Width(ColType));
        GUILayout.Label("Amount", EditorStyles.boldLabel, GUILayout.Width(ColAmount));
        GUILayout.Label("Icon Key", EditorStyles.boldLabel, GUILayout.Width(ColIconKey));
        GUILayout.Label("Requires", EditorStyles.boldLabel, GUILayout.Width(ColRequires));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawItemRow(int index, BaseRewardItem item)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(string.IsNullOrEmpty(item.id) ? $"(no id) [{index}]" : item.id, GUILayout.Width(ColId));
        GUILayout.Label(item.type ?? "-", GUILayout.Width(ColType));
        GUILayout.Label(item.amount.ToString(), GUILayout.Width(ColAmount));
        GUILayout.Label(string.IsNullOrEmpty(item.iconKey) ? "-" : item.iconKey, GUILayout.Width(ColIconKey));
        GUILayout.Label(item.requireType.ToString(), GUILayout.Width(ColRequires));
        EditorGUILayout.EndHorizontal();
    }

    private void CopyCsvToClipboard(DailyRewardConfigData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("section,setIndex,id,type,amount,iconKey,requireType");

        for (int i = 0; i < data.mainRewards.Count; i++)
        {
            AppendCsvRow(sb, "main", -1, data.mainRewards[i]);
        }

        for (int setIndex = 0; setIndex < data.subRewardSets.Count; setIndex++)
        {
            SubRewardSet set = data.subRewardSets[setIndex];
            for (int i = 0; i < set.subRewards.Count; i++)
            {
                AppendCsvRow(sb, "sub", setIndex, set.subRewards[i]);
            }
        }

        EditorGUIUtility.systemCopyBuffer = sb.ToString();
        Debug.Log("Daily reward config copied to clipboard as CSV.");
    }

    private void AppendCsvRow(StringBuilder sb, string section, int setIndex, BaseRewardItem item)
    {
        sb.AppendLine(
            $"{section},{setIndex},{item.id},{item.type},{item.amount},{item.iconKey},{item.requireType}");
    }

    // Returns null (with an explanatory status message) if GlobalConfig
    // hasn't loaded yet - that's expected outside Play Mode.
    private DailyRewardConfigData GetDailyRewardConfig(out string statusMessage)
    {
        if (!EditorApplication.isPlaying)
        {
            statusMessage = "Enter Play Mode to load GlobalConfig and view the live daily reward data.";
            return null;
        }

        bool hasConfig = Shared.Boot.GlobalConfigRuntimeContext.HasConfig;

        if (!hasConfig)
        {
            statusMessage = "GlobalConfig hasn't finished loading yet.";
            return null;
        }

        GlobalConfig config = Shared.Boot.GlobalConfigRuntimeContext.Config;

        if (config?.DailyReward == null)
        {
            statusMessage = "GlobalConfig loaded, but DailyReward is missing.";
            return null;
        }

        statusMessage = null;
        return config.DailyReward;
    }
}