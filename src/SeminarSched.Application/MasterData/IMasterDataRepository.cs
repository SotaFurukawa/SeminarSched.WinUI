using SeminarSched.Domain.MasterData;

namespace SeminarSched.Application.MasterData;

public interface IMasterDataRepository
{
    Task<IReadOnlyList<Student>> GetStudentsAsync(string projectPath, bool includeInactive = true, CancellationToken cancellationToken = default);
    Task<Student> SaveStudentAsync(string projectPath, Student student, CancellationToken cancellationToken = default);
    Task DeleteStudentAsync(string projectPath, long studentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Teacher>> GetTeachersAsync(string projectPath, bool includeInactive = true, CancellationToken cancellationToken = default);
    Task<Teacher> SaveTeacherAsync(string projectPath, Teacher teacher, CancellationToken cancellationToken = default);
    Task DeleteTeacherAsync(string projectPath, long teacherId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Subject>> GetSubjectsAsync(string projectPath, bool includeInactive = true, CancellationToken cancellationToken = default);
    Task<Subject> SaveSubjectAsync(string projectPath, Subject subject, CancellationToken cancellationToken = default);
    Task SaveQualificationAsync(string projectPath, TeacherQualification qualification, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeacherQualification>> GetQualificationsAsync(string projectPath, CancellationToken cancellationToken = default);
    Task SaveRegularLessonAsync(string projectPath, RegularLessonProfile profile, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RegularLessonProfile>> GetRegularLessonsAsync(string projectPath, CancellationToken cancellationToken = default);
    Task SaveLessonRequestAsync(string projectPath, LessonRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LessonRequest>> GetLessonRequestsAsync(string projectPath, CancellationToken cancellationToken = default);
    Task DeleteLessonRequestAsync(string projectPath, long studentId, long subjectId, CancellationToken cancellationToken = default);
}
