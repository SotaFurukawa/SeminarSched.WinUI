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

        // checkpoint93（ユーザー報告「2時間経過しても最高品質のものが終わりませんでした。もう少し
        // 軽量な仕様にしてください...イメージだと45分くらいで終わらせて、残りの15分は合間合間で
        // もう少し良いものがないか探索する時間です」）: High/Highestとも、まず「完成させる」ことに
        // 名目時間の75%（IE+CA）を割き、残り25%（NR[+FP]）だけを「時間が余ったので改善を試す」枠に
        // 充てるよう再設計した。以前は65〜70%を初期探索・候補改善に、25〜40%を改善枠に割きつつ、
        // 初期探索を5個の戦略で均等分割していたため、1戦略あたりの持ち時間が短く（Highestで3.6分）
        // 「完成させる」こと自体に失敗しやすかった（同じ問題を毎回ゼロから解き直す短い試行を5回
        // 繰り返すより、少数の戦略へまとまった時間を与えた方が完成に到達しやすい）。戦略数も
        // 5→3（High）・5→4（Highest）へ減らし、1戦略あたりの持ち時間を底上げした。
        yield return Profile(
            OptimizationQualityLevel.High,
            "高品質",
            "約10〜30分",
            "6種類の探索を行った後、部分修復による改善を試みます。",
            1800,
            420,
            Stage(OptimizationStageKind.InitialExploration, 0.50, 2,
                OptimizationStrategyKind.StandardCpSat,
                OptimizationStrategyKind.SeededCpSatA,
                OptimizationStrategyKind.AlternateDecision),
            Stage(OptimizationStageKind.CandidateAdvancement, 0.25, 2,
                OptimizationStrategyKind.MultiStage,
                OptimizationStrategyKind.HintImprovement),
            // CP-SATは証明済み最適解に達すると持ち時間を使い切らず早期に終わるため、固定数の
            // 部分修復を並べるだけでは（当初3〜5個並べてみたが、実際に数分で全部終わってしまうこと
            // をユーザーに指摘された）長い名目時間を活かせない。GrindingNeighborhoodRepairStrategyへ
            // ステージの持ち時間まるごとを渡し、内部でこのステージの持ち時間を使い切るまで異なる
            // 乱数近傍を試し続けさせる（1戦略だけをここに置くことで、strategyBudget=stageBudgetの
            // 全体がこの1戦略に渡る）。
            Stage(OptimizationStageKind.NeighborhoodRepair, 0.25, 1,
                OptimizationStrategyKind.GrindingNeighborhoodRepair));

        yield return Profile(
            OptimizationQualityLevel.Highest,
            "最高品質",
            "約30〜60分",
            "8種類の探索を行った後、部分修復による改善と最終調整を試みます。",
            3600,
            600,
            Stage(OptimizationStageKind.InitialExploration, 0.50, 3,
                OptimizationStrategyKind.StandardCpSat,
                OptimizationStrategyKind.SeededCpSatA,
                OptimizationStrategyKind.SeededCpSatB,
                OptimizationStrategyKind.AlternateDecision),
            Stage(OptimizationStageKind.CandidateAdvancement, 0.25, 2,
                OptimizationStrategyKind.MultiStage,
                OptimizationStrategyKind.HintImprovement),
            Stage(OptimizationStageKind.NeighborhoodRepair, 0.15, 1,
                OptimizationStrategyKind.GrindingNeighborhoodRepair),
            Stage(OptimizationStageKind.FinalPolishing, 0.10, 1,
                OptimizationStrategyKind.GrindingFinalPolishing));
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
