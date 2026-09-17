using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SeminarSched.Application.Importing;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Importing;

public sealed class SqliteAvailabilityMatrixService : IAvailabilityMatrixService
{
    public async Task<IReadOnlyList<AvailabilityEntityOption>> GetEntitiesAsync(string projectPath, AvailabilityEntityKind kind, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = kind == AvailabilityEntityKind.Student
            ? "SELECT Id,ExternalId||' '||Name FROM Student WHERE Active=1 ORDER BY ExternalId;"
            : "SELECT Id,ExternalId||' '||Name FROM Teacher WHERE Active=1 ORDER BY ExternalId;";
        var result = new List<AvailabilityEntityOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new AvailabilityEntityOption(reader.GetInt64(0), reader.GetString(1)));
        return result;
    }

    public async Task<IReadOnlyList<AvailabilityDateOption>> GetOpenDatesAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Date FROM OpenDate WHERE IsOpen=1 ORDER BY Date;";
        var result = new List<AvailabilityDateOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new AvailabilityDateOption(reader.GetInt64(0), reader.GetString(1)));
        return result;
    }

    public async Task<IReadOnlyList<AvailabilitySlotOption>> GetSlotsForDateAsync(string projectPath, long openDateId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ts.Id,ts.DisplayName||' '||ts.StartTime||'-'||ts.EndTime FROM OpenDateTimeSlot ds JOIN TimeSlot ts ON ts.Id=ds.TimeSlotId WHERE ds.OpenDateId=$date AND ts.Active=1 ORDER BY ts.SortOrder;";
        command.Parameters.AddWithValue("$date", openDateId);
        var result = new List<AvailabilitySlotOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new AvailabilitySlotOption(reader.GetInt64(0), reader.GetString(1)));
        return result;
    }

    public async Task<IReadOnlyList<AvailabilityMatrixRow>> GetDayMatrixAsync(string projectPath, AvailabilityEntityKind kind, long openDateId, IReadOnlyCollection<long> entityIds, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        var (entityTable, availabilityTable, entityColumn) = TableNames(kind);

        var slots = new List<(long Id, string Label)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ts.Id,ts.DisplayName||' '||ts.StartTime||'-'||ts.EndTime FROM OpenDateTimeSlot ds JOIN TimeSlot ts ON ts.Id=ds.TimeSlotId WHERE ds.OpenDateId=$date AND ts.Active=1 ORDER BY ts.SortOrder;";
            command.Parameters.AddWithValue("$date", openDateId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) slots.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        var result = new List<AvailabilityMatrixRow>();
        foreach (var entityId in entityIds)
        {
            string label;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT ExternalId||' '||Name FROM {entityTable} WHERE Id=$id;";
                command.Parameters.AddWithValue("$id", entityId);
                var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (value is null) continue;
                label = (string)value;
            }
            var levels = new Dictionary<long, int>();
            foreach (var slot in slots)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"SELECT AvailabilityLevel FROM {availabilityTable} WHERE {entityColumn}=$entity AND OpenDateId=$date AND TimeSlotId=$slot;";
                command.Parameters.AddWithValue("$entity", entityId);
                command.Parameters.AddWithValue("$date", openDateId);
                command.Parameters.AddWithValue("$slot", slot.Id);
                var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                levels[slot.Id] = value is null ? 1 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            result.Add(new AvailabilityMatrixRow(entityId, label, levels));
        }
        return result;
    }

    public async Task SetLevelAsync(string projectPath, AvailabilityEntityKind kind, IReadOnlyCollection<long> entityIds, long openDateId, long timeSlotId, int level, CancellationToken cancellationToken = default)
    {
        if (level is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(level));
        if (entityIds.Count == 0) throw new InvalidOperationException("対象を1件以上選択してください。");
        var (entityTable, availabilityTable, entityColumn) = TableNames(kind);

        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        await using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT EXISTS(SELECT 1 FROM OpenDateTimeSlot WHERE OpenDateId=$date AND TimeSlotId=$slot);";
            check.Parameters.AddWithValue("$date", openDateId);
            check.Parameters.AddWithValue("$slot", timeSlotId);
            if (Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 0)
                throw new InvalidOperationException("この日付では使用できないコマです。");
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var entityId in entityIds)
        {
            await using var upsert = connection.CreateCommand();
            upsert.Transaction = transaction;
            upsert.CommandText = $"""
                INSERT INTO {availabilityTable}(ProjectId,{entityColumn},OpenDateId,TimeSlotId,AvailabilityLevel)
                VALUES(1,$entity,$date,$slot,$level)
                ON CONFLICT(ProjectId,{entityColumn},OpenDateId,TimeSlotId) DO UPDATE SET AvailabilityLevel=excluded.AvailabilityLevel;
                """;
            upsert.Parameters.AddWithValue("$entity", entityId);
            upsert.Parameters.AddWithValue("$date", openDateId);
            upsert.Parameters.AddWithValue("$slot", timeSlotId);
            upsert.Parameters.AddWithValue("$level", level);
            await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (kind == AvailabilityEntityKind.Teacher)
            {
                await using var removeUnavailable = connection.CreateCommand();
                removeUnavailable.Transaction = transaction;
                removeUnavailable.CommandText = "DELETE FROM TeacherUnavailability WHERE TeacherId=$entity AND OpenDateId=$date AND TimeSlotId=$slot;";
                removeUnavailable.Parameters.AddWithValue("$entity", entityId);
                removeUnavailable.Parameters.AddWithValue("$date", openDateId);
                removeUnavailable.Parameters.AddWithValue("$slot", timeSlotId);
                await removeUnavailable.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                if (level == 0)
                {
                    await using var insertUnavailable = connection.CreateCommand();
                    insertUnavailable.Transaction = transaction;
                    insertUnavailable.CommandText = "INSERT OR IGNORE INTO TeacherUnavailability(TeacherId,OpenDateId,TimeSlotId) VALUES($entity,$date,$slot);";
                    insertUnavailable.Parameters.AddWithValue("$entity", entityId);
                    insertUnavailable.Parameters.AddWithValue("$date", openDateId);
                    insertUnavailable.Parameters.AddWithValue("$slot", timeSlotId);
                    await insertUnavailable.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }

        await using (var audit = connection.CreateCommand())
        {
            audit.Transaction = transaction;
            audit.CommandText = "INSERT INTO AuditLog(ProjectId,TimestampUtc,Action,EntityType,EntityId,AfterJson,Reason,Source) VALUES(1,$utc,'availability_bulk_edited',$entityType,$entityIds,$after,'手動可用性編集','manual');";
            audit.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            audit.Parameters.AddWithValue("$entityType", kind == AvailabilityEntityKind.Student ? "student_availability" : "teacher_availability");
            audit.Parameters.AddWithValue("$entityIds", string.Join(",", entityIds));
            audit.Parameters.AddWithValue("$after", JsonSerializer.Serialize(new { openDateId, timeSlotId, level, count = entityIds.Count }));
            await audit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static (string EntityTable, string AvailabilityTable, string EntityColumn) TableNames(AvailabilityEntityKind kind) => kind == AvailabilityEntityKind.Student
        ? ("Student", "StudentAvailability", "StudentId")
        : ("Teacher", "TeacherAvailability", "TeacherId");

    private static async Task<SqliteConnection> OpenAsync(string projectPath, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(projectPath), Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true, Pooling = false }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
