namespace SeminarSched.Domain.Projects;

public sealed record CourseProjectDefinition
{
    private CourseProjectDefinition(
        int academicYear,
        CourseSeason season,
        DateOnly startDate,
        DateOnly endDate,
        bool considerGroupLessons,
        string? customSeasonName)
    {
        AcademicYear = academicYear;
        Season = season;
        StartDate = startDate;
        EndDate = endDate;
        ConsiderGroupLessons = considerGroupLessons;
        CustomSeasonName = customSeasonName;
    }

    public int AcademicYear { get; }

    public CourseSeason Season { get; }

    public DateOnly StartDate { get; }

    public DateOnly EndDate { get; }

    // ホーム画面「新しい講習プロジェクト」の「集団授業の日程を考慮する」チェックボックス由来。
    // オンの場合のみ③アンケート取込みに3.1/3.2（集団授業クラスの登録・受講登録）を表示する。
    public bool ConsiderGroupLessons { get; }

    // Season==Otherのときだけ意味を持つ、ホーム画面で入力する講習区分の名称。それ以外はnull。
    public string? CustomSeasonName { get; }

    public string Title => $"{AcademicYear}{(Season == CourseSeason.Other ? CustomSeasonName : Season.ToJapaneseName())}";

    public static CourseProjectDefinition Create(
        int academicYear,
        CourseSeason season,
        DateOnly startDate,
        DateOnly endDate,
        bool considerGroupLessons = false,
        string? customSeasonName = null)
    {
        if (academicYear is < 2000 or > 2200)
        {
            throw new ArgumentOutOfRangeException(nameof(academicYear));
        }

        if (!Enum.IsDefined(season))
        {
            throw new ArgumentOutOfRangeException(nameof(season));
        }

        if (season == CourseSeason.Other)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(customSeasonName);
        }

        if (endDate < startDate)
        {
            throw new ArgumentException("The end date must not be before the start date.", nameof(endDate));
        }

        if (endDate.DayNumber - startDate.DayNumber > 180)
        {
            throw new ArgumentException("A course project cannot span more than 181 days.", nameof(endDate));
        }

        return new CourseProjectDefinition(academicYear, season, startDate, endDate, considerGroupLessons, season == CourseSeason.Other ? customSeasonName!.Trim() : null);
    }
}
