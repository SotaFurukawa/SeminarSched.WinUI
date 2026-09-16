namespace SeminarSched.Optimization.Core;

public sealed record LessonDemand(long RequestId, long StudentId, int RequiredSessions, int AlreadyFixedSessions);
public sealed record PlacementCandidate(long RequestId, long StudentId, long TeacherId, long OpenDateId, long TimeSlotId);
public sealed record ScheduleProblem(IReadOnlyList<LessonDemand> Demands, IReadOnlyList<PlacementCandidate> Candidates);
public sealed record SchedulePlacement(long RequestId, long StudentId, long TeacherId, long OpenDateId, long TimeSlotId);
public sealed record ScheduleSolution(IReadOnlyList<SchedulePlacement> Placements, int UnassignedLessons, long ObjectiveValue, TimeSpan Elapsed);
