using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FillIt.Levels;
using FillIt.Rules;
using Shared.GameConfig;
using UnityEditor;
using UnityEngine;

namespace FillIt.EditorTools
{
    /// <summary>
    /// Writes the Fill It level content: the 100 campaign levels (overwriting the existing
    /// level_NNN.json assets in place, so every scene reference to them stays wired) and the
    /// Daily Challenge levels (Resources/DailyChallengeLevels/dc_N_1.json). Levels 1-3 are the
    /// hand-made 3x3 tutorial boards, the 20 hand-checked reference puzzles are slotted into the
    /// size ramp, and everything else is generated with a proven-unique solution. Deterministic -
    /// the same seed always writes the same levels.
    /// </summary>
    public static class FillItLevelGenerator
    {
        public const string CampaignFolder = "Assets/0_Game/CatFlap/Data/Json/Levels";
        public const string DailyChallengeFolder = "Assets/Resources/DailyChallengeLevels";
        private const string GlobalConfigPath = "Assets/Resources/Configs/GlobalConfig.asset";
        private const int CampaignLevelCount = 100;
        private const int Seed = 20260928;

        private const float EasyClueRatio = 0.52f;
        private const float MediumClueRatio = 0.44f;
        private const float HardClueRatio = 0.36f;

        // Starting Timer-mode limit written into each level's "timeLimitSeconds" - tune per level in the JSON.
        private const float TimerSecondsPerEmptyCell = 6f;
        private const float TimerMinimumSeconds = 45f;

        // Tutorial boards (FTUE levels 1-3): tap for a 1, drag to count, grow a number.
        private static readonly (string clues, string solution)[] TutorialLevels =
        {
            ("022333220", "122333221"),
            ("200210122", "233213122"),
            ("400220333", "444224333"),
        };

        // Hand-checked reference puzzles (unique solutions), keyed by the campaign level they take.
        private static readonly Dictionary<int, (int size, string clues, string solution)> ReferenceLevels = new Dictionary<int, (int, string, string)>
        {
            { 4, (4, "2034443002044000", "2234443442344214") },
            { 5, (4, "0140201003300040", "2144231443344441") },
            { 6, (4, "0020010243300000", "4221413243324221") },
            { 8, (5, "2202144000012500000005003", "2242144425512555525555333") },
            { 10, (5, "0302100000250020030052000", "3322123444255425533252231") },
            { 12, (5, "1400050024000000040500000", "1442454424555445445544555") },
            { 17, (6, "006000600540162200000420040004300500", "666555665544162244344422345544355544") },
            { 19, (6, "433063002000051000000000010310100100", "433363442263451663555366513313122133") },
            { 22, (6, "130040010000030200501300000500014050", "133342514442533221531333544555514455") },
            { 26, (6, "031200040060050006050000004000520600", "331226344466154666551441524466526666") },
            { 32, (7, "0410720007000001000012460500400040030360040200070", "4417722447777721665512466555444644433367742277777") },
            { 36, (7, "0022000010014052065040000520005000000670140000001", "5522444516614152665145266524665552466677146777771") },
            { 42, (7, "0143100340004000000050030001006305004700420000004", "3143144344334436415556633751666375244777424477444") },
            { 48, (7, "0401000043044040400000005603006000470004000750000", "1431441443344341455537445653746666477764447755555") },
            { 54, (8, "0000700176074030006020032000000000600510007420041881805080888301", "7777744176674433276622132764414477642514777425541881835588888331") },
            { 62, (8, "0401000440205040700480660040080006608800400080770630000700027007", "4441554447225544777485667744886676648866466688774633381744227777") },
            { 74, (8, "0080000110000007000027120521003001800450400080000000132004820001", "8888777118882777552827125521333251844455488884554882132544823321") },
            { 83, (9, "107704040010061405000600500560766405000023000600040000404000555035059000005010000", "177714441517161415557666555567766445566223344666444355444149555435559999335519999") },
            { 91, (9, "808080200088884080600674000603007080402040040004008904000800010100009000027800000", "888184228688884488666674788633377788442247748474448944477888914177789999227888999") },
            { 98, (9, "002030090446650000000000390301600035303700700440807055700100700000000000000819000", "422333999446659999466655399331655335343777755443887755743188799777888999777819999") },
        };

