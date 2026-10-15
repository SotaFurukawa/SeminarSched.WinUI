using System.Text.RegularExpressions;
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
        Assert.Contains("【2026夏期講習】個別指導コースの受講申し込みのフォームです。", studentScript);
        Assert.Contains("申し込み締め切りは7/6(月)です", studentScript);

        Assert.Contains("function createTeacherQuestionnaire()", teacherScript);
        Assert.Contains("出勤不可日時（チェックしたコマは出勤不可）", teacherScript);
        Assert.DoesNotContain("\"subjectsBySchoolLevel\"", teacherScript);
        Assert.DoesNotContain("teacher_subject", teacherScript);
        Assert.DoesNotContain("addTeacherSubjectQuestions_", teacherScript);

        Assert.Empty(Directory.GetDirectories(_directory, "*.tmp-*"));
    }

    [Fact]
    public async Task GenerateAsync_AskEntranceExamQuestionOff_KeepsSplitElementarySubjectChoicesUnchanged()
    {
        var path = Path.Combine(_directory, "project.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20)));
        var course = new SqliteCourseSettingsRepository();
        var master = new SqliteMasterDataRepository();
        await master.SaveSubjectAsync(path, new Subject(0, "ES_MATH_ENTRANCE", "小学校・算数（中学受験）", "算", "小学校", 1));
        await master.SaveSubjectAsync(path, new Subject(0, "ES_MATH", "小学校・算数（中学受験以外なら可能）", "算", "小学校", 2));
        await master.SaveSubjectAsync(path, new Subject(0, "JH_MATH", "中学校・数学", "数", "中学校", 3));
        await master.SaveSubjectAsync(path, new Subject(0, "HS_MATH", "高校・数学IA", "数", "高校", 4));

        var output = await new QuestionnaireKitService(course, master).GenerateAsync(path, _directory, "2026夏期講習", "生徒用", "講師用", "2026-07-06", "2026-07-10", "問い合わせ先");

        var studentScript = await File.ReadAllTextAsync(Path.Combine(output, "create_student_questionnaire.gs"));
        Assert.Contains("\"算数（中学受験）\"", studentScript);
        Assert.Contains("\"算数（中学受験以外）\"", studentScript);
        Assert.Contains("\"askEntranceExamQuestion\": false", studentScript);
    }

    [Fact]
    public async Task GenerateAsync_AskEntranceExamQuestionOn_CombinesElementaryMathAndJapaneseAndAddsDedicatedQuestion()
    {
        var path = Path.Combine(_directory, "project.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20)));
        var course = new SqliteCourseSettingsRepository();
        var master = new SqliteMasterDataRepository();
        await master.SaveSubjectAsync(path, new Subject(0, "ES_MATH_ENTRANCE", "小学校・算数（中学受験）", "算", "小学校", 1));
        await master.SaveSubjectAsync(path, new Subject(0, "ES_MATH", "小学校・算数（中学受験以外なら可能）", "算", "小学校", 2));
        await master.SaveSubjectAsync(path, new Subject(0, "ES_JPN_ENTRANCE", "小学校・国語（中学受験）", "国", "小学校", 3));
        await master.SaveSubjectAsync(path, new Subject(0, "ES_JPN", "小学校・国語（中学受験以外なら可能）", "国", "小学校", 4));
        await master.SaveSubjectAsync(path, new Subject(0, "JH_MATH", "中学校・数学", "数", "中学校", 5));
        await master.SaveSubjectAsync(path, new Subject(0, "HS_MATH", "高校・数学IA", "数", "高校", 6));

        var output = await new QuestionnaireKitService(course, master).GenerateAsync(path, _directory, "2026夏期講習", "生徒用", "講師用", "2026-07-06", "2026-07-10", "問い合わせ先", askEntranceExamQuestion: true);

        var studentScript = await File.ReadAllTextAsync(Path.Combine(output, "create_student_questionnaire.gs"));
        Assert.Contains("\"askEntranceExamQuestion\": true", studentScript);
        Assert.Contains("中学受験をしますか？（必須）", studentScript);
        // 自動判定される小学校ページの選択肢は分割前の「算数」「国語」だけにまとまる
        // （分割済みの名前は他学年ページ用のstudentSubjectChoicesにだけ、学校区分の付記付きで残る）。
        Assert.Contains("\"算数\",", studentScript);
        Assert.Contains("\"国語\"", studentScript);
        Assert.Single(Regex.Matches(studentScript, Regex.Escape("\"算数（中学受験）（小学校）\"")));
        Assert.Single(Regex.Matches(studentScript, Regex.Escape("\"算数（中学受験以外）（小学校）\"")));
        // 「他学年を含む」ページ用の一覧は、このオプションの影響を受けず分割済みのままにする
        // （学校区分の付記はaskEntranceExamQuestionの値に関わらず常に付く）。
        Assert.Contains("\"算数（中学受験）（小学校）\"", studentScript);
        Assert.Contains("\"算数（中学受験以外）（小学校）\"", studentScript);
        Assert.Contains("\"国語（中学受験）（小学校）\"", studentScript);
        Assert.Contains("\"国語（中学受験以外）（小学校）\"", studentScript);
    }

    [Fact]
    public async Task GenerateAsync_ElementaryPageHasExplicitGoToAvailabilityPage()
    {
        // 不具合修正: 小学校ページには以前、明示的な遷移先指定が無く、Googleフォームの既定動作
        // （次に定義されたページへそのまま進む＝中学校ページ）に依存していたため、小学校の回答者が
        // 誤って中学校ページの設問まで続けて見てしまっていた。askEntranceExamQuestionの値に
        // 関わらず常に必要な修正のため、既定（オフ）で生成しても含まれることを確認する。
        var path = Path.Combine(_directory, "project.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20)));
        var course = new SqliteCourseSettingsRepository();
        var master = new SqliteMasterDataRepository();
        await master.SaveSubjectAsync(path, new Subject(0, "ES_MATH", "小学校・算数", "算", "小学校", 1));
        await master.SaveSubjectAsync(path, new Subject(0, "JH_MATH", "中学校・数学", "数", "中学校", 2));
        await master.SaveSubjectAsync(path, new Subject(0, "HS_MATH", "高校・数学IA", "数", "高校", 3));

        var output = await new QuestionnaireKitService(course, master).GenerateAsync(path, _directory, "2026夏期講習", "生徒用", "講師用", "2026-07-06", "2026-07-10", "問い合わせ先");

        var studentScript = await File.ReadAllTextAsync(Path.Combine(output, "create_student_questionnaire.gs"));
        Assert.Contains("elementaryPage.setGoToPage(availabilityPage);", studentScript);
        Assert.DoesNotContain("学校区分は小学校として自動的に扱います", studentScript);
        Assert.DoesNotContain("学校区分は中学校として自動的に扱います", studentScript);
        Assert.DoesNotContain("学校区分は高校として自動的に扱います", studentScript);
    }

    [Fact]
    public async Task GenerateAsync_OtherGradeSubjectChoices_AreSuffixedWithSchoolLevel()
    {
        // ユーザー要望: 「他学年受講を行う場合に、学校区分を選択させてから、科目を選ぶ形式ですが、
        // 科目(学校区分)のように科目を書いておいてほしい。つまり物理（高校）のような感じです。」
        var path = Path.Combine(_directory, "project.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20)));
        var course = new SqliteCourseSettingsRepository();
        var master = new SqliteMasterDataRepository();
        await master.SaveSubjectAsync(path, new Subject(0, "ES_MATH", "小学校・算数", "算", "小学校", 1));
        await master.SaveSubjectAsync(path, new Subject(0, "JH_MATH", "中学校・数学", "数", "中学校", 2));
        await master.SaveSubjectAsync(path, new Subject(0, "HS_PHYSICS", "高校・物理", "物", "高校", 3));

        var output = await new QuestionnaireKitService(course, master).GenerateAsync(path, _directory, "2026夏期講習", "生徒用", "講師用", "2026-07-06", "2026-07-10", "問い合わせ先");

        var studentScript = await File.ReadAllTextAsync(Path.Combine(output, "create_student_questionnaire.gs"));
        Assert.Contains("\"物理（高校）\"", studentScript);
        Assert.Contains("\"算数（小学校）\"", studentScript);
        Assert.Contains("\"数学（中学校）\"", studentScript);
        // 自動判定される高校ページ自体の選択肢は、今まで通り学校区分の付記無しのままにする
        // （このページでは「学校区分」を別途選択させないため付記が不要）。
        Assert.Contains("\"物理\"\n", studentScript.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task GenerateAsync_SeasonLabel_ReplacesHardcodedSummerWordingInLastQuestion()
    {
        // ユーザー要望:「最後の質問は冬期講習なら冬期などになるように修正してください。」
        var path = Path.Combine(_directory, "project.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Winter, new DateOnly(2026, 12, 20), new DateOnly(2026, 12, 20)));
        var course = new SqliteCourseSettingsRepository();
        var master = new SqliteMasterDataRepository();
        await master.SaveSubjectAsync(path, new Subject(0, "ES_MATH", "小学校・算数", "算", "小学校", 1));
        await master.SaveSubjectAsync(path, new Subject(0, "JH_MATH", "中学校・数学", "数", "中学校", 2));
        await master.SaveSubjectAsync(path, new Subject(0, "HS_MATH", "高校・数学IA", "数", "高校", 3));

        var output = await new QuestionnaireKitService(course, master).GenerateAsync(path, _directory, "2026冬期講習", "生徒用", "講師用", "2026-12-06", "2026-12-10", "問い合わせ先", seasonLabel: "冬期");

        var studentScript = await File.ReadAllTextAsync(Path.Combine(output, "create_student_questionnaire.gs"));
        Assert.Contains("\"seasonLabel\": \"冬期\"", studentScript);
        Assert.Contains("\"冬期学力テストを受験する\"", studentScript);
        Assert.Contains("\"冬期学力テストを受験しない\"", studentScript);
        Assert.DoesNotContain("夏期", studentScript);
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
