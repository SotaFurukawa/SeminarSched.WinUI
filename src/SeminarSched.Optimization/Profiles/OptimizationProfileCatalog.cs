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
        // v0.2.0のマルチ戦略への切替時、実データ（生徒57名・受講希望83件・必要回数計458件）で
        // 「高速」を検証したところ、ステージ内3戦略で60秒を均等分割すると1戦略あたり20秒しか
        // 割り当てられず、単一戦略時代なら21〜31秒で得られていたcold-start解にすら届かず
        // 全戦略が失敗するという実害のある退行を確認した。ステージの時間はStrategies.Countで
        // 均等分割される仕様（ScheduleOptimizer参照）なので、低品質帯は戦略数を絞り総時間も
        // 底上げして「1戦略あたりの持ち時間」が単一戦略時代を下回らないようにしている。
        yield return Profile(
            OptimizationQualityLevel.Fast,
            "高速",
            "約1〜2分",
            "動作確認や仮の時間割を短時間で作成します。",
            120,
            60,
            Stage(OptimizationStageKind.InitialExploration, 1.0, 1,
                OptimizationStrategyKind.StandardCpSat,
                OptimizationStrategyKind.AlternateDecision));

        yield return Profile(
            OptimizationQualityLevel.Faster,
            "やや高速",
            "約2〜4分",
            "複数の初期解と簡易的なhint再探索を比較します。",
            240,
            90,
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
            "複数候補から上位を選び、hint再探索と複数の部分修復で改善します。",
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
            // 部分修復を1回だけでなく異なる乱数の近傍で複数回試す（NeighborhoodRepairStrategyBase参照）。
            // CP-SATは証明済み最適解に達すると持ち時間を使い切らず早期に終わることが多いため、
            // 高品質帯で長い名目時間を実際に活かすには「同じ探索を長く待つ」のではなく
            // 「別の近傍を複数回試す」ほうが改善の見込みが立つ。
            Stage(OptimizationStageKind.NeighborhoodRepair, 0.25, 1,
                OptimizationStrategyKind.NeighborhoodRepair,
                OptimizationStrategyKind.NeighborhoodRepairB,
                OptimizationStrategyKind.NeighborhoodRepairC));

        yield return Profile(
            OptimizationQualityLevel.Highest,
            "最高品質",
            "約30〜60分",
            "有望な候補へ時間を集中し、複数の部分修復と最終調整まで行います。",
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
                OptimizationStrategyKind.NeighborhoodRepair,
                OptimizationStrategyKind.NeighborhoodRepairB,
                OptimizationStrategyKind.NeighborhoodRepairC,
                OptimizationStrategyKind.NeighborhoodRepairD,
                OptimizationStrategyKind.NeighborhoodRepairE),
            Stage(OptimizationStageKind.FinalPolishing, 0.15, 1,
                OptimizationStrategyKind.FinalPolishing,
                OptimizationStrategyKind.FinalPolishingB));
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
