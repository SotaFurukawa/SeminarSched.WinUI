using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SeminarSched.Application.Scheduling;
using SeminarSched.Domain.MasterData;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Scheduling;

public sealed class SqliteScheduleEditorService : IScheduleEditorService
{
    public Task<long> GetDataVersionAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(projectPath);
        var mainTicks = File.Exists(fullPath) ? File.GetLastWriteTimeUtc(fullPath).Ticks : 0L;
        var walPath = fullPath + "-wal";
        var walTicks = File.Exists(walPath) ? File.GetLastWriteTimeUtc(walPath).Ticks : 0L;
        return Task.FromResult(Math.Max(mainTicks, walTicks));
    }

    public async Task<IReadOnlyList<ScheduleAssignmentItem>> GetAssignmentsAsync(string projectPath, long? openDateId = null, CancellationToken cancellationToken = default)
    {
        await using var connection=await OpenAsync(projectPath,cancellationToken).ConfigureAwait(false);await SqliteProjectSchema.EnsureCurrentAsync(connection,cancellationToken).ConfigureAwait(false);
        await using var command=connection.CreateCommand();command.CommandText=(openDateId is null?"""
            SELECT a.Id,a.LessonRequestId,a.TeacherId,a.OpenDateId,a.TimeSlotId,a.IsLocked,a.IsManual,a.Source,
                   '第'||a.SessionIndex||'回　'||d.Date||' '||ts.DisplayName||' / '||st.Name||'（'||st.Grade||'） / '||su.DisplayName||' / '||te.Name
            FROM Assignment a JOIN LessonRequest r ON r.Id=a.LessonRequestId JOIN Student st ON st.Id=r.StudentId JOIN Subject su ON su.Id=r.SubjectId JOIN Teacher te ON te.Id=a.TeacherId JOIN OpenDate d ON d.Id=a.OpenDateId JOIN TimeSlot ts ON ts.Id=a.TimeSlotId
            ORDER BY d.Date,ts.SortOrder,te.ExternalId,st.ExternalId;
            """:"""
            SELECT a.Id,a.LessonRequestId,a.TeacherId,a.OpenDateId,a.TimeSlotId,a.IsLocked,a.IsManual,a.Source,
                   '第'||a.SessionIndex||'回　'||d.Date||' '||ts.DisplayName||' / '||st.Name||'（'||st.Grade||'） / '||su.DisplayName||' / '||te.Name
            FROM Assignment a JOIN LessonRequest r ON r.Id=a.LessonRequestId JOIN Student st ON st.Id=r.StudentId JOIN Subject su ON su.Id=r.SubjectId JOIN Teacher te ON te.Id=a.TeacherId JOIN OpenDate d ON d.Id=a.OpenDateId JOIN TimeSlot ts ON ts.Id=a.TimeSlotId
            WHERE a.OpenDateId=$date
            ORDER BY d.Date,ts.SortOrder,te.ExternalId,st.ExternalId;
            """);
        if(openDateId is not null)command.Parameters.AddWithValue("$date",openDateId.Value);
        var result=new List<ScheduleAssignmentItem>();await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))result.Add(new ScheduleAssignmentItem(reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2),reader.GetInt64(3),reader.GetInt64(4),reader.GetBoolean(5),reader.GetBoolean(6),reader.GetString(7),reader.GetString(8)));return result;
    }

    public async Task AddManualAsync(string projectPath,long lessonRequestId,long teacherId,long openDateId,long timeSlotId,bool isLocked,bool confirmSoftWarnings=false,string? reason=null,CancellationToken cancellationToken=default)
    {
        await new SqliteFixedLessonService().AddManualAsync(projectPath,lessonRequestId,teacherId,openDateId,timeSlotId,isLocked,confirmSoftWarnings,reason,cancellationToken).ConfigureAwait(false);
    }

    public async Task<EditPreview> PreviewAddAsync(string projectPath,long lessonRequestId,long teacherId,long openDateId,long timeSlotId,CancellationToken cancellationToken=default)
        => await new SqliteFixedLessonService().PreviewAddAsync(projectPath,lessonRequestId,teacherId,openDateId,timeSlotId,cancellationToken).ConfigureAwait(false);

    public async Task RemoveManualAsync(string projectPath,long assignmentId,CancellationToken cancellationToken=default)
    {
        await using var connection=await OpenAsync(projectPath,cancellationToken).ConfigureAwait(false);await SqliteProjectSchema.EnsureCurrentAsync(connection,cancellationToken).ConfigureAwait(false);await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using(var check=connection.CreateCommand()){check.Transaction=transaction;check.CommandText="SELECT IsLocked FROM Assignment WHERE Id=$id AND IsManual=1;";check.Parameters.AddWithValue("$id",assignmentId);var locked=await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);if(locked is null)throw new InvalidOperationException("削除できる手動配置が見つかりません。");if(Convert.ToInt64(locked)==1)throw new InvalidOperationException("ロック済みの配置は削除できません。先にロックを解除してください。");}
        await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="DELETE FROM Assignment WHERE Id=$id AND IsManual=1;";command.Parameters.AddWithValue("$id",assignmentId);await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);await InsertAuditAsync(connection,transaction,"manual_assignment_removed",assignmentId.ToString(CultureInfo.InvariantCulture),null,cancellationToken).ConfigureAwait(false);await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetLockedAsync(string projectPath,long assignmentId,bool isLocked,CancellationToken cancellationToken=default)
    {
        await using var connection=await OpenAsync(projectPath,cancellationToken).ConfigureAwait(false);await SqliteProjectSchema.EnsureCurrentAsync(connection,cancellationToken).ConfigureAwait(false);await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="UPDATE Assignment SET IsLocked=$locked WHERE Id=$id;";command.Parameters.AddWithValue("$locked",isLocked);command.Parameters.AddWithValue("$id",assignmentId);if(await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false)!=1)throw new InvalidOperationException("配置が見つかりません。");await InsertAuditAsync(connection,transaction,"assignment_lock_changed",assignmentId.ToString(CultureInfo.InvariantCulture),new{isLocked},cancellationToken).ConfigureAwait(false);await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ResetAutomaticAsync(string projectPath,CancellationToken cancellationToken=default)
    {
        await using var connection=await OpenAsync(projectPath,cancellationToken).ConfigureAwait(false);await SqliteProjectSchema.EnsureCurrentAsync(connection,cancellationToken).ConfigureAwait(false);await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="DELETE FROM Assignment WHERE IsLocked=0 AND IsManual=0;";var removed=await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);await InsertAuditAsync(connection,transaction,"automatic_assignments_reset","project:1",new{removed},cancellationToken).ConfigureAwait(false);await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    // 「自動配置だけリセット」と異なり、ロック済み・手動配置も含めた配置を全件削除する。ユーザー要望：
    // 時間割編集の受講状況をまっさらに戻したいことがある（自動作成前のやり直し等）。
    public async Task ResetAllAsync(string projectPath,CancellationToken cancellationToken=default)
    {
        await using var connection=await OpenAsync(projectPath,cancellationToken).ConfigureAwait(false);await SqliteProjectSchema.EnsureCurrentAsync(connection,cancellationToken).ConfigureAwait(false);await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="DELETE FROM Assignment;";var removed=await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);await InsertAuditAsync(connection,transaction,"all_assignments_reset","project:1",new{removed},cancellationToken).ConfigureAwait(false);await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OpenDateOption>> GetOpenDatesAsync(string projectPath,CancellationToken cancellationToken=default)
    {
        await using var connection=await OpenAsync(projectPath,cancellationToken).ConfigureAwait(false);await SqliteProjectSchema.EnsureCurrentAsync(connection,cancellationToken).ConfigureAwait(false);
        await using var command=connection.CreateCommand();command.CommandText="SELECT Id,Date FROM OpenDate WHERE IsOpen=1 ORDER BY Date;";
        var result=new List<OpenDateOption>();await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var date=DateOnly.Parse(reader.GetString(1),CultureInfo.InvariantCulture);
            result.Add(new OpenDateOption(reader.GetInt64(0),$"{date:yyyy-MM-dd} ({JapaneseWeekday(date)})"));
        }
        return result;
    }

    private static string JapaneseWeekday(DateOnly date)=>date.DayOfWeek switch
    {
        DayOfWeek.Sunday=>"日",DayOfWeek.Monday=>"月",DayOfWeek.Tuesday=>"火",DayOfWeek.Wednesday=>"水",
        DayOfWeek.Thursday=>"木",DayOfWeek.Friday=>"金",_=>"土",
    };

    public async Task<ScheduleBoard> GetBoardAsync(string projectPath,long openDateId,IReadOnlyCollection<long> extraTeacherIds,CancellationToken cancellationToken=default)
    {
        await using var connection=await OpenAsync(projectPath,cancellationToken).ConfigureAwait(false);await SqliteProjectSchema.EnsureCurrentAsync(connection,cancellationToken).ConfigureAwait(false);

        var slots=new List<BoardSlotRow>();
        await using(var command=connection.CreateCommand())
        {
            command.CommandText="SELECT ts.Id,ts.DisplayName,ts.SortOrder FROM OpenDateTimeSlot ds JOIN TimeSlot ts ON ts.Id=ds.TimeSlotId WHERE ds.OpenDateId=$date AND ts.Active=1 ORDER BY ts.SortOrder;";
            command.Parameters.AddWithValue("$date",openDateId);
            await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                slots.Add(new BoardSlotRow(reader.GetInt64(0),reader.GetString(1),reader.GetInt32(2)));
        }

        var qualified=new List<BoardTeacherColumn>();
        await using(var command=connection.CreateCommand())
        {
            command.CommandText="SELECT DISTINCT t.Id,t.Name FROM Teacher t JOIN TeacherQualification q ON q.TeacherId=t.Id AND q.CanTeach=1 WHERE t.Active=1 ORDER BY t.ExternalId;";
            await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                qualified.Add(new BoardTeacherColumn(reader.GetInt64(0),reader.GetString(1)));
        }

        var unavailableCells=new HashSet<(long TeacherId,long TimeSlotId)>();
        await using(var command=connection.CreateCommand())
        {
            command.CommandText="SELECT TeacherId,TimeSlotId FROM TeacherUnavailability WHERE OpenDateId=$date;";
            command.Parameters.AddWithValue("$date",openDateId);
            await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                unavailableCells.Add((reader.GetInt64(0),reader.GetInt64(1)));
        }

        var teachersWithAvailabilityRows=new HashSet<long>();
        var availabilityLevels=new Dictionary<(long TeacherId,long TimeSlotId),int>();
        await using(var command=connection.CreateCommand())
        {
            command.CommandText="SELECT TeacherId,OpenDateId,TimeSlotId,AvailabilityLevel FROM TeacherAvailability;";
            await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var teacherId=reader.GetInt64(0);teachersWithAvailabilityRows.Add(teacherId);
                if(reader.GetInt64(1)==openDateId)availabilityLevels[(teacherId,reader.GetInt64(2))]=reader.GetInt32(3);
            }
        }

        bool IsBlocked(long teacherId,long timeSlotId)
        {
            if(unavailableCells.Contains((teacherId,timeSlotId)))return true;
            if(teachersWithAvailabilityRows.Contains(teacherId))
                return !availabilityLevels.TryGetValue((teacherId,timeSlotId),out var level)||level==0;
            return false;
        }

        // 出勤可否(TeacherAvailability)が一度も設定されていない講師は「常時出勤可能」と解釈されるため、
        // 資格だけ持ち越されて今期の出勤可否を未設定のまま放置された講師が全日程に紛れ込んでいた。
        // 明示的に一時表示(extraTeacherIds)された場合を除き、出勤可否データが無い講師は編集画面に出さない。
        var teachers=qualified
            .Where(t=>extraTeacherIds.Contains(t.TeacherId)||(teachersWithAvailabilityRows.Contains(t.TeacherId)&&slots.Any(s=>!IsBlocked(t.TeacherId,s.TimeSlotId))))
            .ToList();

        var cardsByCell=new Dictionary<(long TeacherId,long TimeSlotId),List<BoardCard>>();
        await using(var command=connection.CreateCommand())
        {
            command.CommandText="""
                SELECT a.Id,a.TeacherId,a.TimeSlotId,r.StudentId,st.Name,su.DisplayName,a.IsManual,a.IsLocked,
                       CASE WHEN r.OneToOneRequired=1 OR COALESCE(p.OneToOneRequired,0)=1 THEN 1 ELSE 0 END,
                       CASE WHEN COALESCE(r.RegularTeacherPriority,p.RegularTeacherPriority)=5 THEN 1 ELSE 0 END
                FROM Assignment a JOIN LessonRequest r ON r.Id=a.LessonRequestId JOIN Student st ON st.Id=r.StudentId JOIN Subject su ON su.Id=r.SubjectId
                LEFT JOIN RegularLessonProfile p ON p.ProjectId=r.ProjectId AND p.StudentId=r.StudentId AND p.SubjectId=r.SubjectId
                WHERE a.OpenDateId=$date ORDER BY st.ExternalId;
                """;
            command.Parameters.AddWithValue("$date",openDateId);
            await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var key=(reader.GetInt64(1),reader.GetInt64(2));
                var card=new BoardCard(reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2),reader.GetInt64(3),reader.GetString(4),reader.GetString(5),reader.GetBoolean(6),reader.GetBoolean(7),reader.GetBoolean(8),reader.GetBoolean(9));
                if(!cardsByCell.TryGetValue(key,out var list)){list=[];cardsByCell[key]=list;}
                list.Add(card);
            }
        }

        var cells=new List<BoardCell>();
        foreach(var slot in slots)
            foreach(var teacher in teachers)
                cells.Add(new BoardCell(slot.TimeSlotId,teacher.TeacherId,IsBlocked(teacher.TeacherId,slot.TimeSlotId),
                    cardsByCell.TryGetValue((teacher.TeacherId,slot.TimeSlotId),out var cards)?cards:[]));

        return new ScheduleBoard(slots,teachers,cells);
    }

    // 生徒ID等は出さず、選択中の日付で実際に配置できる受講希望だけを一覧に出す（生徒がその日出席できない・
    // 資格のある講師の空きが無い等の場合はそもそも一覧に出さない）。カードには氏名・学年・科目略称・
    // 残り回数に加え、その日置ける具体的なコマ(コード)一覧を添える。
    public async Task<IReadOnlyList<UnplacedSessionOption>> GetUnplacedSessionsAsync(string projectPath,long openDateId,CancellationToken cancellationToken=default)
    {
        await using var connection=await OpenAsync(projectPath,cancellationToken).ConfigureAwait(false);await SqliteProjectSchema.EnsureCurrentAsync(connection,cancellationToken).ConfigureAwait(false);
        var rows=new List<(long Id,string StudentName,string Grade,string SubjectShortName,int Remaining)>();
        await using(var command=connection.CreateCommand())
        {
            command.CommandText="""
                SELECT r.Id,st.Name,st.Grade,su.DisplayName,su.ShortName,su.Code,r.RequiredSessions-COUNT(a.Id) AS remaining
                FROM LessonRequest r JOIN Student st ON st.Id=r.StudentId JOIN Subject su ON su.Id=r.SubjectId
                LEFT JOIN Assignment a ON a.LessonRequestId=r.Id
                GROUP BY r.Id HAVING remaining>0 ORDER BY st.ExternalId;
                """;
            await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                rows.Add((reader.GetInt64(0),reader.GetString(1),reader.GetString(2),SubjectAbbreviation.Resolve(reader.GetString(3),reader.GetString(4),reader.GetString(5)),reader.GetInt32(6)));
        }

        var slotsByRequest=await GetAvailableSlotCodesForDateAsync(connection,openDateId,cancellationToken).ConfigureAwait(false);

        return rows.Where(r=>slotsByRequest.ContainsKey(r.Id))
            .Select(r=>new UnplacedSessionOption(r.Id,r.StudentName,r.Grade,r.SubjectShortName,r.Remaining,slotsByRequest[r.Id]))
            .ToList();
    }

    // ユーザー要望（checkpoint151）「未配置に残っているものを移そうとしてドラッグしているときに、
    // 生徒が出席不可にしているコマに禁止マークをつけるようにしておく」への対応。明示的に
    // AvailabilityLevel=0の行だけを対象にする（未回答＝行が無い場合は出席可として扱う、
    // このファイルの他クエリと同じ既定値の慣習）。
    public async Task<IReadOnlyList<long>> GetStudentUnavailableSlotIdsAsync(string projectPath,long lessonRequestId,long openDateId,CancellationToken cancellationToken=default)
    {
        await using var connection=await OpenAsync(projectPath,cancellationToken).ConfigureAwait(false);await SqliteProjectSchema.EnsureCurrentAsync(connection,cancellationToken).ConfigureAwait(false);
        await using var command=connection.CreateCommand();
        command.CommandText="""
            SELECT sa.TimeSlotId
            FROM StudentAvailability sa
            JOIN LessonRequest r ON r.StudentId=sa.StudentId
            WHERE r.Id=$request AND sa.OpenDateId=$date AND sa.AvailabilityLevel=0;
            """;
        command.Parameters.AddWithValue("$request",lessonRequestId);
        command.Parameters.AddWithValue("$date",openDateId);
        var result=new List<long>();
        await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(reader.GetInt64(0));
        return result;
    }

    /// <summary>
    /// CountCandidatesAsync（旧実装）と同じ条件（資格・空き時間・出勤不可・同時刻の生徒衝突）を、
    /// 日付を1つに絞って実際のコマコードを返す形に書き換えたもの。講師の同時担当上限（2名まで）は
    /// solverの候補生成でも列挙時点ではフィルタしていないため、ここでも同様に含めない。
    /// ユーザー報告バグ修正: 講師単位でNOT EXISTSをfallbackさせていたため、アンケートに一度も
    /// 回答していない講師（TeacherAvailability行が0件）を「常に出勤可能」として扱っていた。
    /// プロジェクト単位のNOT EXISTS（詳細はSqliteScheduleRunService.BuildProblemAsyncの同種の
    /// 修正コメント参照）へ変更し、アンケート未取込みのプロジェクトでは従来通り全講師を候補のまま
    /// 残しつつ、取込み済みプロジェクトでは未回答の講師個別だけを対象外にする。
    /// </summary>
    private static async Task<Dictionary<long,IReadOnlyList<string>>> GetAvailableSlotCodesForDateAsync(SqliteConnection connection,long openDateId,CancellationToken cancellationToken)
    {
        var result=new Dictionary<long,List<string>>();
        await using var command=connection.CreateCommand();
        command.CommandText="""
            SELECT DISTINCT r.Id,ts.Code,ts.SortOrder
            FROM LessonRequest r
            JOIN TeacherQualification tq ON tq.SubjectId=r.SubjectId AND tq.CanTeach=1
            JOIN Teacher t ON t.Id=tq.TeacherId AND t.Active=1
            JOIN OpenDateTimeSlot ds ON ds.OpenDateId=$date
            JOIN OpenDate d ON d.Id=ds.OpenDateId AND d.IsOpen=1
            JOIN TimeSlot ts ON ts.Id=ds.TimeSlotId AND ts.Active=1
            LEFT JOIN StudentAvailability sa ON sa.ProjectId=r.ProjectId AND sa.StudentId=r.StudentId AND sa.OpenDateId=ds.OpenDateId AND sa.TimeSlotId=ds.TimeSlotId
            LEFT JOIN TeacherAvailability ta ON ta.ProjectId=r.ProjectId AND ta.TeacherId=tq.TeacherId AND ta.OpenDateId=ds.OpenDateId AND ta.TimeSlotId=ds.TimeSlotId
            WHERE (NOT EXISTS(SELECT 1 FROM StudentAvailability WHERE ProjectId=r.ProjectId AND StudentId=r.StudentId) OR COALESCE(sa.AvailabilityLevel,0)>0)
              AND (NOT EXISTS(SELECT 1 FROM TeacherAvailability WHERE ProjectId=r.ProjectId) OR COALESCE(ta.AvailabilityLevel,0)>0)
              AND NOT EXISTS(SELECT 1 FROM TeacherUnavailability u WHERE u.TeacherId=tq.TeacherId AND u.OpenDateId=ds.OpenDateId AND u.TimeSlotId=ds.TimeSlotId)
              AND NOT EXISTS(SELECT 1 FROM Assignment a JOIN LessonRequest ar ON ar.Id=a.LessonRequestId WHERE (a.IsLocked=1 OR a.IsManual=1) AND a.OpenDateId=ds.OpenDateId AND a.TimeSlotId=ds.TimeSlotId AND ar.StudentId=r.StudentId)
            ORDER BY r.Id,ts.SortOrder;
            """;
        command.Parameters.AddWithValue("$date",openDateId);
        await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id=reader.GetInt64(0);
            if(!result.TryGetValue(id,out var codes)){codes=new List<string>();result[id]=codes;}
            codes.Add(reader.GetString(1));
        }
        return result.ToDictionary(kv=>kv.Key,IReadOnlyList<string> (kv)=>kv.Value);
    }

    public async Task<EditPreview> PreviewMoveAsync(string projectPath,long assignmentId,long teacherId,long openDateId,long timeSlotId,CancellationToken cancellationToken=default)
        => await new SqliteFixedLessonService().PreviewMoveAsync(projectPath,assignmentId,teacherId,openDateId,timeSlotId,cancellationToken).ConfigureAwait(false);

    public async Task MoveAsync(string projectPath,long assignmentId,long teacherId,long openDateId,long timeSlotId,bool confirmSoftWarnings=false,string? reason=null,CancellationToken cancellationToken=default)
        => await new SqliteFixedLessonService().MoveAsync(projectPath,assignmentId,teacherId,openDateId,timeSlotId,confirmSoftWarnings,reason,cancellationToken).ConfigureAwait(false);

    public async Task SetTeacherUnavailableAsync(string projectPath,long teacherId,long openDateId,long timeSlotId,bool unavailable,CancellationToken cancellationToken=default)
        => await SetTeacherUnavailableManyAsync(projectPath,openDateId,[(teacherId,timeSlotId)],unavailable,cancellationToken).ConfigureAwait(false);

    public async Task SetTeacherUnavailableManyAsync(string projectPath,long openDateId,IReadOnlyCollection<(long TeacherId,long TimeSlotId)> targets,bool unavailable,CancellationToken cancellationToken=default)
    {
        if(targets.Count==0)throw new InvalidOperationException("講師とコマを1件以上選択してください。");
        await using var connection=await OpenAsync(projectPath,cancellationToken).ConfigureAwait(false);await SqliteProjectSchema.EnsureCurrentAsync(connection,cancellationToken).ConfigureAwait(false);await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach(var(teacherId,timeSlotId) in targets)
        {
            if(unavailable)
            {
                await using var check=connection.CreateCommand();check.Transaction=transaction;check.CommandText="SELECT EXISTS(SELECT 1 FROM Assignment WHERE TeacherId=$teacher AND OpenDateId=$date AND TimeSlotId=$slot);";
                check.Parameters.AddWithValue("$teacher",teacherId);check.Parameters.AddWithValue("$date",openDateId);check.Parameters.AddWithValue("$slot",timeSlotId);
                if(Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!=0)
                    throw new InvalidOperationException("この日時には既に配置がある講師が含まれています。先に配置を移動または削除してください。");
                await using var insert=connection.CreateCommand();insert.Transaction=transaction;insert.CommandText="INSERT OR IGNORE INTO TeacherUnavailability(TeacherId,OpenDateId,TimeSlotId) VALUES($teacher,$date,$slot);";
                insert.Parameters.AddWithValue("$teacher",teacherId);insert.Parameters.AddWithValue("$date",openDateId);insert.Parameters.AddWithValue("$slot",timeSlotId);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await using var delete=connection.CreateCommand();delete.Transaction=transaction;delete.CommandText="DELETE FROM TeacherUnavailability WHERE TeacherId=$teacher AND OpenDateId=$date AND TimeSlotId=$slot;";
                delete.Parameters.AddWithValue("$teacher",teacherId);delete.Parameters.AddWithValue("$date",openDateId);delete.Parameters.AddWithValue("$slot",timeSlotId);
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        await InsertAuditAsync(connection,transaction,"teacher_unavailability_changed","bulk",new{openDateId,count=targets.Count,unavailable},cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ScheduleSnapshot> CaptureSnapshotAsync(string projectPath,CancellationToken cancellationToken=default)
    {
        await using var connection=await OpenAsync(projectPath,cancellationToken).ConfigureAwait(false);await SqliteProjectSchema.EnsureCurrentAsync(connection,cancellationToken).ConfigureAwait(false);
        var assignments=new List<AssignmentSnapshotRow>();
        await using(var command=connection.CreateCommand())
        {
            command.CommandText="SELECT Id,LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source,SessionIndex,OptimizationRunId,IsManual,Note FROM Assignment;";
            await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                assignments.Add(new AssignmentSnapshotRow(reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2),reader.GetInt64(3),reader.GetInt64(4),reader.GetBoolean(5),reader.GetString(6),reader.GetInt32(7),reader.IsDBNull(8)?null:reader.GetInt64(8),reader.GetBoolean(9),reader.GetString(10)));
        }
        var unavailabilities=new List<TeacherUnavailabilitySnapshotRow>();
        await using(var command=connection.CreateCommand())
        {
            command.CommandText="SELECT TeacherId,OpenDateId,TimeSlotId FROM TeacherUnavailability;";
            await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                unavailabilities.Add(new TeacherUnavailabilitySnapshotRow(reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2)));
        }
        return new ScheduleSnapshot(assignments,unavailabilities);
    }

    public async Task RestoreSnapshotAsync(string projectPath,ScheduleSnapshot snapshot,CancellationToken cancellationToken=default)
    {
        await using var connection=await OpenAsync(projectPath,cancellationToken).ConfigureAwait(false);await SqliteProjectSchema.EnsureCurrentAsync(connection,cancellationToken).ConfigureAwait(false);await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using(var clearAssignments=connection.CreateCommand()){clearAssignments.Transaction=transaction;clearAssignments.CommandText="DELETE FROM Assignment;";await clearAssignments.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);}
        await using(var clearUnavailability=connection.CreateCommand()){clearUnavailability.Transaction=transaction;clearUnavailability.CommandText="DELETE FROM TeacherUnavailability;";await clearUnavailability.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);}
        foreach(var row in snapshot.Assignments)
        {
            await using var insert=connection.CreateCommand();insert.Transaction=transaction;
            insert.CommandText="INSERT INTO Assignment(Id,LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source,SessionIndex,OptimizationRunId,IsManual,Note) VALUES($id,$request,$teacher,$date,$slot,$locked,$source,$session,$run,$manual,$note);";
            insert.Parameters.AddWithValue("$id",row.Id);insert.Parameters.AddWithValue("$request",row.LessonRequestId);insert.Parameters.AddWithValue("$teacher",row.TeacherId);insert.Parameters.AddWithValue("$date",row.OpenDateId);insert.Parameters.AddWithValue("$slot",row.TimeSlotId);insert.Parameters.AddWithValue("$locked",row.IsLocked);insert.Parameters.AddWithValue("$source",row.Source);insert.Parameters.AddWithValue("$session",row.SessionIndex);insert.Parameters.AddWithValue("$run",(object?)row.OptimizationRunId??DBNull.Value);insert.Parameters.AddWithValue("$manual",row.IsManual);insert.Parameters.AddWithValue("$note",row.Note);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        foreach(var row in snapshot.TeacherUnavailabilities)
        {
            await using var insert=connection.CreateCommand();insert.Transaction=transaction;
            insert.CommandText="INSERT INTO TeacherUnavailability(TeacherId,OpenDateId,TimeSlotId) VALUES($teacher,$date,$slot);";
            insert.Parameters.AddWithValue("$teacher",row.TeacherId);insert.Parameters.AddWithValue("$date",row.OpenDateId);insert.Parameters.AddWithValue("$slot",row.TimeSlotId);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await InsertAuditAsync(connection,transaction,"schedule_snapshot_restored","project:1",new{assignments=snapshot.Assignments.Count,unavailabilities=snapshot.TeacherUnavailabilities.Count},cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static readonly Dictionary<string,string> AuditActionLabels = new()
    {
        ["manual_assignment_added"]="手動配置を追加",
        ["preconfirmed_assignment_added"]="事前確定として追加",
        ["manual_assignment_moved"]="配置を移動",
        ["manual_assignment_removed"]="手動配置を削除",
        ["assignment_lock_changed"]="ロック状態を変更",
        ["automatic_assignments_reset"]="自動配置をリセット",
        ["all_assignments_reset"]="すべての配置をリセット",
        ["teacher_unavailability_changed"]="出勤可否を変更",
        ["schedule_snapshot_restored"]="元に戻す・やり直すを実行",
    };

    public async Task<IReadOnlyList<AuditHistoryEntry>> GetAuditHistoryAsync(string projectPath,int limit=50,CancellationToken cancellationToken=default)
    {
        await using var connection=await OpenAsync(projectPath,cancellationToken).ConfigureAwait(false);await SqliteProjectSchema.EnsureCurrentAsync(connection,cancellationToken).ConfigureAwait(false);
        await using var command=connection.CreateCommand();
        command.CommandText="SELECT TimestampUtc,Action,Reason FROM AuditLog WHERE Action IN ('manual_assignment_added','preconfirmed_assignment_added','manual_assignment_moved','manual_assignment_removed','assignment_lock_changed','automatic_assignments_reset','all_assignments_reset','teacher_unavailability_changed','schedule_snapshot_restored') ORDER BY Id DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit",limit);
        var result=new List<AuditHistoryEntry>();
        await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var action=reader.GetString(1);
            result.Add(new AuditHistoryEntry(DateTimeOffset.Parse(reader.GetString(0),CultureInfo.InvariantCulture),AuditActionLabels.GetValueOrDefault(action,action),reader.IsDBNull(2)?null:reader.GetString(2)));
        }
        return result;
    }

    public async Task<ScheduleLabelSet> GetLabelSetAsync(string projectPath,CancellationToken cancellationToken=default)
    {
        await using var connection=await OpenAsync(projectPath,cancellationToken).ConfigureAwait(false);await SqliteProjectSchema.EnsureCurrentAsync(connection,cancellationToken).ConfigureAwait(false);
        var requests=new Dictionary<long,string>();
        await using(var command=connection.CreateCommand())
        {
            command.CommandText="SELECT r.Id,st.Name||'（'||st.Grade||'） / '||su.DisplayName FROM LessonRequest r JOIN Student st ON st.Id=r.StudentId JOIN Subject su ON su.Id=r.SubjectId;";
            await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))requests[reader.GetInt64(0)]=reader.GetString(1);
        }
        var teachers=new Dictionary<long,string>();
        await using(var command=connection.CreateCommand())
        {
            command.CommandText="SELECT Id,Name FROM Teacher;";
            await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))teachers[reader.GetInt64(0)]=reader.GetString(1);
        }
        var dates=new Dictionary<long,string>();
        await using(var command=connection.CreateCommand())
        {
            command.CommandText="SELECT Id,Date FROM OpenDate;";
            await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var date=DateOnly.Parse(reader.GetString(1),CultureInfo.InvariantCulture);
                dates[reader.GetInt64(0)]=$"{date:yyyy-MM-dd}({JapaneseWeekday(date)})";
            }
        }
        var slots=new Dictionary<long,string>();
        await using(var command=connection.CreateCommand())
        {
            command.CommandText="SELECT Id,DisplayName FROM TimeSlot;";
            await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))slots[reader.GetInt64(0)]=reader.GetString(1);
        }
        return new ScheduleLabelSet(requests,teachers,dates,slots);
    }

    private static async Task InsertAuditAsync(SqliteConnection connection,SqliteTransaction transaction,string action,string entityId,object? summary,CancellationToken cancellationToken){await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="INSERT INTO AuditLog(ProjectId,TimestampUtc,Action,EntityType,EntityId,AfterJson,Reason,Source,OperationId) VALUES(1,$utc,$action,'assignment',$entity,$after,'時間割手動編集','manual',$operation);";command.Parameters.AddWithValue("$utc",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture));command.Parameters.AddWithValue("$action",action);command.Parameters.AddWithValue("$entity",entityId);command.Parameters.AddWithValue("$after",summary is null?DBNull.Value:JsonSerializer.Serialize(summary));command.Parameters.AddWithValue("$operation",Guid.NewGuid().ToString("N"));await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);}
    private static async Task<SqliteConnection> OpenAsync(string path,CancellationToken cancellationToken){var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.GetFullPath(path),Mode=SqliteOpenMode.ReadWrite,ForeignKeys=true,Pooling=false}.ToString());await connection.OpenAsync(cancellationToken).ConfigureAwait(false);return connection;}
}
