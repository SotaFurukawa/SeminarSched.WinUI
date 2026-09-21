namespace SeminarSched.Optimization.Evaluation;

public sealed record ScheduleEvaluation(
    int HardConstraintViolations,
    int UnassignedLessons,
    int ImportantPreferenceViolations,
    long MajorPenalty,
    int TeacherGapSlots,
    long DistributionPenalty,
    long SpacingPenalty,
    long OtherSoftPenalty,
    long ObjectiveValue) : IComparable<ScheduleEvaluation>
{
    public int CompareTo(ScheduleEvaluation? other)
    {
        if (other is null)
        {
            return -1;
        }

        return Compare(HardConstraintViolations, other.HardConstraintViolations)
            ?? Compare(UnassignedLessons, other.UnassignedLessons)
            ?? Compare(ImportantPreferenceViolations, other.ImportantPreferenceViolations)
            ?? Compare(MajorPenalty, other.MajorPenalty)
            ?? Compare(TeacherGapSlots, other.TeacherGapSlots)
            ?? Compare(DistributionPenalty, other.DistributionPenalty)
            ?? Compare(SpacingPenalty, other.SpacingPenalty)
            ?? Compare(OtherSoftPenalty, other.OtherSoftPenalty)
            ?? ObjectiveValue.CompareTo(other.ObjectiveValue);
    }

    public bool IsBetterThan(ScheduleEvaluation other) => CompareTo(other) < 0;

    private static int? Compare<T>(T left, T right)
        where T : IComparable<T>
    {
        var result = left.CompareTo(right);
        return result == 0 ? null : result;
    }
}
