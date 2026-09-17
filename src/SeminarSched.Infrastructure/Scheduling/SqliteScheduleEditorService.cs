using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SeminarSched.Application.Scheduling;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Scheduling;

public sealed class SqliteScheduleEditorService : IScheduleEditorService
{
    public async Task<IReadOnlyList<ScheduleAssignmentItem>> GetAssignmentsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await using var connection=await OpenAsync(projectPath,cancellationToken).ConfigureAwait(false);await SqliteProjectSchema.EnsureCurrentAsync(connection,cancellationToken).ConfigureAwait(false);
        await using var command=connection.CreateCommand();command.CommandText="""
            SELECT a.Id,a.LessonRequestId,a.TeacherId,a.OpenDateId,a.TimeSlotId,a.IsLocked,a.IsManual,a.Source,
                   d.Date||' '||ts.DisplayName||' / '||st.ExternalId||' '||st.Name||' / '||su.DisplayName||' / '||te.ExternalId||' '||te.Name
            FROM Assignment a JOIN LessonRequest r ON r.Id=a.LessonRequestId JOIN Student st ON st.Id=r.StudentId JOIN Subject su ON su.Id=r.SubjectId JOIN Teacher te ON te.Id=a.TeacherId JOIN OpenDate d ON d.Id=a.OpenDateId JOIN TimeSlot ts ON ts.Id=a.TimeSlotId
            ORDER BY d.Date,ts.SortOrder,te.ExternalId,st.ExternalId;
            """;var result=new List<ScheduleAssignmentItem>();await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))result.Add(new ScheduleAssignmentItem(reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2),reader.GetInt64(3),reader.GetInt64(4),reader.GetBoolean(5),reader.GetBoolean(6),reader.GetString(7),reader.GetString(8)));return result;
    }

    public async Task AddManualAsync(string projectPath,long lessonRequestId,long teacherId,long openDateId,long timeSlotId,bool isLocked,CancellationToken cancellationToken=default)
    {
        await new SqliteFixedLessonService().AddManualAsync(projectPath,lessonRequestId,teacherId,openDateId,timeSlotId,isLocked,cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveManualAsync(string projectPath,long assignmentId,CancellationToken cancellationToken=default)
    {
        await using var connection=await OpenAsync(projectPath,cancellationToken).ConfigureAwait(false);await SqliteProjectSchema.EnsureCurrentAsync(connection,cancellationToken).ConfigureAwait(false);await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="DELETE FROM Assignment WHERE Id=$id AND IsManual=1;";command.Parameters.AddWithValue("$id",assignmentId);if(await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false)!=1)throw new InvalidOperationException("削除できる手動配置が見つかりません。");await InsertAuditAsync(connection,transaction,"manual_assignment_removed",assignmentId.ToString(CultureInfo.InvariantCulture),null,cancellationToken).ConfigureAwait(false);await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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
            command.CommandText="SELECT DISTINCT t.Id,t.ExternalId||' '||t.Name FROM Teacher t JOIN TeacherQualification q ON q.TeacherId=t.Id AND q.CanTeach=1 WHERE t.Active=1 ORDER BY t.ExternalId;";
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

        var teachers=qualified
            .Where(t=>extraTeacherIds.Contains(t.TeacherId)||slots.Any(s=>!IsBlocked(t.TeacherId,s.TimeSlotId)))
            .ToList();

        var cardsByCell=new Dictionary<(long TeacherId,long TimeSlotId),List<BoardCard>>();
        await using(var command=connection.CreateCommand())
        {
            command.CommandText="""
                SELECT a.Id,a.TeacherId,a.TimeSlotId,r.StudentId,st.ExternalId||' '||st.Name,su.DisplayName,a.IsManual,a.IsLocked
                FROM Assignment a JOIN LessonRequest r ON r.Id=a.LessonRequestId JOIN Student st ON st.Id=r.StudentId JOIN Subject su ON su.Id=r.SubjectId
                WHERE a.OpenDateId=$date ORDER BY st.ExternalId;
                """;
            command.Parameters.AddWithValue("$date",openDateId);
            await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var key=(reader.GetInt64(1),reader.GetInt64(2));
                var card=new BoardCard(reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2),reader.GetInt64(3),reader.GetString(4),reader.GetString(5),reader.GetBoolean(6),reader.GetBoolean(7));
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

    public async Task<IReadOnlyList<UnplacedSessionOption>> GetUnplacedSessionsAsync(string projectPath,CancellationToken cancellationToken=default)
    {
        await using var connection=await OpenAsync(projectPath,cancellationToken).ConfigureAwait(false);await SqliteProjectSchema.EnsureCurrentAsync(connection,cancellationToken).ConfigureAwait(false);
        await using var command=connection.CreateCommand();command.CommandText="""
            SELECT r.Id,st.ExternalId||' '||st.Name||' / '||su.DisplayName,r.RequiredSessions-COUNT(a.Id) AS remaining
            FROM LessonRequest r JOIN Student st ON st.Id=r.StudentId JOIN Subject su ON su.Id=r.SubjectId
            LEFT JOIN Assignment a ON a.LessonRequestId=r.Id
            GROUP BY r.Id HAVING remaining>0 ORDER BY st.ExternalId;
            """;
        var result=new List<UnplacedSessionOption>();await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new UnplacedSessionOption(reader.GetInt64(0),reader.GetString(1),reader.GetInt32(2)));
        return result;
    }

    public async Task MoveAsync(string projectPath,long assignmentId,long teacherId,long openDateId,long timeSlotId,CancellationToken cancellationToken=default)
        => await new SqliteFixedLessonService().MoveAsync(projectPath,assignmentId,teacherId,openDateId,timeSlotId,cancellationToken).ConfigureAwait(false);

    public async Task SetTeacherUnavailableAsync(string projectPath,long teacherId,long openDateId,long timeSlotId,bool unavailable,CancellationToken cancellationToken=default)
    {
        await using var connection=await OpenAsync(projectPath,cancellationToken).ConfigureAwait(false);await SqliteProjectSchema.EnsureCurrentAsync(connection,cancellationToken).ConfigureAwait(false);await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if(unavailable)
        {
            await using var check=connection.CreateCommand();check.Transaction=transaction;check.CommandText="SELECT EXISTS(SELECT 1 FROM Assignment WHERE TeacherId=$teacher AND OpenDateId=$date AND TimeSlotId=$slot);";
            check.Parameters.AddWithValue("$teacher",teacherId);check.Parameters.AddWithValue("$date",openDateId);check.Parameters.AddWithValue("$slot",timeSlotId);
            if(Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!=0)
                throw new InvalidOperationException("この日時には既に配置があります。先に配置を移動または削除してください。");
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
        await InsertAuditAsync(connection,transaction,"teacher_unavailability_changed",teacherId.ToString(CultureInfo.InvariantCulture),new{openDateId,timeSlotId,unavailable},cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertAuditAsync(SqliteConnection connection,SqliteTransaction transaction,string action,string entityId,object? summary,CancellationToken cancellationToken){await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="INSERT INTO AuditLog(ProjectId,TimestampUtc,Action,EntityType,EntityId,AfterJson,Reason,Source,OperationId) VALUES(1,$utc,$action,'assignment',$entity,$after,'時間割手動編集','manual',$operation);";command.Parameters.AddWithValue("$utc",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture));command.Parameters.AddWithValue("$action",action);command.Parameters.AddWithValue("$entity",entityId);command.Parameters.AddWithValue("$after",summary is null?DBNull.Value:JsonSerializer.Serialize(summary));command.Parameters.AddWithValue("$operation",Guid.NewGuid().ToString("N"));await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);}
    private static async Task<SqliteConnection> OpenAsync(string path,CancellationToken cancellationToken){var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.GetFullPath(path),Mode=SqliteOpenMode.ReadWrite,ForeignKeys=true,Pooling=false}.ToString());await connection.OpenAsync(cancellationToken).ConfigureAwait(false);return connection;}
}