        [MenuItem("MangraGames/Fill It/Generate All Levels")]
        public static void GenerateAllFromMenu()
        {
            GenerateAll(dailyChallengeCount: 40);
        }

        /// <summary>
        /// Adds "timeLimitSeconds" to every existing campaign and Daily Challenge level JSON that
        /// doesn't have one yet, without touching the puzzles. Authored values are kept.
        /// </summary>
        [MenuItem("MangraGames/Fill It/Write Missing Level Time Limits")]
        public static void WriteMissingTimeLimitsFromMenu()
        {
            Debug.Log("[FillIt] " + WriteMissingTimeLimits());
        }

        public static string WriteMissingTimeLimits()
        {
            int written = 0;
            int kept = 0;
            var files = new List<string>();
            files.AddRange(Directory.GetFiles(CampaignFolder, "level_*.json"));
            if (Directory.Exists(DailyChallengeFolder))
            {
                files.AddRange(Directory.GetFiles(DailyChallengeFolder, "dc_*.json"));
            }

            foreach (string path in files)
            {
                if (!FillItLevelJson.TryParse(File.ReadAllText(path), out FillItLevelData level, out string error))
                {
                    Debug.LogError($"[FillIt] {path}: {error}");
                    continue;
                }

                if (level.TimeLimitSeconds != null)
                {
                    kept++;
                    continue;
                }

                level.TimeLimitSeconds = level.EstimateTimeLimitSeconds(TimerSecondsPerEmptyCell, TimerMinimumSeconds);
                File.WriteAllText(path, FillItLevelJson.ToJson(level) + "\n", new UTF8Encoding(false));
                written++;
            }

            AssetDatabase.Refresh();
            return $"Level time limits: {written} written, {kept} already set.";
        }

