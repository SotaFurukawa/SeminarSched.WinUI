using SeminarSched.Domain.Projects;

namespace SeminarSched.Application.Projects;

public sealed record ProjectSummary(
    string Path,
    string Title,
    int AcademicYear,
    CourseSeason Season,
    DateOnly StartDate,
    DateOnly EndDate,
    int WorkflowCompletedStep);
