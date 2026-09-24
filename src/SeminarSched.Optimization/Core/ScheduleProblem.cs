using SeminarSched.Domain.Scheduling;

namespace SeminarSched.Optimization.Core;

public sealed record LessonDemand(
    long RequestId,
    long StudentId,
    int RequiredSessions,
    int AlreadyFixedSessions,
    long? RegularTeacherId = null,
    int RegularTeacherPriority = 1,
    int FixedRegularTeacherSessions = 0,
    int MaxConsecutiveSlots = 2,
    bool AllowGap = false,
    bool OneToOneRequired = false);

public sealed record PlacementCandidate(
    long RequestId,
    long StudentId,
    long TeacherId,
    long OpenDateId,
    long TimeSlotId,
    int DayOrdinal = 0,
    int SlotOrder = 0,
    int PreferencePenalty = 0,
    int AvailabilityPreference = 1,
    bool OneToOneRequired = false);
public sealed record ScheduleSlot(long OpenDateId, long TimeSlotId, int DayOrdinal, int SlotOrder);
public sealed record FixedPlacement(
    long RequestId,
    long StudentId,
    long TeacherId,
    long OpenDateId,
    long TimeSlotId,
    int DayOrdinal,
    int SlotOrder,
    bool OneToOneRequired = false);
public sealed record ScheduleProblem(
    IReadOnlyList<LessonDemand> Demands,
    IReadOnlyList<PlacementCandidate> Candidates,
    IReadOnlyList<ScheduleSlot>? Slots = null,
    IReadOnlyList<FixedPlacement>? FixedPlacements = null,
    SchedulingPolicy? Policy = null)
{
    public SchedulingPolicy Policy { get; } = Policy ?? SchedulingPolicy.Default;

    public IReadOnlyList<ScheduleSlot> AvailableSlots { get; } = Slots ??
        Candidates.Select(candidate => new ScheduleSlot(candidate.OpenDateId, candidate.TimeSlotId, candidate.DayOrdinal, candidate.SlotOrder))
            .Distinct()
            .ToArray();

    public IReadOnlyList<FixedPlacement> ExistingPlacements { get; } = FixedPlacements ?? [];
}
public sealed record SchedulePlacement(long RequestId, long StudentId, long TeacherId, long OpenDateId, long TimeSlotId);
public sealed record ScheduleSolution(IReadOnlyList<SchedulePlacement> Placements, int UnassignedLessons, long ObjectiveValue, TimeSpan Elapsed);
