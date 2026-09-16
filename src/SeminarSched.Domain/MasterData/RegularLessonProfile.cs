namespace SeminarSched.Domain.MasterData;

public sealed record RegularLessonProfile
{
    public RegularLessonProfile(long id, long studentId, long subjectId, long? regularTeacherId,
        int regularTeacherPriority = 3, bool oneToOneRequired = false, string note = "")
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (studentId <= 0) throw new ArgumentOutOfRangeException(nameof(studentId));
        if (subjectId <= 0) throw new ArgumentOutOfRangeException(nameof(subjectId));
        if (regularTeacherId <= 0) regularTeacherId = null;
        if (regularTeacherPriority is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(regularTeacherPriority));
        Id = id; StudentId = studentId; SubjectId = subjectId; RegularTeacherId = regularTeacherId;
        RegularTeacherPriority = regularTeacherPriority; OneToOneRequired = oneToOneRequired; Note = note?.Trim() ?? "";
    }
    public long Id { get; }
    public long StudentId { get; }
    public long SubjectId { get; }
    public long? RegularTeacherId { get; }
    public int RegularTeacherPriority { get; }
    public bool OneToOneRequired { get; }
    public string Note { get; }
}
