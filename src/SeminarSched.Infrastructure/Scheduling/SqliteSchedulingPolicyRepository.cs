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
                   TimeOfDayPreference,MaxConcurrentSeats,ContinueBeyondNominalTimeIfIncomplete
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
            reader.GetInt32(7),
            reader.GetBoolean(8));
    }

    public async Task SaveAsync(string projectPath, SchedulingPolicy policy, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken); await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SchedulingPolicy(ProjectId,MaxStudentsPerTeacher,TeacherCountPerDayPreference,TeacherLoadBalancePreference,StudentAttendanceDaysPreference,TeacherAttendanceDaysPreference,PairingSizePreference,TimeOfDayPreference,MaxConcurrentSeats,ContinueBeyondNominalTimeIfIncomplete)
            VALUES(1,@maxStudents,@teacherCountPerDay,@teacherLoadBalance,@studentAttendanceDays,@teacherAttendanceDays,@pairingSize,@timeOfDay,@maxSeats,@continueBeyond)
            ON CONFLICT(ProjectId) DO UPDATE SET
                MaxStudentsPerTeacher=excluded.MaxStudentsPerTeacher,
                TeacherCountPerDayPreference=excluded.TeacherCountPerDayPreference,
                TeacherLoadBalancePreference=excluded.TeacherLoadBalancePreference,
                StudentAttendanceDaysPreference=excluded.StudentAttendanceDaysPreference,
                TeacherAttendanceDaysPreference=excluded.TeacherAttendanceDaysPreference,
                PairingSizePreference=excluded.PairingSizePreference,
                TimeOfDayPreference=excluded.TimeOfDayPreference,
                MaxConcurrentSeats=excluded.MaxConcurrentSeats,
                ContinueBeyondNominalTimeIfIncomplete=excluded.ContinueBeyondNominalTimeIfIncomplete;
            """;
        command.Parameters.AddWithValue("@maxStudents", policy.MaxStudentsPerTeacher);
        command.Parameters.AddWithValue("@teacherCountPerDay", (int)policy.TeacherCountPerDayPreference);
        command.Parameters.AddWithValue("@teacherLoadBalance", (int)policy.TeacherLoadBalancePreference);
        command.Parameters.AddWithValue("@studentAttendanceDays", (int)policy.StudentAttendanceDaysPreference);
        command.Parameters.AddWithValue("@teacherAttendanceDays", (int)policy.TeacherAttendanceDaysPreference);
        command.Parameters.AddWithValue("@pairingSize", (int)policy.PairingSizePreference);
        command.Parameters.AddWithValue("@timeOfDay", (int)policy.TimeOfDayPreference);
        command.Parameters.AddWithValue("@maxSeats", policy.MaxConcurrentSeats);
        command.Parameters.AddWithValue("@continueBeyond", policy.ContinueBeyondNominalTimeIfIncomplete);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<SqliteConnection> OpenAsync(string path, CancellationToken token)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true, Pooling = false }.ToString());
        await connection.OpenAsync(token);
        return connection;
    }
}
