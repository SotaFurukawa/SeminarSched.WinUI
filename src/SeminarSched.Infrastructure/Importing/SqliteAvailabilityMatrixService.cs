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
            ? "SELECT Id,Name FROM Student WHERE Active=1 ORDER BY ExternalId;"
            : "SELECT Id,Name FROM Teacher WHERE Active=1 ORDER BY ExternalId;";
        var result = new List<AvailabilityEntityOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new AvailabilityEntityOption(reader.GetInt64(0), reader.GetString(1)));
        return result;
    }

    // ユーザー要望（checkpoint111）「可用性の手動編集をカレンダーで変更できるようにしたい」への対応。
    // 選択中の1名について、講習期間内の全開講日×その日の全コマの現在値を、日付ごとに個別クエリを
    // 繰り返す旧方式ではなく3回のクエリでまとめて取得する（開講日数が多いプロジェクトでの低速なPCでの
    // 遅延を避けるため）。
    public async Task<AvailabilityCalendar> GetCalendarAsync(string projectPath, AvailabilityEntityKind kind, long entityId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        var (_, availabilityTable, entityColumn) = TableNames(kind);

        var dates = new List<(long Id, DateOnly Date)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id,Date FROM OpenDate WHERE IsOpen=1 ORDER BY Date;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                dates.Add((reader.GetInt64(0), DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        var slotsByDate = new Dictionary<long, List<(long Id, string Label)>>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ds.OpenDateId,ts.Id,ts.DisplayName||' '||ts.StartTime||'-'||ts.EndTime FROM OpenDateTimeSlot ds JOIN TimeSlot ts ON ts.Id=ds.TimeSlotId WHERE ts.Active=1 ORDER BY ds.OpenDateId,ts.SortOrder;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var dateId = reader.GetInt64(0);
                if (!slotsByDate.TryGetValue(dateId, out var list)) slotsByDate[dateId] = list = [];
                list.Add((reader.GetInt64(1), reader.GetString(2)));
            }
        }

        var levels = new Dictionary<(long DateId, long SlotId), int>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT OpenDateId,TimeSlotId,AvailabilityLevel FROM {availabilityTable} WHERE {entityColumn}=$entity;";
            command.Parameters.AddWithValue("$entity", entityId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                levels[(reader.GetInt64(0), reader.GetInt64(1))] = reader.GetInt32(2);
        }

        var days = dates.Select(d => new AvailabilityCalendarDay(
            d.Id,
            d.Date,
            (slotsByDate.TryGetValue(d.Id, out var slots) ? slots : []).Select(s =>
                new AvailabilityCalendarSlot(s.Id, s.Label, levels.TryGetValue((d.Id, s.Id), out var level) ? level : 1)).ToArray()
        )).ToArray();

        return new AvailabilityCalendar(days);
    }

    // ユーザー要望（checkpoint111）「保存ボタンはなしで、即座に反映されるようにしてほしい」への対応。
    // カレンダーのチェックボックス1つのトグルにつき1回、この1件だけを即座に反映する。
    public async Task SetLevelAsync(string projectPath, AvailabilityEntityKind kind, long entityId, long openDateId, long timeSlotId, int level, CancellationToken cancellationToken = default)
    {
        // ユーザー指示（checkpoint111）「出勤出席可能日の可能と優先がありますが...優先は削除してください」
        // への対応。0(不可)/1(可能)の2値のみを許可する（従来の2=優先は廃止）。
        if (level is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(level));
        var (_, availabilityTable, entityColumn) = TableNames(kind);

        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT EXISTS(SELECT 1 FROM OpenDateTimeSlot WHERE OpenDateId=$date AND TimeSlotId=$slot);";
            check.Parameters.AddWithValue("$date", openDateId);
            check.Parameters.AddWithValue("$slot", timeSlotId);
            if (Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 0)
                throw new InvalidOperationException("この日付では使用できないコマが選択されています。");
        }

        await using (var upsert = connection.CreateCommand())
        {
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
        }

        if (kind == AvailabilityEntityKind.Teacher)
        {
            await using (var removeUnavailable = connection.CreateCommand())
            {
                removeUnavailable.Transaction = transaction;
                removeUnavailable.CommandText = "DELETE FROM TeacherUnavailability WHERE TeacherId=$entity AND OpenDateId=$date AND TimeSlotId=$slot;";
                removeUnavailable.Parameters.AddWithValue("$entity", entityId);
                removeUnavailable.Parameters.AddWithValue("$date", openDateId);
                removeUnavailable.Parameters.AddWithValue("$slot", timeSlotId);
                await removeUnavailable.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
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

        await using (var audit = connection.CreateCommand())
        {
            audit.Transaction = transaction;
            audit.CommandText = "INSERT INTO AuditLog(ProjectId,TimestampUtc,Action,EntityType,EntityId,AfterJson,Reason,Source) VALUES(1,$utc,'availability_edited',$entityType,$entityId,$after,'手動可用性編集','manual');";
            audit.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            audit.Parameters.AddWithValue("$entityType", kind == AvailabilityEntityKind.Student ? "student_availability" : "teacher_availability");
            audit.Parameters.AddWithValue("$entityId", entityId.ToString(CultureInfo.InvariantCulture));
            audit.Parameters.AddWithValue("$after", JsonSerializer.Serialize(new { openDateId, timeSlotId, level }));
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
