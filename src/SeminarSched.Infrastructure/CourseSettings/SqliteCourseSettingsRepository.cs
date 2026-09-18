using System.Globalization;
using Microsoft.Data.Sqlite;
using SeminarSched.Application.CourseSettings;
using SeminarSched.Domain.CourseSettings;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.CourseSettings;

public sealed class SqliteCourseSettingsRepository : ICourseSettingsRepository
{
    public async Task<IReadOnlyList<TimeSlot>> GetTimeSlotsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken); await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT Id,Code,DisplayName,StartTime,EndTime,SortOrder,Active FROM TimeSlot ORDER BY SortOrder,Id;";
        var result = new List<TimeSlot>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new TimeSlot(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), TimeOnly.ParseExact(reader.GetString(3), "HH:mm", CultureInfo.InvariantCulture), TimeOnly.ParseExact(reader.GetString(4), "HH:mm", CultureInfo.InvariantCulture), reader.GetInt32(5), reader.GetBoolean(6)));
        return result;
    }

    public async Task<TimeSlot> SaveTimeSlotAsync(string projectPath, TimeSlot slot, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken); await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = slot.Id == 0
            ? "INSERT INTO TimeSlot(Code,DisplayName,StartTime,EndTime,SortOrder,Active) VALUES(@code,@name,@start,@end,@sort,@active); SELECT last_insert_rowid();"
            : "UPDATE TimeSlot SET Code=@code,DisplayName=@name,StartTime=@start,EndTime=@end,SortOrder=@sort,Active=@active WHERE Id=@id; SELECT changes();";
        command.Parameters.AddWithValue("@code", slot.Code); command.Parameters.AddWithValue("@name", slot.DisplayName);
        command.Parameters.AddWithValue("@start", slot.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture)); command.Parameters.AddWithValue("@end", slot.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@sort", slot.SortOrder); command.Parameters.AddWithValue("@active", slot.Active); if (slot.Id != 0) command.Parameters.AddWithValue("@id", slot.Id);
        var value = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)); if (slot.Id != 0 && value != 1) throw new InvalidOperationException("更新対象が見つかりません。");
        return slot with { Id = slot.Id == 0 ? value : slot.Id };
    }

    // Assignment.TimeSlotId は ON DELETE RESTRICT なので、既に時間割配置で使われているコマを
    // 削除しようとするとSqliteExceptionが飛ぶ（呼び出し側で捕捉してエラー表示する）。
    // OpenDateTimeSlot・各種Availability・Unavailabilityは ON DELETE CASCADE で自動的に削除される。
    public async Task DeleteTimeSlotAsync(string projectPath, long timeSlotId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken); await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM TimeSlot WHERE Id=@id; SELECT changes();";
        command.Parameters.AddWithValue("@id", timeSlotId);
        var changed = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        if (changed != 1) throw new InvalidOperationException("削除対象のコマが見つかりません。");
    }

    public async Task<IReadOnlyList<CourseDay>> GetCourseDaysAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken); await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT od.Id,od.Date,od.IsOpen,od.Note,odts.TimeSlotId FROM OpenDate od LEFT JOIN OpenDateTimeSlot odts ON odts.OpenDateId=od.Id ORDER BY od.Date,odts.TimeSlotId;";
        var rows = new Dictionary<long, (DateOnly Date, bool Open, string Note, List<long> Slots)>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) { var id=reader.GetInt64(0); if(!rows.TryGetValue(id,out var row)) row=(DateOnly.ParseExact(reader.GetString(1),"yyyy-MM-dd",CultureInfo.InvariantCulture),reader.GetBoolean(2),reader.GetString(3),[]); if(!reader.IsDBNull(4)) row.Slots.Add(reader.GetInt64(4)); rows[id]=row; }
        return rows.Values.Select(x => new CourseDay(x.Date,x.Open,x.Note,x.Slots)).ToArray();
    }

    public async Task SaveCourseDayAsync(string projectPath, CourseDay day, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken); await EnsureSchemaAsync(connection, cancellationToken); await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var update = connection.CreateCommand(); update.Transaction=(SqliteTransaction)transaction; update.CommandText="UPDATE OpenDate SET IsOpen=@open,Note=@note WHERE Date=@date; SELECT Id FROM OpenDate WHERE Date=@date;"; update.Parameters.AddWithValue("@open",day.IsOpen); update.Parameters.AddWithValue("@note",day.Note??""); update.Parameters.AddWithValue("@date",day.Date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
        var id = await update.ExecuteScalarAsync(cancellationToken) ?? throw new ArgumentException("講習期間外の日付です。");
        await using var delete=connection.CreateCommand(); delete.Transaction=(SqliteTransaction)transaction; delete.CommandText="DELETE FROM OpenDateTimeSlot WHERE OpenDateId=@id;"; delete.Parameters.AddWithValue("@id",id); await delete.ExecuteNonQueryAsync(cancellationToken);
        if(day.IsOpen) foreach(var slotId in day.EnabledTimeSlotIds.Distinct()){ await using var insert=connection.CreateCommand(); insert.Transaction=(SqliteTransaction)transaction; insert.CommandText="INSERT INTO OpenDateTimeSlot(OpenDateId,TimeSlotId) VALUES(@date,@slot);"; insert.Parameters.AddWithValue("@date",id); insert.Parameters.AddWithValue("@slot",slotId); await insert.ExecuteNonQueryAsync(cancellationToken); }
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<SqliteConnection> OpenAsync(string path,CancellationToken token){var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.GetFullPath(path),Mode=SqliteOpenMode.ReadWrite,ForeignKeys=true,Pooling=false}.ToString());await c.OpenAsync(token);return c;}
    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken token)
    {
        await SqliteProjectSchema.EnsureCurrentAsync(connection, token).ConfigureAwait(false);
    }
}
