namespace SeminarSched.Domain.Projects;

public sealed record CourseProjectDefinition
{
    private CourseProjectDefinition(
        int academicYear,
        CourseSeason season,
        DateOnly startDate,
        DateOnly endDate)
    {
        AcademicYear = academicYear;
        Season = season;
        StartDate = startDate;
        EndDate = endDate;
    }

    public int AcademicYear { get; }

    public CourseSeason Season { get; }

    public DateOnly StartDate { get; }

    public DateOnly EndDate { get; }

    public string Title => $"{AcademicYear}{Season.ToJapaneseName()}";

    public static CourseProjectDefinition Create(
        int academicYear,
        CourseSeason season,
        DateOnly startDate,
        DateOnly endDate)
    {
        if (academicYear is < 2000 or > 2200)
        {
            throw new ArgumentOutOfRangeException(nameof(academicYear));
        }

        if (!Enum.IsDefined(season))
        {
            throw new ArgumentOutOfRangeException(nameof(season));
        }

        if (endDate < startDate)
        {
            throw new ArgumentException("The end date must not be before the start date.", nameof(endDate));
        }

        if (endDate.DayNumber - startDate.DayNumber > 180)
        {
            throw new ArgumentException("A course project cannot span more than 181 days.", nameof(endDate));
        }

        return new CourseProjectDefinition(academicYear, season, startDate, endDate);
    }
}
