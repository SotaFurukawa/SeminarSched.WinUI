namespace SeminarSched.Application.Importing;

public enum AvailabilityEntityKind { Student, Teacher }

public sealed record AvailabilityEntityOption(long Id, string Label);

public sealed record AvailabilityDateOption(long OpenDateId, string Label);

public sealed record AvailabilitySlotOption(long TimeSlotId, string Label);

public sealed record AvailabilityMatrixRow(long EntityId, string Label, IReadOnlyDictionary<long, int> LevelsBySlot);

public interface IAvailabilityMatrixService
{
    Task<IReadOnlyList<AvailabilityEntityOption>> GetEntitiesAsync(string projectPath, AvailabilityEntityKind kind, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AvailabilityDateOption>> GetOpenDatesAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AvailabilitySlotOption>> GetSlotsForDateAsync(string projectPath, long openDateId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AvailabilityMatrixRow>> GetDayMatrixAsync(string projectPath, AvailabilityEntityKind kind, long openDateId, IReadOnlyCollection<long> entityIds, CancellationToken cancellationToken = default);
    Task SetLevelAsync(string projectPath, AvailabilityEntityKind kind, IReadOnlyCollection<long> entityIds, long openDateId, long timeSlotId, int level, CancellationToken cancellationToken = default);
}
