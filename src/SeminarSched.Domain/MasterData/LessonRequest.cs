namespace SeminarSched.Domain.MasterData;

public sealed record LessonRequest
{
    public LessonRequest(long id, long studentId, long subjectId, int requiredSessions,
        long? regularTeacherId = null, int regularTeacherPriority = 3,
        long? preferredTeacher1Id = null, long? preferredTeacher2Id = null, long? preferredTeacher3Id = null,
        bool oneToOneRequired = false, int? maxConsecutiveSlotsOverride = null, bool? allowGapOverride = null, string note = "")
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (studentId <= 0) throw new ArgumentOutOfRangeException(nameof(studentId));
        if (subjectId <= 0) throw new ArgumentOutOfRangeException(nameof(subjectId));
        if (requiredSessions <= 0) throw new ArgumentOutOfRangeException(nameof(requiredSessions));
        if (regularTeacherPriority is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(regularTeacherPriority));
        if (maxConsecutiveSlotsOverride <= 0) throw new ArgumentOutOfRangeException(nameof(maxConsecutiveSlotsOverride));
        if (regularTeacherId <= 0) regularTeacherId = null;
        if (preferredTeacher1Id <= 0) preferredTeacher1Id = null;
        if (preferredTeacher2Id <= 0) preferredTeacher2Id = null;
        if (preferredTeacher3Id <= 0) preferredTeacher3Id = null;
        Id = id; StudentId = studentId; SubjectId = subjectId; RequiredSessions = requiredSessions;
        RegularTeacherId = regularTeacherId; RegularTeacherPriority = regularTeacherPriority;
        PreferredTeacher1Id = preferredTeacher1Id; PreferredTeacher2Id = preferredTeacher2Id; PreferredTeacher3Id = preferredTeacher3Id;
        OneToOneRequired = oneToOneRequired; MaxConsecutiveSlotsOverride = maxConsecutiveSlotsOverride; AllowGapOverride = allowGapOverride;
        Note = note?.Trim() ?? "";
    }
    public long Id { get; }
    public long StudentId { get; }
    public long SubjectId { get; }
    public int RequiredSessions { get; }
    public long? RegularTeacherId { get; }
    public int RegularTeacherPriority { get; }
    public long? PreferredTeacher1Id { get; }
    public long? PreferredTeacher2Id { get; }
    public long? PreferredTeacher3Id { get; }
    public bool OneToOneRequired { get; }
    public int? MaxConsecutiveSlotsOverride { get; }
    public bool? AllowGapOverride { get; }
    public string Note { get; }
}
