using SeminarSched.Application.Questionnaires;
using SeminarSched.Domain.CourseSettings;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.CourseSettings;
using SeminarSched.Infrastructure.MasterData;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Tests;

public sealed class QuestionnaireKitServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SeminarSched.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GenerateAsync_WritesTwoAppsScriptsAtomicallyWithSurveyCompatibleHeaders()
    {
        var path = Path.Combine(_directory, "project.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20)));
        var course = new SqliteCourseSettingsRepository();
        var master = new SqliteMasterDataRepository();
        await master.SaveSubjectAsync(path, new Subject(0, "ES_MATH", "小学校・算数", "算", "小学校", 1));
        await master.SaveSubjectAsync(path, new Subject(0, "JH_MATH", "中学校・数学", "数", "中学校", 2));
        await master.SaveSubjectAsync(path, new Subject(0, "HS_MATH", "高校・数学IA", "数", "高校", 3));

        var output = await new QuestionnaireKitService(course, master).GenerateAsync(path, _directory, "2026夏期講習", "2026夏期講習 個別指導受講申込", "2026夏期講習 非常勤勤務アンケート", "2026-07-06", "2026-07-10", "校舎へお問い合わせください");

        var studentScript = await File.ReadAllTextAsync(Path.Combine(output, "create_student_questionnaire.gs"));
        var teacherScript = await File.ReadAllTextAsync(Path.Combine(output, "create_teacher_questionnaire.gs"));
        Assert.True(File.Exists(Path.Combine(output, "Googleフォーム作成手順.txt")));
        Assert.False(File.Exists(Path.Combine(output, "create_teacher_subject_questionnaire.gs")));

        Assert.Contains("function createStudentQuestionnaire()", studentScript);
        Assert.Contains("受講教科（${schoolLabel}${index}教科目）", studentScript);
        Assert.Contains("受講不可日時（チェックしたコマは受講不可）", studentScript);
        Assert.Contains("\"2026-07-20\"", studentScript);
        Assert.Contains("\"A 17:10～18:30\"", studentScript);
        Assert.Contains("\"算数\"", studentScript);
        Assert.DoesNotContain("小学校・算数", studentScript);

        Assert.Contains("function createTeacherQuestionnaire()", teacherScript);
        Assert.Contains("出勤不可日時（チェックしたコマは出勤不可）", teacherScript);
        Assert.DoesNotContain("\"subjectsBySchoolLevel\"", teacherScript);
        Assert.DoesNotContain("teacher_subject", teacherScript);
        Assert.DoesNotContain("addTeacherSubjectQuestions_", teacherScript);

        Assert.Empty(Directory.GetDirectories(_directory, "*.tmp-*"));
    }

    [Fact]
    public async Task GenerateAsync_MissingSchoolLevelSubjects_Throws()
    {
        var path = Path.Combine(_directory, "project.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20)));
        var course = new SqliteCourseSettingsRepository();
        var master = new SqliteMasterDataRepository();
        await master.SaveSubjectAsync(path, new Subject(0, "ES_MATH", "小学校・算数", "算", "小学校", 1));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new QuestionnaireKitService(course, master).GenerateAsync(path, _directory, "2026夏期講習", "生徒用", "講師用", "2026-07-06", "2026-07-06", "問い合わせ先"));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
