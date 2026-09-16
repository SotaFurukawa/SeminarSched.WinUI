namespace SeminarSched.Domain.Projects;

public enum CourseSeason
{
    Spring = 1,
    Summer = 2,
    Winter = 3,
}

public static class CourseSeasonExtensions
{
    public static string ToJapaneseName(this CourseSeason season) => season switch
    {
        CourseSeason.Spring => "春期講習",
        CourseSeason.Summer => "夏期講習",
        CourseSeason.Winter => "冬期講習",
        _ => throw new ArgumentOutOfRangeException(nameof(season), season, "Unknown course season."),
    };
}
