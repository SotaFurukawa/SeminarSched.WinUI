namespace SeminarSched.Optimization.Profiles;

public static class OptimizationProfileCatalog
{
    private static readonly IReadOnlyDictionary<OptimizationQualityLevel, OptimizationProfile> Profiles =
        CreateProfiles().ToDictionary(profile => profile.Level);

    public static OptimizationQualityLevel DefaultLevel => OptimizationQualityLevel.Standard;

    public static IReadOnlyCollection<OptimizationProfile> All => Profiles.Values.ToArray();

    public static OptimizationProfile Get(OptimizationQualityLevel level) =>
        Profiles.TryGetValue(level, out var profile)
            ? profile
            : throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown optimization quality level.");

    private static IEnumerable<OptimizationProfile> CreateProfiles()
    {
        yield return Profile(
            OptimizationQualityLevel.Fast,
            "高速",
            "約30秒〜1分",
            "動作確認や仮の時間割を短時間で作成します。",
            60,
            30,
            Stage(OptimizationStageKind.InitialExploration, 1.0, 1,
                OptimizationStrategyKind.StandardCpSat,
                OptimizationStrategyKind.SeededCpSatA,
                OptimizationStrategyKind.AlternateDecision));

        yield return Profile(
            OptimizationQualityLevel.Faster,
            "やや高速",
            "約1〜3分",
            "複数の初期解と簡易的なhint再探索を比較します。",
            180,
            60,
            Stage(OptimizationStageKind.InitialExploration, 0.75, 1,
                OptimizationStrategyKind.StandardCpSat,
                OptimizationStrategyKind.SeededCpSatA,
                OptimizationStrategyKind.SeededCpSatB,
                OptimizationStrategyKind.AlternateDecision),
            Stage(OptimizationStageKind.CandidateAdvancement, 0.25, 1,
                OptimizationStrategyKind.HintImprovement));

        yield return Profile(
            OptimizationQualityLevel.Standard,
            "標準",
            "約3〜10分",
            "複数の探索方法を比較し、良い解をhintとしてさらに改善します。",
            600,
            180,
            Stage(OptimizationStageKind.InitialExploration, 0.60, 2,
                OptimizationStrategyKind.StandardCpSat,
                OptimizationStrategyKind.SeededCpSatA,
                OptimizationStrategyKind.SeededCpSatB,
                OptimizationStrategyKind.AlternateDecision),
            Stage(OptimizationStageKind.CandidateAdvancement, 0.40, 1,
                OptimizationStrategyKind.MultiStage,
                OptimizationStrategyKind.HintImprovement));

        yield return Profile(
            OptimizationQualityLevel.High,
            "高品質",
            "約10〜30分",
            "複数候補から上位を選び、hint再探索と部分修復で改善します。",
            1800,
            420,
            Stage(OptimizationStageKind.InitialExploration, 0.40, 3,
                OptimizationStrategyKind.StandardCpSat,
                OptimizationStrategyKind.SeededCpSatA,
                OptimizationStrategyKind.SeededCpSatB,
                OptimizationStrategyKind.SeededCpSatC,
                OptimizationStrategyKind.AlternateDecision),
            Stage(OptimizationStageKind.CandidateAdvancement, 0.35, 2,
                OptimizationStrategyKind.MultiStage,
                OptimizationStrategyKind.HintImprovement),
            Stage(OptimizationStageKind.NeighborhoodRepair, 0.25, 1,
                OptimizationStrategyKind.NeighborhoodRepair));

        yield return Profile(
            OptimizationQualityLevel.Highest,
            "最高品質",
            "約30〜60分",
            "有望な候補へ時間を集中し、部分修復と最終調整まで行います。",
            3600,
            600,
            Stage(OptimizationStageKind.InitialExploration, 0.30, 3,
                OptimizationStrategyKind.StandardCpSat,
                OptimizationStrategyKind.SeededCpSatA,
                OptimizationStrategyKind.SeededCpSatB,
                OptimizationStrategyKind.SeededCpSatC,
                OptimizationStrategyKind.AlternateDecision),
            Stage(OptimizationStageKind.CandidateAdvancement, 0.30, 2,
                OptimizationStrategyKind.MultiStage,
                OptimizationStrategyKind.HintImprovement),
            Stage(OptimizationStageKind.NeighborhoodRepair, 0.25, 1,
                OptimizationStrategyKind.NeighborhoodRepair),
            Stage(OptimizationStageKind.FinalPolishing, 0.15, 1,
                OptimizationStrategyKind.FinalPolishing));
    }

    private static OptimizationProfile Profile(
        OptimizationQualityLevel level,
        string name,
        string duration,
        string description,
        int maximumSeconds,
        int stagnationSeconds,
        params OptimizationStageDefinition[] stages) =>
        new(
            level,
            name,
            duration,
            description,
            TimeSpan.FromSeconds(maximumSeconds),
            TimeSpan.FromSeconds(stagnationSeconds),
            stages);

    private static OptimizationStageDefinition Stage(
        OptimizationStageKind kind,
        double budgetShare,
        int candidatesToAdvance,
        params OptimizationStrategyKind[] strategies) =>
        new(kind, budgetShare, candidatesToAdvance, strategies);
}
