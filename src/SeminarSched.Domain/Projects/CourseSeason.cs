namespace SeminarSched.Domain.Projects;

public enum CourseSeason
{
    Spring = 1,
    Summer = 2,
    Winter = 3,
    Other = 4,
}

public static class CourseSeasonExtensions
{
    // Otherの場合、実際の名称はCourseProjectDefinition.CustomSeasonName（ホーム画面で入力）が
    // 正本でありTitleへ反映される。ここでの「その他」は、その値を持たない文脈（帳票出力の
    // 補助フィールド等）向けの汎用fallbackに過ぎない。
    public static string ToJapaneseName(this CourseSeason season) => season switch
    {
        CourseSeason.Spring => "春期講習",
        CourseSeason.Summer => "夏期講習",
        CourseSeason.Winter => "冬期講習",
        CourseSeason.Other => "その他",
        _ => throw new ArgumentOutOfRangeException(nameof(season), season, "Unknown course season."),
    };
}
