namespace SeminarSched.Domain.MasterData;

public sealed record TeacherQualification(long TeacherId, long SubjectId, bool CanTeach, string Note = "")
{
    public TeacherQualification Normalize()
    {
        if (TeacherId <= 0) throw new ArgumentOutOfRangeException(nameof(TeacherId));
        if (SubjectId <= 0) throw new ArgumentOutOfRangeException(nameof(SubjectId));
        return this with { Note = Note?.Trim() ?? "" };
    }
}
