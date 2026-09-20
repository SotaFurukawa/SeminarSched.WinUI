using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using SeminarSched.Domain.CourseSettings;
using SeminarSched.Domain.GroupLessons;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.CourseSettings;
using SeminarSched.Infrastructure.GroupLessons;
using SeminarSched.Infrastructure.MasterData;
using SeminarSched.Infrastructure.Output;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Tests;

public sealed class SqliteOutputPackageServiceTests : IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),"SeminarSched.Tests",Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GenerateAsync_CreatesAllFiveReportKindsAtomically()
    {
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"report.jukuschedule");await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));
        var m=new SqliteMasterDataRepository();var st=await m.SaveStudentAsync(path,new Student(0,"S-001","架空 生徒","中2"));var te=await m.SaveTeacherAsync(path,new Teacher(0,"T-001","架空 講師"));var sub=await m.SaveSubjectAsync(path,new Subject(0,"MATH","数学","数","中学",1));var course=new SqliteCourseSettingsRepository();var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));
        await using(var c=new SqliteConnection($"Data Source={path};Pooling=False")){await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText=$"INSERT INTO LessonRequest(Id,ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,1,{st.Id},{sub.Id},1);INSERT INTO Assignment(Id,LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source) SELECT 1,1,{te.Id},d.Id,{slot.Id},0,'test' FROM OpenDate d LIMIT 1;";await q.ExecuteNonQueryAsync();}

        var result=await new SqliteOutputPackageService().GenerateAsync(path,_directory);
        foreach(var xlsx in new[]{result.OverallExcelPath,result.StudentHandoutsExcelPath,result.TeacherHandoutsExcelPath,result.IssuesExcelPath})
            Assert.True(new FileInfo(xlsx).Length>500,$"{xlsx} should be a non-trivial workbook.");
        foreach(var pdf in new[]{result.OverallPdfPath,result.StudentHandoutsPdfPath,result.TeacherHandoutsPdfPath,result.IssuesPdfPath})
        {
            var bytes=await File.ReadAllBytesAsync(pdf);Assert.Equal("%PDF",System.Text.Encoding.ASCII.GetString(bytes,0,4));
        }
        Assert.Empty(Directory.GetDirectories(_directory,"*.tmp-*",SearchOption.AllDirectories));

        // ユーザー指示: 出力はプロジェクトごとにフォルダを分ける。ファイル名は既定パターン
        // {project}-{report}（例: 2026年度夏期講習-全体時間割.xlsx）になる。
        var projectFolder=Path.Combine(_directory,"2026年度夏期講習");
        Assert.Equal(projectFolder,Path.GetDirectoryName(result.DirectoryPath));
        Assert.Equal("2026年度夏期講習-全体時間割.xlsx",Path.GetFileName(result.OverallExcelPath));
        Assert.Equal("2026年度夏期講習-全体時間割.pdf",Path.GetFileName(result.OverallPdfPath));
        Assert.Equal("2026年度夏期講習-講師配布用講師別時間割(一括).xlsx",Path.GetFileName(result.CombinedTeacherPacketExcelPath));
        Assert.True(new FileInfo(result.CombinedTeacherPacketExcelPath).Length>500);
        Assert.True(new FileInfo(result.CombinedTeacherPacketPdfPath).Length>0);

        using var overall=new XLWorkbook(result.OverallExcelPath);
        Assert.True(overall.Worksheets.Contains("出力情報"));
        Assert.Equal("全体時間割",overall.Worksheet("出力情報").Cell(1,2).GetString());
        Assert.Contains(overall.Worksheets,ws=>ws.Name.StartsWith("週_",StringComparison.Ordinal));

        using var studentHandouts=new XLWorkbook(result.StudentHandoutsExcelPath);
        Assert.Contains(studentHandouts.Worksheets,ws=>ws.Name=="中2_架空 生徒");
        var handoutSheet=studentHandouts.Worksheet("中2_架空 生徒");
        var studentCells=handoutSheet.CellsUsed().Select(cell=>cell.GetString()).ToArray();
        Assert.Contains(studentCells,text=>text=="数");
        Assert.DoesNotContain(studentCells,text=>text.Contains("架空",StringComparison.Ordinal)&&text.Contains("数",StringComparison.Ordinal));

        // ユーザー指定の書式（1ページ形式の生徒配布時間割）: 列幅・1行目の反転配色・氏名の文字サイズ・
        // 学年欄の左右揃え・曜日=9pt・日の背景色・学力テスト行の背景色をピンポイントで検証する。
        Assert.Equal(Math.Round((54-5)/7.0,2),handoutSheet.Column(1).Width);
        Assert.Equal(Math.Round((96-5)/7.0,2),handoutSheet.Column(2).Width);
        Assert.Equal(Math.Round((64-5)/7.0,2),handoutSheet.Column(3).Width);
        var handoutTitle=handoutSheet.Cell(1,1);
        Assert.Equal(XLColor.Black,handoutTitle.Style.Fill.BackgroundColor);
        Assert.Equal(XLColor.White,handoutTitle.Style.Font.FontColor);
        Assert.Equal("BIZ UDPMincho Medium",handoutTitle.Style.Font.FontName);
        Assert.Equal(16,handoutTitle.Style.Font.FontSize);
        Assert.Equal(XLAlignmentHorizontalValues.Right,handoutSheet.Cell(4,2).Style.Alignment.Horizontal);
        Assert.Equal(XLAlignmentHorizontalValues.Left,handoutSheet.Cell(4,4).Style.Alignment.Horizontal);
        Assert.Equal(14,handoutSheet.Cell(4,6).Style.Font.FontSize);
        Assert.Equal("HG丸ゴシックM-PRO",handoutSheet.Cell(7,3).Style.Font.FontName);
        Assert.Equal(9,handoutSheet.Cell(8,3).Style.Font.FontSize);
        Assert.Equal(9,handoutSheet.Cell(9,3).Style.Font.FontSize);
        Assert.Equal(XLColor.FromHtml("#90CAFE"),handoutSheet.Cell(9,3).Style.Fill.BackgroundColor);
        Assert.Equal(XLColor.FromHtml("#BFBFBF"),handoutSheet.Cell(7,1).Style.Fill.BackgroundColor);
        var academicTestRow=handoutSheet.CellsUsed().First(cell=>cell.GetString().Contains("学力テスト",StringComparison.Ordinal)).Address.RowNumber;
        Assert.Equal(XLColor.FromHtml("#95B3D7"),handoutSheet.Cell(academicTestRow,1).Style.Fill.BackgroundColor);
        Assert.Equal(XLColor.FromHtml("#95B3D7"),handoutSheet.Cell(academicTestRow,9).Style.Fill.BackgroundColor);

        using var teacherHandouts=new XLWorkbook(result.TeacherHandoutsExcelPath);
        var teacherCells=teacherHandouts.Worksheet("中2_架空 生徒").CellsUsed().Select(cell=>cell.GetString()).ToArray();
        Assert.Contains(teacherCells,text=>text.Contains("数",StringComparison.Ordinal)&&text.Contains("架空",StringComparison.Ordinal));

        using var issues=new XLWorkbook(result.IssuesExcelPath);
        Assert.True(issues.Worksheets.Contains("未配置一覧"));Assert.True(issues.Worksheets.Contains("警告一覧"));
        Assert.Equal("生徒",issues.Worksheet("未配置一覧").Cell(1,1).GetString());
        Assert.Equal("severity",issues.Worksheet("警告一覧").Cell(1,1).GetString());

        Assert.True(Directory.Exists(result.TeacherPacketDirectory));
        var teacherPacket=Path.Combine(result.TeacherPacketDirectory,"架空 講師t.xlsx");
        Assert.True(File.Exists(teacherPacket));
        using var packet=new XLWorkbook(teacherPacket);
        Assert.Contains(packet.Worksheets,ws=>ws.Name.EndsWith("_講師別",StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenerateAsync_MultiWeekPeriod_RendersPerWeekOverviewSheetsAndAbsenceList()
    {
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"multiweek.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,8,10)));
        var m=new SqliteMasterDataRepository();
        var s1=await m.SaveStudentAsync(path,new Student(0,"S-001","田中 太郎","中2"));
        var s2=await m.SaveStudentAsync(path,new Student(0,"S-002","田中 次郎","中1"));
        await m.SaveStudentAsync(path,new Student(0,"S-003","架空 欠席生徒","中3"));
        var te=await m.SaveTeacherAsync(path,new Teacher(0,"T-001","架空 講師"));
        var sub=await m.SaveSubjectAsync(path,new Subject(0,"MATH","数学","数","中学",1));
        var course=new SqliteCourseSettingsRepository();
        var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));
        await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));
        await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,8,3),true,"",[slot.Id]));
        await using(var c=new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await c.OpenAsync();await using var q=c.CreateCommand();
            q.CommandText=$"""
                INSERT INTO LessonRequest(Id,ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,1,{s1.Id},{sub.Id},2),(2,1,{s2.Id},{sub.Id},1);
                INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source)
                  SELECT 1,{te.Id},d.Id,{slot.Id},0,'test' FROM OpenDate d WHERE d.Date='2026-07-20';
                INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source)
                  SELECT 1,{te.Id},d.Id,{slot.Id},0,'test' FROM OpenDate d WHERE d.Date='2026-08-03';
                INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source)
                  SELECT 2,{te.Id},d.Id,{slot.Id},0,'test' FROM OpenDate d WHERE d.Date='2026-07-20';
                """;
            await q.ExecuteNonQueryAsync();
        }

        var result=await new SqliteOutputPackageService().GenerateAsync(path,_directory);

        using var studentHandouts=new XLWorkbook(result.StudentHandoutsExcelPath);
        Assert.Contains(studentHandouts.Worksheets,ws=>ws.Name=="中2_田中 太郎");
        Assert.Contains(studentHandouts.Worksheets,ws=>ws.Name=="中1_田中 次郎");
        Assert.True(studentHandouts.Worksheets.Contains("講習欠席一覧"));
        var absenceCells=studentHandouts.Worksheet("講習欠席一覧").CellsUsed().Select(cell=>cell.GetString()).ToArray();
        Assert.Contains(absenceCells,text=>text=="架空 欠席生徒");

        using var overall=new XLWorkbook(result.OverallExcelPath);
        var weekSheets=overall.Worksheets.Where(ws=>ws.Name.StartsWith("週_",StringComparison.Ordinal)).ToArray();
        Assert.True(weekSheets.Length>=2,"Expected at least one week sheet per week containing an assignment.");
        var overviewCells=weekSheets.SelectMany(ws=>ws.CellsUsed()).Select(cell=>cell.GetString()).ToArray();
        Assert.Contains(overviewCells,text=>text=="架空");
        Assert.Contains(overviewCells,text=>text=="数");
    }

    [Fact]
    public async Task GenerateAsync_ReportsRegularTeacherShortfallAsWarningRow()
    {
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"shortfall.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,21)));
        var m=new SqliteMasterDataRepository();
        var student=await m.SaveStudentAsync(path,new Student(0,"S-001","架空 生徒","中2"));
        var regular=await m.SaveTeacherAsync(path,new Teacher(0,"T-REG","架空 通常担当"));
        var substitute=await m.SaveTeacherAsync(path,new Teacher(0,"T-SUB","架空 代講"));
        var sub=await m.SaveSubjectAsync(path,new Subject(0,"MATH","数学","数","中学",1));
        await m.SaveQualificationAsync(path,new TeacherQualification(regular.Id,sub.Id,true));
        await m.SaveQualificationAsync(path,new TeacherQualification(substitute.Id,sub.Id,true));
        await m.SaveRegularLessonAsync(path,new RegularLessonProfile(0,student.Id,sub.Id,regular.Id,regularTeacherPriority:5));
        var course=new SqliteCourseSettingsRepository();
        var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));
        await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));
        await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,21),true,"",[slot.Id]));
        await using(var c=new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await c.OpenAsync();await using var q=c.CreateCommand();
            q.CommandText=$"""
                INSERT INTO LessonRequest(Id,ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,1,{student.Id},{sub.Id},2);
                INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source)
                  SELECT 1,{substitute.Id},d.Id,{slot.Id},0,'test' FROM OpenDate d WHERE d.Date='2026-07-20';
                """;
            await q.ExecuteNonQueryAsync();
        }

        var result=await new SqliteOutputPackageService().GenerateAsync(path,_directory);
        using var issues=new XLWorkbook(result.IssuesExcelPath);
        var warningSheet=issues.Worksheet("警告一覧");
        var warningRow=warningSheet.RowsUsed().Skip(1).First();
        Assert.Equal("通常担当不足",warningRow.Cell(2).GetString());
        Assert.Contains("架空 通常担当",warningRow.Cell(6).GetString());
        Assert.Contains("目標2回中0回",warningRow.Cell(7).GetString());
        // 科目名は略称(ShortName)のみで表示する（「数学」ではなく「数」）。全xlsxで統一する仕様。
        Assert.StartsWith("数：通常担当",warningRow.Cell(7).GetString());

        Assert.True(Directory.Exists(result.TeacherPacketDirectory));
        var substituteFile=Path.Combine(result.TeacherPacketDirectory,"架空 代講t.xlsx");
        Assert.True(File.Exists(substituteFile));
        using var substituteWorkbook=new XLWorkbook(substituteFile);
        Assert.Contains(substituteWorkbook.Worksheets,ws=>ws.Name.EndsWith("_講師別",StringComparison.Ordinal));

        var substitutePdf=Path.Combine(result.TeacherPacketDirectory,"架空 代講t.pdf");
        Assert.True(File.Exists(substitutePdf));
        var pdfBytes=await File.ReadAllBytesAsync(substitutePdf);
        Assert.Equal("%PDF",System.Text.Encoding.ASCII.GetString(pdfBytes,0,4));
    }

    [Fact]
    public async Task GenerateAsync_OverviewGrid_GraysOutUnavailableSlotForTeacherWithOtherAssignmentsThatDay()
    {
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"unavailable.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));
        var m=new SqliteMasterDataRepository();
        var student=await m.SaveStudentAsync(path,new Student(0,"S-001","架空 生徒","中2"));
        var teacher=await m.SaveTeacherAsync(path,new Teacher(0,"T-001","架空 講師"));
        var sub=await m.SaveSubjectAsync(path,new Subject(0,"MATH","数学","数","中学",1));
        var course=new SqliteCourseSettingsRepository();
        var slot1=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));
        var slot2=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"2","2限",new TimeOnly(10,0),new TimeOnly(11,0),2));
        await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot1.Id,slot2.Id]));
        await using(var c=new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await c.OpenAsync();await using var q=c.CreateCommand();
            q.CommandText=$"""
                INSERT INTO LessonRequest(Id,ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,1,{student.Id},{sub.Id},1);
                INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source)
                  SELECT 1,{teacher.Id},d.Id,{slot1.Id},0,'test' FROM OpenDate d WHERE d.Date='2026-07-20';
                INSERT INTO TeacherUnavailability(TeacherId,OpenDateId,TimeSlotId)
                  SELECT {teacher.Id},d.Id,{slot2.Id} FROM OpenDate d WHERE d.Date='2026-07-20';
                """;
            await q.ExecuteNonQueryAsync();
        }

        var result=await new SqliteOutputPackageService().GenerateAsync(path,_directory);
        using var workbook=new XLWorkbook(result.OverallExcelPath);
        var overview=workbook.Worksheets.First(ws=>ws.Name.StartsWith("週_",StringComparison.Ordinal));
        // The project also carries the default A/B/C slots seeded by SqliteProjectRepository.CreateAsync
        // alongside this test's own "1限"/"2限" slots, so slot rows must be located by label text rather
        // than by an assumed row offset from one another. The overview grid's slot label cell is
        // "{code}\n{start}–{end}" (code = TimeSlot.Code, here "1"/"2", not the DisplayName "1限"/"2限").
        var unavailableFill=XLColor.FromHtml("#D9D9D9");
        var slot1Row=overview.CellsUsed().First(cell=>cell.GetString()=="1\n09:00–10:00").Address.RowNumber;
        var slot2Row=overview.CellsUsed().First(cell=>cell.GetString()=="2\n10:00–11:00").Address.RowNumber;
        var teacherCol=overview.CellsUsed().First(cell=>cell.GetString()=="架空").Address.ColumnNumber;
        Assert.Equal("中2",overview.Cell(slot1Row,teacherCol).GetString());
        Assert.Equal(string.Empty,overview.Cell(slot2Row,teacherCol).GetString());
        Assert.Equal(unavailableFill,overview.Cell(slot2Row,teacherCol).Style.Fill.BackgroundColor);
        Assert.NotEqual(unavailableFill,overview.Cell(slot1Row,teacherCol).Style.Fill.BackgroundColor);
    }

    [Fact]
    public async Task GenerateAsync_TeacherWithUnavailabilityButNoAssignments_DoesNotThrow()
    {
        // 実機ログで再現したクラッシュの再現テスト: 出勤不可情報だけ登録され、最終的に一度も配置されなかった
        // 講師がいると、全体時間割のteacherLabels（report.Rowsのみから構築）にその講師のキーが無く
        // KeyNotFoundExceptionになっていた（ExcelScheduleReportRenderer.RenderOverall）。
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"unassigned-unavailable.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));
        var m=new SqliteMasterDataRepository();
        var student=await m.SaveStudentAsync(path,new Student(0,"S-001","架空 生徒","中2"));
        var assignedTeacher=await m.SaveTeacherAsync(path,new Teacher(0,"T-001","架空 講師一"));
        var unassignedTeacher=await m.SaveTeacherAsync(path,new Teacher(0,"T-002","架空 講師二"));
        var sub=await m.SaveSubjectAsync(path,new Subject(0,"MATH","数学","数","中学",1));
        var course=new SqliteCourseSettingsRepository();
        var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));
        await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));
        await using(var c=new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await c.OpenAsync();await using var q=c.CreateCommand();
            q.CommandText=$"""
                INSERT INTO LessonRequest(Id,ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,1,{student.Id},{sub.Id},1);
                INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source)
                  SELECT 1,{assignedTeacher.Id},d.Id,{slot.Id},0,'test' FROM OpenDate d WHERE d.Date='2026-07-20';
                INSERT INTO TeacherUnavailability(TeacherId,OpenDateId,TimeSlotId)
                  SELECT {unassignedTeacher.Id},d.Id,{slot.Id} FROM OpenDate d WHERE d.Date='2026-07-20';
                """;
            await q.ExecuteNonQueryAsync();
        }

        var result=await new SqliteOutputPackageService().GenerateAsync(path,_directory);
        Assert.True(new FileInfo(result.OverallExcelPath).Length>500);
    }

    [Fact]
    public async Task GenerateAsync_OverviewGrid_SharesOneComaLabelColumnAcrossAllDaysInAWeek()
    {
        // ユーザー指示によるPython版からの意図的な差分: 週の中に複数日・複数講師の出勤があっても、
        // 「コマ」ラベル（時刻テキスト）は週の先頭（A列）に1回だけ置き、日付ごとに繰り返さない。
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"shared-coma-label.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,21)));
        var m=new SqliteMasterDataRepository();
        var student=await m.SaveStudentAsync(path,new Student(0,"S-001","架空 生徒","中2"));
        var teacher1=await m.SaveTeacherAsync(path,new Teacher(0,"T-001","架空 一郎"));
        var teacher2=await m.SaveTeacherAsync(path,new Teacher(0,"T-002","架空 二郎"));
        var sub=await m.SaveSubjectAsync(path,new Subject(0,"MATH","数学","数","中学",1));
        var course=new SqliteCourseSettingsRepository();
        var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));
        await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));
        await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,21),true,"",[slot.Id]));
        await using(var c=new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await c.OpenAsync();await using var q=c.CreateCommand();
            q.CommandText=$"""
                INSERT INTO LessonRequest(Id,ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,1,{student.Id},{sub.Id},2);
                INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source)
                  SELECT 1,{teacher1.Id},d.Id,{slot.Id},0,'test' FROM OpenDate d WHERE d.Date='2026-07-20';
                INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source)
                  SELECT 1,{teacher2.Id},d.Id,{slot.Id},0,'test' FROM OpenDate d WHERE d.Date='2026-07-21';
                """;
            await q.ExecuteNonQueryAsync();
        }

        var result=await new SqliteOutputPackageService().GenerateAsync(path,_directory);
        using var overall=new XLWorkbook(result.OverallExcelPath);
        var week=overall.Worksheets.First(ws=>ws.Name.StartsWith("週_",StringComparison.Ordinal));
        var labelCells=week.CellsUsed().Where(cell=>cell.GetString()=="1\n09:00–10:00").ToArray();
        var labelCell=Assert.Single(labelCells);
        Assert.Equal(1,labelCell.Address.ColumnNumber);
        Assert.Equal(XLAlignmentHorizontalValues.Center,labelCell.Style.Alignment.Horizontal);
        Assert.Contains(week.CellsUsed(),cell=>cell.GetString()=="架空一");
        Assert.Contains(week.CellsUsed(),cell=>cell.GetString()=="架空二");
        Assert.Equal(Math.Round((45-5)/7.0,2),week.Column(1).Width);
        Assert.Equal(Math.Round((30-5)/7.0,2),week.Column(2).Width);
    }

    [Fact]
    public async Task GenerateAsync_HandoutStyleSheet_RoundTripsCustomizationFromPreviousOutput()
    {
        // 生徒配布xlsxの「デザイン設定」シートは編集・保存可能なテンプレートであり、
        // 次回以降の出力（同じ出力先フォルダ）でその値を読み戻して反映する。既定値のまま何も編集
        // していない場合は既定値が維持されることも合わせて確認する。
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"style-roundtrip.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));
        var m=new SqliteMasterDataRepository();var st=await m.SaveStudentAsync(path,new Student(0,"S-001","架空 生徒","中2"));var te=await m.SaveTeacherAsync(path,new Teacher(0,"T-001","架空 講師"));var sub=await m.SaveSubjectAsync(path,new Subject(0,"MATH","数学","数","中学",1));var course=new SqliteCourseSettingsRepository();var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));
        await using(var c=new SqliteConnection($"Data Source={path};Pooling=False")){await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText=$"INSERT INTO LessonRequest(Id,ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,1,{st.Id},{sub.Id},1);INSERT INTO Assignment(Id,LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source) SELECT 1,1,{te.Id},d.Id,{slot.Id},0,'test' FROM OpenDate d LIMIT 1;";await q.ExecuteNonQueryAsync();}

        var firstResult=await new SqliteOutputPackageService().GenerateAsync(path,_directory);
        using(var firstWorkbook=new XLWorkbook(firstResult.StudentHandoutsExcelPath))
        {
            Assert.True(firstWorkbook.Worksheets.Contains("デザイン設定"));
            var styleSheet=firstWorkbook.Worksheet("デザイン設定");
            var nameSizeRow=styleSheet.CellsUsed().First(cell=>cell.Address.ColumnNumber==1&&cell.GetString()=="氏名の文字サイズ").Address.RowNumber;
            var academicTestColorRow=styleSheet.CellsUsed().First(cell=>cell.Address.ColumnNumber==1&&cell.GetString()=="学力テスト行の背景色").Address.RowNumber;
            Assert.Equal("14",styleSheet.Cell(nameSizeRow,2).GetString());
            Assert.Equal("#95B3D7",styleSheet.Cell(academicTestColorRow,2).GetString());
            // 編集: 氏名の文字サイズと学力テスト行の背景色を書き換えて保存する（校舎側の操作を模擬）。
            styleSheet.Cell(academicTestColorRow,2).Value="#FF0000";
            styleSheet.Cell(nameSizeRow,2).Value="20";
            firstWorkbook.Save();
        }

        var secondResult=await new SqliteOutputPackageService().GenerateAsync(path,_directory);
        using var secondWorkbook=new XLWorkbook(secondResult.StudentHandoutsExcelPath);
        var handoutSheet=secondWorkbook.Worksheet("中2_架空 生徒");
        Assert.Equal(20,handoutSheet.Cell(4,6).Style.Font.FontSize);
        var academicTestRow=handoutSheet.CellsUsed().First(cell=>cell.GetString().Contains("学力テスト",StringComparison.Ordinal)).Address.RowNumber;
        Assert.Equal(XLColor.FromHtml("#FF0000"),handoutSheet.Cell(academicTestRow,1).Style.Fill.BackgroundColor);
        // 出力し直したテンプレートシート自体も、直前に編集した値をそのまま引き継いで表示する。
        var secondStyleSheet=secondWorkbook.Worksheet("デザイン設定");
        var secondNameSizeRow=secondStyleSheet.CellsUsed().First(cell=>cell.Address.ColumnNumber==1&&cell.GetString()=="氏名の文字サイズ").Address.RowNumber;
        Assert.Equal("20",secondStyleSheet.Cell(secondNameSizeRow,2).GetString());
        // 編集していない項目（タイトルのフォント名等）は既定値のまま維持される。
        var titleFontRow=secondStyleSheet.CellsUsed().First(cell=>cell.Address.ColumnNumber==1&&cell.GetString()=="タイトル文字のフォント名").Address.RowNumber;
        Assert.Equal("BIZ UDPMincho Medium",secondStyleSheet.Cell(titleFontRow,2).GetString());
    }

    [Fact]
    public async Task GenerateAsync_StudentAttendingGroupLesson_ShowsBlackGroupLessonCellOnHandout()
    {
        // ユーザー指示: 集団授業を受講する生徒の集団授業がある時間帯は、個別指導ページ上でも
        // そのマス目を黒塗り・白文字「集団」で表示する（全体時間割は変更不要、個別のページのみ対象）。
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"group-lesson-overlay.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,21)));
        var m=new SqliteMasterDataRepository();
        var student=await m.SaveStudentAsync(path,new Student(0,"S-001","架空 生徒","中2"));
        var teacher=await m.SaveTeacherAsync(path,new Teacher(0,"T-001","架空 講師"));
        var sub=await m.SaveSubjectAsync(path,new Subject(0,"MATH","数学","数","中学",1));
        var course=new SqliteCourseSettingsRepository();
        var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));
        await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));
        await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,21),true,"",[slot.Id]));
        long openDate2Id;
        await using(var c=new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await c.OpenAsync();
            await using(var q=c.CreateCommand()){q.CommandText=$"INSERT INTO LessonRequest(Id,ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,1,{student.Id},{sub.Id},1);INSERT INTO Assignment(Id,LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source) SELECT 1,1,{teacher.Id},d.Id,{slot.Id},0,'test' FROM OpenDate d WHERE d.Date='2026-07-20';";await q.ExecuteNonQueryAsync();}
            await using(var q=c.CreateCommand()){q.CommandText="SELECT Id FROM OpenDate WHERE Date='2026-07-21';";openDate2Id=(long)(await q.ExecuteScalarAsync())!;}
        }

        var groupLessons=new SqliteGroupLessonService();
        var groupClass=await groupLessons.SaveClassAsync(path,new GroupLessonClass(0,"中2A","中2","理科"));
        await groupLessons.AddSessionsAsync(path,groupClass.Id,[openDate2Id],new TimeOnly(9,0),new TimeOnly(10,0));
        await groupLessons.SetEnrollmentAsync(path,groupClass.Id,student.Id,true);

        var result=await new SqliteOutputPackageService().GenerateAsync(path,_directory);
        using var studentHandouts=new XLWorkbook(result.StudentHandoutsExcelPath);
        var handoutSheet=studentHandouts.Worksheet("中2_架空 生徒");

        var individualCell=handoutSheet.CellsUsed().Single(cell=>cell.GetString()=="数");
        Assert.NotEqual(XLColor.Black,individualCell.Style.Fill.BackgroundColor);

        var groupCell=handoutSheet.CellsUsed().Single(cell=>cell.GetString()=="集団");
        Assert.Equal(XLColor.Black,groupCell.Style.Fill.BackgroundColor);
        Assert.Equal(XLColor.White,groupCell.Style.Font.FontColor);
        Assert.NotEqual(individualCell.Address,groupCell.Address);

        // 全体時間割は変更不要（既存仕様のまま。集団授業のセルが紛れ込んでいないことを確認）。
        using var overall=new XLWorkbook(result.OverallExcelPath);
        var overallWeek=overall.Worksheets.First(ws=>ws.Name.StartsWith("週_",StringComparison.Ordinal));
        Assert.DoesNotContain(overallWeek.CellsUsed(),cell=>cell.GetString()=="集団");
    }

    [Fact]
    public async Task GenerateAsync_SubjectWithoutExplicitShortName_StillUsesOneCharacterAbbreviation()
    {
        // ユーザー指示: 科目の略称（一文字）はPython版と同様、Subject.ShortNameが未入力でも
        // 表示名からきちんと一文字の略称になっている必要がある（フルネームへフォールバックしない）。
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"subject-abbreviation.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));
        var m=new SqliteMasterDataRepository();var st=await m.SaveStudentAsync(path,new Student(0,"S-001","架空 生徒","中2"));var te=await m.SaveTeacherAsync(path,new Teacher(0,"T-001","架空 講師"));
        var sub=await m.SaveSubjectAsync(path,new Subject(0,"ENG","英語","","中学",1)); // ShortName未入力
        var course=new SqliteCourseSettingsRepository();var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));
        await using(var c=new SqliteConnection($"Data Source={path};Pooling=False")){await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText=$"INSERT INTO LessonRequest(Id,ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,1,{st.Id},{sub.Id},1);INSERT INTO Assignment(Id,LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source) SELECT 1,1,{te.Id},d.Id,{slot.Id},0,'test' FROM OpenDate d LIMIT 1;";await q.ExecuteNonQueryAsync();}

        var result=await new SqliteOutputPackageService().GenerateAsync(path,_directory);
        using var studentHandouts=new XLWorkbook(result.StudentHandoutsExcelPath);
        var handoutCells=studentHandouts.Worksheet("中2_架空 生徒").CellsUsed().Select(cell=>cell.GetString()).ToArray();
        Assert.Contains(handoutCells,text=>text=="英");
        Assert.DoesNotContain(handoutCells,text=>text=="英語");
    }

    public void Dispose(){if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
}
