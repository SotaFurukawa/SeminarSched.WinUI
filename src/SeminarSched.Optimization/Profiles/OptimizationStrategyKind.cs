namespace SeminarSched.Optimization.Profiles;

public enum OptimizationStrategyKind
{
    StandardCpSat,
    SeededCpSatA,
    SeededCpSatB,
    SeededCpSatC,
    AlternateDecision,
    MultiStage,
    HintImprovement,
    NeighborhoodRepair,
    FinalPolishing,
}
