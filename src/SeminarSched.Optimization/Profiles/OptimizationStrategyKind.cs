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
    NeighborhoodRepairB,
    NeighborhoodRepairC,
    NeighborhoodRepairD,
    NeighborhoodRepairE,
    GrindingNeighborhoodRepair,
    FinalPolishing,
    FinalPolishingB,
    GrindingFinalPolishing,
}
