using Microsoft.Data.Sqlite;
using SeminarSched.Domain.CourseSettings;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.CourseSettings;
using SeminarSched.Infrastructure.MasterData;
using SeminarSched.Infrastructure.Projects;
using SeminarSched.Infrastructure.Scheduling;

namespace SeminarSched.Infrastructure.Tests;

public sealed class SqliteScheduleRunServiceTests : IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),"SeminarSched.Tests",Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task RunAsync_BuildsValidCandidatesAndPersistsSolverResult()
    {
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"solver.jukuschedule");await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));
        var master=new SqliteMasterDataRepository();var student=await master.SaveStudentAsync(path,new Student(0,"S-001","架空 生徒","中2"));var teacher=await master.SaveTeacherAsync(path,new Teacher(0,"T-001","架空 講師"));var subject=await master.SaveSubjectAsync(path,new Subject(0,"JH_MATH","数学","数","中学",1));await master.SaveQualificationAsync(path,new TeacherQualification(teacher.Id,subject.Id,true));
        var course=new SqliteCourseSettingsRepository();var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));
        await using(var c=new SqliteConnection($"Data Source={path};Pooling=False")){await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText=$"INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,{student.Id},{subject.Id},1);";await q.ExecuteNonQueryAsync();}
        var result=await new SqliteScheduleRunService().RunAsync(path,TimeSpan.FromSeconds(5));Assert.Equal(1,result.PlacedLessons);Assert.Equal(0,result.UnassignedLessons);
        await using var verify=new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");await verify.OpenAsync();await using var count=verify.CreateCommand();count.CommandText="SELECT COUNT(*) FROM Assignment WHERE Source='cp-sat';";Assert.Equal(1L,Convert.ToInt64(await count.ExecuteScalarAsync()));
        count.CommandText="SELECT COUNT(*) FROM OptimizationRun WHERE Status='completed' AND UnassignedCount=0;";Assert.Equal(1L,Convert.ToInt64(await count.ExecuteScalarAsync()));
        count.CommandText="SELECT SessionIndex FROM Assignment WHERE Source='cp-sat';";Assert.Equal(1L,Convert.ToInt64(await count.ExecuteScalarAsync()));
    }
    [Fact]
    public async Task RunAsync_PreservesUnlockedManualPlacement()
    {
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"manual.jukuschedule");await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));
        var master=new SqliteMasterDataRepository();var student=await master.SaveStudentAsync(path,new Student(0,"S-002","架空 手動生徒","中1"));var teacher=await master.SaveTeacherAsync(path,new Teacher(0,"T-002","架空 手動講師"));var subject=await master.SaveSubjectAsync(path,new Subject(0,"JH_ENG","英語","英","中学",1));await master.SaveQualificationAsync(path,new TeacherQualification(teacher.Id,subject.Id,true));var course=new SqliteCourseSettingsRepository();var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));
        await using(var connection=new SqliteConnection($"Data Source={path};Pooling=False")){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=$"INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,{student.Id},{subject.Id},1);INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source,SessionIndex,IsManual) SELECT last_insert_rowid(),{teacher.Id},Id,{slot.Id},0,'manual',1,1 FROM OpenDate LIMIT 1;";await command.ExecuteNonQueryAsync();}
        // CIの実行機が混雑している場合にCP-SATの単一戦略が短い持ち時間へ間に合わないことがあった
        // （2026-09-23、実際にCIで1回だけ発生。直後の無関係なdocsのみのcommitでは同じテストが問題無く
        // 通過しており、コードの不具合ではなく一時的なタイミングの問題と判断した）ため、余裕を持たせる。
        var result=await new SqliteScheduleRunService().RunAsync(path,TimeSpan.FromSeconds(5));Assert.Equal(0,result.PlacedLessons);Assert.Equal(0,result.UnassignedLessons);
        await using var verify=new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");await verify.OpenAsync();await using var count=verify.CreateCommand();count.CommandText="SELECT COUNT(*) FROM Assignment WHERE IsManual=1 AND IsLocked=0 AND Source='manual';";Assert.Equal(1L,Convert.ToInt64(await count.ExecuteScalarAsync()));
    }
    // ユーザー指示: 担当講師優先度5は、通常担当講師の出勤可能コマ数が必要回数以上ある限り、
    // 他の講師が候補に残らないようにする（第1〜3希望が未設定ならそれ以外は一切使わせない）。
    // 制限が無い場合、別講師を使うと生徒の日程分散スコア（1日あたり10,000点）が
    // 講師優先度スコア（100点単位のPreferencePenalty）を上回るため、あえて別講師・別日へ
    // 分散させてしまう状況を作り、制限が実際に効いていることを確認する。
    [Fact]
    public async Task RunAsync_PriorityFiveRestrictsPlacementsToRegularTeacherWhenSlotsSuffice()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "priority5-sufficient.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 21)));
        var master = new SqliteMasterDataRepository();
        var student = await master.SaveStudentAsync(path, new Student(0, "S-P5", "架空 優先度5生徒", "中2"));
        var regularTeacher = await master.SaveTeacherAsync(path, new Teacher(0, "T-REG", "架空 通常担当講師"));
        var otherTeacher = await master.SaveTeacherAsync(path, new Teacher(0, "T-OTH", "架空 別講師"));
        var subject = await master.SaveSubjectAsync(path, new Subject(0, "JH_P5", "優先度5科目", "優", "中学", 1));
        await master.SaveQualificationAsync(path, new TeacherQualification(regularTeacher.Id, subject.Id, true));
        await master.SaveQualificationAsync(path, new TeacherQualification(otherTeacher.Id, subject.Id, true));
        var course = new SqliteCourseSettingsRepository();
        var slot1 = await course.SaveTimeSlotAsync(path, new TimeSlot(0, "1", "1限", new TimeOnly(9, 0), new TimeOnly(10, 0), 1));
        var slot2 = await course.SaveTimeSlotAsync(path, new TimeSlot(0, "2", "2限", new TimeOnly(10, 10), new TimeOnly(11, 10), 2));
        await course.SaveCourseDayAsync(path, new CourseDay(new DateOnly(2026, 7, 20), true, "", [slot1.Id, slot2.Id]));
        await course.SaveCourseDayAsync(path, new CourseDay(new DateOnly(2026, 7, 21), true, "", [slot1.Id]));

        long requestId, day1Id, day2Id;
        await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await connection.OpenAsync();
            await using (var read = connection.CreateCommand())
            {
                read.CommandText = "SELECT Id FROM OpenDate ORDER BY Date;";
                await using var reader = await read.ExecuteReaderAsync();
                await reader.ReadAsync(); day1Id = reader.GetInt64(0);
                await reader.ReadAsync(); day2Id = reader.GetInt64(0);
            }
            // 通常担当講師は7/20の2コマだけ、別講師は7/20の1コマ目と7/21の1コマ目だけに出勤可能にする
            // （＝別講師を使うと2日にまたがり、日程分散で有利になってしまう状況）。
            await using var block = connection.CreateCommand();
            block.CommandText = "INSERT INTO TeacherUnavailability(TeacherId,OpenDateId,TimeSlotId) VALUES($regularOnDay2,$day2,$slot1),($otherOnDay1Slot2,$day1,$slot2);";
            block.Parameters.AddWithValue("$regularOnDay2", regularTeacher.Id); block.Parameters.AddWithValue("$otherOnDay1Slot2", otherTeacher.Id);
            block.Parameters.AddWithValue("$day1", day1Id); block.Parameters.AddWithValue("$day2", day2Id); block.Parameters.AddWithValue("$slot1", slot1.Id); block.Parameters.AddWithValue("$slot2", slot2.Id);
            await block.ExecuteNonQueryAsync();

            await using var insertRequest = connection.CreateCommand();
            insertRequest.CommandText = "INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions,RegularTeacherId,RegularTeacherPriority) VALUES(1,$student,$subject,2,$regular,5); SELECT last_insert_rowid();";
            insertRequest.Parameters.AddWithValue("$student", student.Id); insertRequest.Parameters.AddWithValue("$subject", subject.Id); insertRequest.Parameters.AddWithValue("$regular", regularTeacher.Id);
            requestId = Convert.ToInt64(await insertRequest.ExecuteScalarAsync());
        }

        var result = await new SqliteScheduleRunService().RunAsync(path, TimeSpan.FromSeconds(5));
        Assert.Equal(2, result.PlacedLessons); Assert.Equal(0, result.UnassignedLessons);

        await using var verify = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        await verify.OpenAsync();
        await using var countOther = verify.CreateCommand();
        countOther.CommandText = "SELECT COUNT(*) FROM Assignment WHERE LessonRequestId=$request AND TeacherId=$other;";
        countOther.Parameters.AddWithValue("$request", requestId); countOther.Parameters.AddWithValue("$other", otherTeacher.Id);
        Assert.Equal(0L, Convert.ToInt64(await countOther.ExecuteScalarAsync()));
    }

    // ユーザー報告バグ修正: アンケートに一度も回答していない講師（TeacherAvailability行が0件）が
    // 「常に出勤可能」として扱われ、候補に混入していた。資格はあるが行が0件の講師は、実際に行を
    // 持つ講師がいるにもかかわらず一切配置されないことを確認する。
    [Fact]
    public async Task RunAsync_TreatsTeacherWithNoAvailabilityResponseAsFullyUnavailable()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "no-survey-response.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20)));
        var master = new SqliteMasterDataRepository();
        var student = await master.SaveStudentAsync(path, new Student(0, "S-NR", "架空 生徒", "中2"));
        var responded = await master.SaveTeacherAsync(path, new Teacher(0, "T-RESP", "架空 回答講師"));
        var noResponse = await master.SaveTeacherAsync(path, new Teacher(0, "T-NORESP", "架空 未回答講師"));
        var subject = await master.SaveSubjectAsync(path, new Subject(0, "JH_MATH2", "数学", "数", "中学", 1));
        await master.SaveQualificationAsync(path, new TeacherQualification(responded.Id, subject.Id, true));
        await master.SaveQualificationAsync(path, new TeacherQualification(noResponse.Id, subject.Id, true));
        var course = new SqliteCourseSettingsRepository();
        var slot = await course.SaveTimeSlotAsync(path, new TimeSlot(0, "1", "1限", new TimeOnly(9, 0), new TimeOnly(10, 0), 1));
        await course.SaveCourseDayAsync(path, new CourseDay(new DateOnly(2026, 7, 20), true, "", [slot.Id]));

        await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await connection.OpenAsync();
            await using (var insertRequest = connection.CreateCommand())
            {
                insertRequest.CommandText = "INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,$student,$subject,1);";
                insertRequest.Parameters.AddWithValue("$student", student.Id);
                insertRequest.Parameters.AddWithValue("$subject", subject.Id);
                await insertRequest.ExecuteNonQueryAsync();
            }
            // 「回答講師」だけがTeacherAvailability行を持つ（アンケートに回答した想定）。
            // 「未回答講師」は資格はあるがTeacherAvailability行が0件（一度も回答していない想定）。
            await using var availability = connection.CreateCommand();
            availability.CommandText = "INSERT INTO TeacherAvailability(ProjectId,TeacherId,OpenDateId,TimeSlotId,AvailabilityLevel) SELECT 1,$teacher,Id,$slot,1 FROM OpenDate;";
            availability.Parameters.AddWithValue("$teacher", responded.Id);
            availability.Parameters.AddWithValue("$slot", slot.Id);
            await availability.ExecuteNonQueryAsync();
        }

        var result = await new SqliteScheduleRunService().RunAsync(path, TimeSpan.FromSeconds(5));
        Assert.Equal(1, result.PlacedLessons);
        Assert.Equal(0, result.UnassignedLessons);

        await using var verify = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        await verify.OpenAsync();
        await using var count = verify.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM Assignment WHERE TeacherId=$noResponse;";
        count.Parameters.AddWithValue("$noResponse", noResponse.Id);
        Assert.Equal(0L, Convert.ToInt64(await count.ExecuteScalarAsync()));
    }

    // 通常担当講師の出勤可能コマ数(2)が必要回数(3)に満たない場合は、制限を適用せず他の講師も
    // 候補に残す（さもなければ1コマ未配置のまま終わってしまう）。
    [Fact]
    public async Task RunAsync_PriorityFiveAllowsOtherTeachersWhenRegularTeacherSlotsAreInsufficient()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "priority5-insufficient.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 21)));
        var master = new SqliteMasterDataRepository();
        var student = await master.SaveStudentAsync(path, new Student(0, "S-P5B", "架空 優先度5生徒二", "中2"));
        var regularTeacher = await master.SaveTeacherAsync(path, new Teacher(0, "T-REGB", "架空 通常担当講師二"));
        var otherTeacher = await master.SaveTeacherAsync(path, new Teacher(0, "T-OTHB", "架空 別講師二"));
        var subject = await master.SaveSubjectAsync(path, new Subject(0, "JH_P5B", "優先度5科目二", "科", "中学", 1));
        await master.SaveQualificationAsync(path, new TeacherQualification(regularTeacher.Id, subject.Id, true));
        await master.SaveQualificationAsync(path, new TeacherQualification(otherTeacher.Id, subject.Id, true));
        var course = new SqliteCourseSettingsRepository();
        var slot1 = await course.SaveTimeSlotAsync(path, new TimeSlot(0, "1", "1限", new TimeOnly(9, 0), new TimeOnly(10, 0), 1));
        var slot2 = await course.SaveTimeSlotAsync(path, new TimeSlot(0, "2", "2限", new TimeOnly(10, 10), new TimeOnly(11, 10), 2));
        await course.SaveCourseDayAsync(path, new CourseDay(new DateOnly(2026, 7, 20), true, "", [slot1.Id, slot2.Id]));
        await course.SaveCourseDayAsync(path, new CourseDay(new DateOnly(2026, 7, 21), true, "", [slot1.Id]));

        await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await connection.OpenAsync();
            // 通常担当講師は7/20の2コマだけ出勤可能（必要回数3に対して不足）。別講師は無制限に出勤可能のまま。
            long day2Id;
            await using (var read = connection.CreateCommand())
            {
                read.CommandText = "SELECT Id FROM OpenDate ORDER BY Date DESC LIMIT 1;";
                day2Id = Convert.ToInt64(await read.ExecuteScalarAsync());
            }
            await using var block = connection.CreateCommand();
            block.CommandText = "INSERT INTO TeacherUnavailability(TeacherId,OpenDateId,TimeSlotId) VALUES($regular,$day2,$slot1);";
            block.Parameters.AddWithValue("$regular", regularTeacher.Id); block.Parameters.AddWithValue("$day2", day2Id); block.Parameters.AddWithValue("$slot1", slot1.Id);
            await block.ExecuteNonQueryAsync();

            await using var insertRequest = connection.CreateCommand();
            insertRequest.CommandText = "INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions,RegularTeacherId,RegularTeacherPriority) VALUES(1,$student,$subject,3,$regular,5);";
            insertRequest.Parameters.AddWithValue("$student", student.Id); insertRequest.Parameters.AddWithValue("$subject", subject.Id); insertRequest.Parameters.AddWithValue("$regular", regularTeacher.Id);
            await insertRequest.ExecuteNonQueryAsync();
        }

        var result = await new SqliteScheduleRunService().RunAsync(path, TimeSpan.FromSeconds(5));
        Assert.Equal(3, result.PlacedLessons); Assert.Equal(0, result.UnassignedLessons);

        await using var verify = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        await verify.OpenAsync();
        await using var countOther = verify.CreateCommand();
        countOther.CommandText = "SELECT COUNT(*) FROM Assignment WHERE TeacherId=$other;";
        countOther.Parameters.AddWithValue("$other", otherTeacher.Id);
        Assert.Equal(1L, Convert.ToInt64(await countOther.ExecuteScalarAsync()));
    }

    // ユーザー報告「配置できない原因がある場合はその警告を出す。例えば、優先度5になっていることで
    // ハード条件が加えられ、それにより実装できない場合はその旨を伝える」への対応。2名の生徒が同じ
    // 通常担当講師（優先度5）を指定し、その講師の出勤可能コマ数が2人分の合計必要回数を満たさない
    // 状況を作る。各生徒individuallyの必要回数だけを見た絞り込み判定（RestrictPriorityFive...)は
    // 通過してしまう（単独では足りているように見える）が、2人が同じ講師の同じ枠を取り合うため
    // OneToOneRequired（ペア不可）にして合計必要回数(4)がその講師の総コマ数(2)を上回るようにし、
    // 未配置が発生してもDiagnoseUnassignedDemandsが優先度5起因と分類できることを確認する。
    [Fact]
    public async Task RunAsync_ReportsUnassignedDueToRegularTeacherPriorityWhenSharedRegularTeacherCapacityIsInsufficient()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "priority5-shared-shortage.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20)));
        var master = new SqliteMasterDataRepository();
        var studentA = await master.SaveStudentAsync(path, new Student(0, "S-P5C1", "架空 優先度5生徒三", "中2"));
        var studentB = await master.SaveStudentAsync(path, new Student(0, "S-P5C2", "架空 優先度5生徒四", "中2"));
        var regularTeacher = await master.SaveTeacherAsync(path, new Teacher(0, "T-REGC", "架空 通常担当講師三"));
        var subject = await master.SaveSubjectAsync(path, new Subject(0, "JH_P5C", "優先度5科目三", "共", "中学", 1));
        await master.SaveQualificationAsync(path, new TeacherQualification(regularTeacher.Id, subject.Id, true));
        var course = new SqliteCourseSettingsRepository();
        var slot1 = await course.SaveTimeSlotAsync(path, new TimeSlot(0, "1", "1限", new TimeOnly(9, 0), new TimeOnly(10, 0), 1));
        var slot2 = await course.SaveTimeSlotAsync(path, new TimeSlot(0, "2", "2限", new TimeOnly(10, 10), new TimeOnly(11, 10), 2));
        await course.SaveCourseDayAsync(path, new CourseDay(new DateOnly(2026, 7, 20), true, "", [slot1.Id, slot2.Id]));

        await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await connection.OpenAsync();
            // 通常担当講師の出勤可能コマは合計2つだけ。両生徒ともOneToOneRequired（ペア不可）で
            // 各2回必要とする（合計必要回数4 > 講師の総コマ数2）。
            await using var insertRequest = connection.CreateCommand();
            insertRequest.CommandText = """
                INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions,RegularTeacherId,RegularTeacherPriority,OneToOneRequired) VALUES
                    (1,$studentA,$subject,2,$regular,5,1),
                    (1,$studentB,$subject,2,$regular,5,1);
                """;
            insertRequest.Parameters.AddWithValue("$studentA", studentA.Id);
            insertRequest.Parameters.AddWithValue("$studentB", studentB.Id);
            insertRequest.Parameters.AddWithValue("$subject", subject.Id);
            insertRequest.Parameters.AddWithValue("$regular", regularTeacher.Id);
            await insertRequest.ExecuteNonQueryAsync();
        }

        var result = await new SqliteScheduleRunService().RunAsync(path, TimeSpan.FromSeconds(5));
        Assert.Equal(2, result.PlacedLessons);
        Assert.Equal(2, result.UnassignedLessons);
        Assert.True(result.UnassignedDueToRegularTeacherPriority > 0);
        Assert.Equal(0, result.UnassignedWithNoQualifiedTeacher);
    }

    // 対応できる講師が資格の時点で1人もいない（＝どれだけ時間をかけても解決しない）科目のケースを
    // NoQualifiedTeacherとして分類できることを確認する。
    [Fact]
    public async Task RunAsync_ReportsUnassignedWithNoQualifiedTeacherWhenNoTeacherCanTeachTheSubject()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "no-qualified-teacher.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20)));
        var master = new SqliteMasterDataRepository();
        var student = await master.SaveStudentAsync(path, new Student(0, "S-NQ", "架空 無資格科目生徒", "中2"));
        var subject = await master.SaveSubjectAsync(path, new Subject(0, "JH_NQ", "無資格科目", "無", "中学", 1));
        // 講師を1人も資格登録しない（対応できる講師が構造的に0人）。
        var course = new SqliteCourseSettingsRepository();
        var slot = await course.SaveTimeSlotAsync(path, new TimeSlot(0, "1", "1限", new TimeOnly(9, 0), new TimeOnly(10, 0), 1));
        await course.SaveCourseDayAsync(path, new CourseDay(new DateOnly(2026, 7, 20), true, "", [slot.Id]));

        await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var insertRequest = connection.CreateCommand();
            insertRequest.CommandText = "INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,$student,$subject,1);";
            insertRequest.Parameters.AddWithValue("$student", student.Id);
            insertRequest.Parameters.AddWithValue("$subject", subject.Id);
            await insertRequest.ExecuteNonQueryAsync();
        }

        var result = await new SqliteScheduleRunService().RunAsync(path, TimeSpan.FromSeconds(5));
        Assert.Equal(0, result.PlacedLessons);
        Assert.Equal(1, result.UnassignedLessons);
        Assert.Equal(1, result.UnassignedWithNoQualifiedTeacher);
        Assert.Equal(0, result.UnassignedDueToRegularTeacherPriority);
    }

    public void Dispose(){if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
}
