using Microsoft.Data.Sqlite;
using SeminarSched.Domain.CourseSettings;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.Output;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.CourseSettings;
using SeminarSched.Infrastructure.MasterData;
using SeminarSched.Infrastructure.Output;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Tests;

public sealed class PdfScheduleReportRendererLayoutTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SeminarSched.Tests", Guid.NewGuid().ToString("N"));

    // MigraDocの PageSetup.PageWidth/PageHeight は、Sectionへ設定した直後には0のままで、実際に
    // レンダリングされるまで解決されない（実機で検証済み。以前この前提を誤解し、レンダリング前に
    // section.PageSetup.PageWidthを読んで使用可能幅を計算していたため、全ての列幅が実質0になり
    // 出力が壊れていたことがある）。PdfScheduleReportRendererは代わりに用紙サイズ・向きから
    // 自前で使用可能幅を計算する（GetUsableWidth）。この4パターン全てで内容が正しく収まった
    // 非自明なサイズのPDFが生成されることを確認する。
    [Theory]
    [InlineData(OutputSettings.PaperSizeA4, OutputSettings.OrientationLandscape)]
    [InlineData(OutputSettings.PaperSizeA4, OutputSettings.OrientationPortrait)]
    [InlineData(OutputSettings.PaperSizeA3, OutputSettings.OrientationLandscape)]
    [InlineData(OutputSettings.PaperSizeA3, OutputSettings.OrientationPortrait)]
    public async Task GenerateAsync_AcrossAllPaperSizesAndOrientations_ProducesNonTrivialStudentHandoutPdf(string paperSize, string orientation)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"pdf-layout-{paperSize}-{orientation}.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20)));
        var m = new SqliteMasterDataRepository();
        var student = await m.SaveStudentAsync(path, new Student(0, "S-001", "架空 生徒", "中2"));
        var teacher = await m.SaveTeacherAsync(path, new Teacher(0, "T-001", "架空 講師"));
        var subject = await m.SaveSubjectAsync(path, new Subject(0, "MATH", "数学", "数", "中学", 1));
        var course = new SqliteCourseSettingsRepository();
        var slot = await course.SaveTimeSlotAsync(path, new TimeSlot(0, "1", "1限", new TimeOnly(9, 0), new TimeOnly(10, 0), 1));
        await course.SaveCourseDayAsync(path, new CourseDay(new DateOnly(2026, 7, 20), true, "", [slot.Id]));
        await using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await c.OpenAsync(); await using var q = c.CreateCommand();
            q.CommandText = $"INSERT INTO LessonRequest(Id,ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,1,{student.Id},{subject.Id},1);INSERT INTO Assignment(Id,LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source) SELECT 1,1,{teacher.Id},d.Id,{slot.Id},0,'test' FROM OpenDate d LIMIT 1;";
            await q.ExecuteNonQueryAsync();
        }

        await new SqliteOutputSettingsRepository().SaveAsync(path, new OutputSettings(paperSize, orientation));
        var outputDir = Path.Combine(_directory, $"out-{paperSize}-{orientation}");
        var result = await new SqliteOutputPackageService().GenerateAsync(path, outputDir);

        var bytes = await File.ReadAllBytesAsync(result.StudentHandoutsPdfPath);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        // 使用可能幅が0/負数に落ちて内容が潰れていた回帰では、生成自体は成功しても極端に小さいファイルに
        // なる（テキストがほぼ描画されないため）。実際に内容が収まった場合の目安として十分な下限を置く。
        Assert.True(bytes.Length > 3000, $"Expected a non-trivial PDF for {paperSize}/{orientation}, got {bytes.Length} bytes.");
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
