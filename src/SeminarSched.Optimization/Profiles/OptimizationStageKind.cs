namespace SeminarSched.Optimization.Profiles;

public enum OptimizationStageKind
{
    InitialExploration,
    CandidateAdvancement,
    NeighborhoodRepair,
    FinalPolishing,

    /// <summary>
    /// ユーザー指示「指定時間内に足りない場合がある。そういった場合は途中で中断するのではなく、
    /// 少し時間を要していますといった警告を出して、続行してください」への対応。通常の全ステージを
    /// 使い切っても未配置が残っている（または1件も解が得られていない）場合に限り、名目時間と同じ
    /// 長さだけ追加でgrinding戦略を実行する延長フェーズ（最大でも名目時間の2倍で必ず打ち切る）。
    /// </summary>
    Extension,
}
