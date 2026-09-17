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

    private static async Task InsertAuditAsync(SqliteConnection connection,SqliteTransaction transaction,string action,string entityId,object? summary,CancellationToken cancellationToken){await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="INSERT INTO AuditLog(ProjectId,TimestampUtc,Action,EntityType,EntityId,AfterJson,Reason,Source,OperationId) VALUES(1,$utc,$action,'assignment',$entity,$after,'時間割手動編集','manual',$operation);";command.Parameters.AddWithValue("$utc",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture));command.Parameters.AddWithValue("$action",action);command.Parameters.AddWithValue("$entity",entityId);command.Parameters.AddWithValue("$after",summary is null?DBNull.Value:JsonSerializer.Serialize(summary));command.Parameters.AddWithValue("$operation",Guid.NewGuid().ToString("N"));await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);}
    private static async Task<SqliteConnection> OpenAsync(string path,CancellationToken cancellationToken){var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.GetFullPath(path),Mode=SqliteOpenMode.ReadWrite,ForeignKeys=true,Pooling=false}.ToString());await connection.OpenAsync(cancellationToken).ConfigureAwait(false);return connection;}
}
