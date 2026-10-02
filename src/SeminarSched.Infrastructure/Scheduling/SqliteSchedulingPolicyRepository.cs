using Microsoft.Data.Sqlite;
using SeminarSched.Application.Scheduling;
using SeminarSched.Domain.Scheduling;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Scheduling;

public sealed class SqliteSchedulingPolicyRepository : ISchedulingPolicyRepository
{
    public async Task<SchedulingPolicy> GetAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken); await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MaxStudentsPerTeacher,TeacherCountPerDayPreference,TeacherLoadBalancePreference,
                   StudentAttendanceDaysPreference,TeacherAttendanceDaysPreference,PairingSizePreference,
                   TimeOfDayPreference,TeacherStudentConsecutivePreference,MaxConcurrentSeats,ContinueBeyondNominalTimeIfIncomplete,
                   PreferenceOrder
            FROM SchedulingPolicy WHERE ProjectId=1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return SchedulingPolicy.Default;

        return new SchedulingPolicy(
            reader.GetInt32(0),
            (TeacherCountPerDayPreference)reader.GetInt32(1),
            (TeacherLoadBalancePreference)reader.GetInt32(2),
            (StudentAttendanceDaysPreference)reader.GetInt32(3),
            (TeacherAttendanceDaysPreference)reader.GetInt32(4),
            (PairingSizePreference)reader.GetInt32(5),
            (TimeOfDayPreference)reader.GetInt32(6),
            (TeacherStudentConsecutivePreference)reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetBoolean(9),
            ParsePreferenceOrder(reader.GetString(10)));
    }

    // ユーザー要望（checkpoint123）「探索方針の優先度を変えられるようにしたい」。カンマ区切りの
    // SchedulingPolicyDimension序数列をパースする。壊れた値（列数不足・重複・範囲外）が万一入って
    // いた場合は、ソルバーがPreferenceOrderの検証（SchedulingPolicyコンストラクタ）で例外にしてしまう
    // より、黙って既定順へフォールバックする方が安全なため、そのように倒す。
    private static IReadOnlyList<SchedulingPolicyDimension> ParsePreferenceOrder(string raw)
    {
        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var dimensions = new SchedulingPolicyDimension[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out var value) || !Enum.IsDefined(typeof(SchedulingPolicyDimension), value))
                return SchedulingPolicy.DefaultPreferenceOrder;
            dimensions[i] = (SchedulingPolicyDimension)value;
        }
        return dimensions.Length == SchedulingPolicy.DefaultPreferenceOrder.Count && dimensions.Distinct().Count() == dimensions.Length
            ? dimensions
            : SchedulingPolicy.DefaultPreferenceOrder;
    }

    private static string SerializePreferenceOrder(IReadOnlyList<SchedulingPolicyDimension> order) =>
        string.Join(',', order.Select(dimension => (int)dimension));

    public async Task SaveAsync(string projectPath, SchedulingPolicy policy, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken); await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SchedulingPolicy(ProjectId,MaxStudentsPerTeacher,TeacherCountPerDayPreference,TeacherLoadBalancePreference,StudentAttendanceDaysPreference,TeacherAttendanceDaysPreference,PairingSizePreference,TimeOfDayPreference,TeacherStudentConsecutivePreference,MaxConcurrentSeats,ContinueBeyondNominalTimeIfIncomplete,PreferenceOrder)
            VALUES(1,@maxStudents,@teacherCountPerDay,@teacherLoadBalance,@studentAttendanceDays,@teacherAttendanceDays,@pairingSize,@timeOfDay,@teacherStudentConsecutive,@maxSeats,@continueBeyond,@preferenceOrder)
            ON CONFLICT(ProjectId) DO UPDATE SET
                MaxStudentsPerTeacher=excluded.MaxStudentsPerTeacher,
                TeacherCountPerDayPreference=excluded.TeacherCountPerDayPreference,
                TeacherLoadBalancePreference=excluded.TeacherLoadBalancePreference,
                StudentAttendanceDaysPreference=excluded.StudentAttendanceDaysPreference,
                TeacherAttendanceDaysPreference=excluded.TeacherAttendanceDaysPreference,
                PairingSizePreference=excluded.PairingSizePreference,
                TimeOfDayPreference=excluded.TimeOfDayPreference,
                TeacherStudentConsecutivePreference=excluded.TeacherStudentConsecutivePreference,
                MaxConcurrentSeats=excluded.MaxConcurrentSeats,
                ContinueBeyondNominalTimeIfIncomplete=excluded.ContinueBeyondNominalTimeIfIncomplete,
                PreferenceOrder=excluded.PreferenceOrder;
            """;
        command.Parameters.AddWithValue("@maxStudents", policy.MaxStudentsPerTeacher);
        command.Parameters.AddWithValue("@teacherCountPerDay", (int)policy.TeacherCountPerDayPreference);
        command.Parameters.AddWithValue("@teacherLoadBalance", (int)policy.TeacherLoadBalancePreference);
        command.Parameters.AddWithValue("@studentAttendanceDays", (int)policy.StudentAttendanceDaysPreference);
        command.Parameters.AddWithValue("@teacherAttendanceDays", (int)policy.TeacherAttendanceDaysPreference);
        command.Parameters.AddWithValue("@pairingSize", (int)policy.PairingSizePreference);
        command.Parameters.AddWithValue("@timeOfDay", (int)policy.TimeOfDayPreference);
        command.Parameters.AddWithValue("@teacherStudentConsecutive", (int)policy.TeacherStudentConsecutivePreference);
        command.Parameters.AddWithValue("@maxSeats", policy.MaxConcurrentSeats);
        command.Parameters.AddWithValue("@continueBeyond", policy.ContinueBeyondNominalTimeIfIncomplete);
        command.Parameters.AddWithValue("@preferenceOrder", SerializePreferenceOrder(policy.PreferenceOrder));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<SqliteConnection> OpenAsync(string path, CancellationToken token)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true, Pooling = false }.ToString());
        await connection.OpenAsync(token);
        return connection;
    }
}