        public static string GenerateAll(int dailyChallengeCount)
        {
            var log = new StringBuilder();
            GlobalConfig config = LoadConfig();
            var hard = new HashSet<int>(config?.HardLevels ?? Array.Empty<int>());
            var easyLoop = new HashSet<int>(config?.LoopLevels?.EasyLevels ?? Array.Empty<int>());
            var mediumLoop = new HashSet<int>(config?.LoopLevels?.MediumLevels ?? Array.Empty<int>());
            if (config?.LoopLevels?.HardLevels != null)
            {
                hard.UnionWith(config.LoopLevels.HardLevels);
            }

            var generator = new FillItPuzzleGenerator(Seed);
            try
            {
                for (int levelNumber = 1; levelNumber <= CampaignLevelCount; levelNumber++)
                {
                    EditorUtility.DisplayProgressBar("Fill It levels", $"Campaign level {levelNumber}", levelNumber / (float)(CampaignLevelCount + dailyChallengeCount));
                    string difficulty = ResolveDifficulty(levelNumber, hard, easyLoop, mediumLoop);
                    FillItLevelData level = BuildCampaignLevel(levelNumber, difficulty, generator);
                    WriteLevel(Path.Combine(CampaignFolder, $"level_{levelNumber:000}.json"), level, log);
                }

                Directory.CreateDirectory(DailyChallengeFolder);
                var dailyGenerator = new FillItPuzzleGenerator(Seed + 1);
                for (int day = 1; day <= dailyChallengeCount; day++)
                {
                    EditorUtility.DisplayProgressBar("Fill It levels", $"Daily Challenge {day}", (CampaignLevelCount + day) / (float)(CampaignLevelCount + dailyChallengeCount));
                    int size = 6 + (day - 1) % 3;
                    FillItPuzzleGenerator.Puzzle puzzle = GenerateOrThrow(dailyGenerator, size, MediumClueRatio);
                    var level = new FillItLevelData
                    {
                        Id = $"dc_{day}_1",
                        Name = $"Daily Challenge {day}",
                        Difficulty = "Medium",
                        Size = size,
                        Clues = puzzle.Clues,
                        Solution = puzzle.Solution
                    };
                    WriteLevel(Path.Combine(DailyChallengeFolder, $"dc_{day}_1.json"), level, log);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }

            Debug.Log("[FillIt] Level generation finished:\n" + log);
            return log.ToString();
        }

        private static FillItLevelData BuildCampaignLevel(int levelNumber, string difficulty, FillItPuzzleGenerator generator)
        {
            var level = new FillItLevelData
            {
                Id = $"level_{levelNumber:000}",
                Name = $"Level {levelNumber}",
                Difficulty = difficulty
            };

            if (levelNumber <= TutorialLevels.Length)
            {
                level.Size = 3;
                level.Clues = FillItLevelJson.ParseDigits(TutorialLevels[levelNumber - 1].clues);
                level.Solution = FillItLevelJson.ParseDigits(TutorialLevels[levelNumber - 1].solution);
                return level;
            }

            if (ReferenceLevels.TryGetValue(levelNumber, out (int size, string clues, string solution) reference))
            {
                level.Size = reference.size;
                level.Clues = FillItLevelJson.ParseDigits(reference.clues);
                level.Solution = FillItLevelJson.ParseDigits(reference.solution);
                return level;
            }

            int boardSize = ResolveSize(levelNumber);
            float ratio = difficulty == "Hard" ? HardClueRatio : difficulty == "Medium" ? MediumClueRatio : EasyClueRatio;
            FillItPuzzleGenerator.Puzzle puzzle = GenerateOrThrow(generator, boardSize, ratio);
            level.Size = boardSize;
            level.Clues = puzzle.Clues;
            level.Solution = puzzle.Solution;
            return level;
        }

        // Board size ramp across the campaign.
        private static int ResolveSize(int levelNumber)
        {
            if (levelNumber <= 7) return 4;
            if (levelNumber <= 15) return 5;
            if (levelNumber <= 30) return 6;
            if (levelNumber <= 50) return 7;
            if (levelNumber <= 80) return 8;
            return 9;
        }

        private static string ResolveDifficulty(int levelNumber, HashSet<int> hard, HashSet<int> easyLoop, HashSet<int> mediumLoop)
        {
            if (hard.Contains(levelNumber)) return "Hard";
            if (easyLoop.Contains(levelNumber)) return "Easy";
            if (mediumLoop.Contains(levelNumber)) return "Medium";
            return levelNumber <= 20 ? "Easy" : "Medium";
        }

        private static FillItPuzzleGenerator.Puzzle GenerateOrThrow(FillItPuzzleGenerator generator, int size, float ratio)
        {
            FillItPuzzleGenerator.Puzzle puzzle = generator.Generate(size, Math.Min(9, size), ratio);
            if (puzzle == null)
            {
                throw new InvalidOperationException($"Fill It generator could not build a unique {size}x{size} puzzle.");
            }

            return puzzle;
        }

        private static void WriteLevel(string path, FillItLevelData level, StringBuilder log)
        {
            List<string> errors = FillItLevelValidator.Validate(level);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException($"{level.Id} is invalid: {string.Join(" | ", errors)}");
            }

            int solutions = new FillItSolver(level.Size).CountSolutions(level.Clues, 2, 5_000_000);
            if (solutions != 1)
            {
                throw new InvalidOperationException($"{level.Id} has {solutions} solutions.");
            }

            if (level.TimeLimitSeconds == null)
            {
                level.TimeLimitSeconds = level.EstimateTimeLimitSeconds(TimerSecondsPerEmptyCell, TimerMinimumSeconds);
            }

            File.WriteAllText(path, FillItLevelJson.ToJson(level) + "\n", new UTF8Encoding(false));
            int clues = 0;
            foreach (int clue in level.Clues)
            {
                if (clue != 0) clues++;
            }

            log.AppendLine($"{level.Id}: {level.Size}x{level.Size} {level.Difficulty} clues {clues}/{level.Size * level.Size}");
        }

        private static GlobalConfig LoadConfig()
        {
            GlobalConfigAsset asset = AssetDatabase.LoadAssetAtPath<GlobalConfigAsset>(GlobalConfigPath);
            return asset != null ? asset.ToConfig() : null;
        }
    }
}
