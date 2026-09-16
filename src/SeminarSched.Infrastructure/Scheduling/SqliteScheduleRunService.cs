using Microsoft.Data.Sqlite;
using SeminarSched.Application.Scheduling;
using SeminarSched.Infrastructure.Projects;
using SeminarSched.Optimization.Core;

namespace SeminarSched.Infrastructure.Scheduling;

public sealed class SqliteScheduleRunService : IScheduleRunService
{
    public async Task<ScheduleRunSummary> RunAsync(string projectPath,TimeSpan maximumDuration,CancellationToken cancellationToken=default)
    {
        await using var connection=await Open(projectPath,cancellationToken);await EnsureSchema(connection,cancellationToken);var problem=await BuildProblem(connection,cancellationToken);
        var solution=await new CpSatScheduleSolver().SolveAsync(problem,maximumDuration,cancellationToken:cancellationToken);ScheduleSolutionValidator.Validate(problem,solution);
        await SaveValidated(connection,problem,solution,cancellationToken);return new(solution.Placements.Count,solution.UnassignedLessons,solution.Elapsed);
    }
    private static async Task<ScheduleProblem> BuildProblem(SqliteConnection c,CancellationToken t)
    {
        var demands=new List<LessonDemand>();await using(var q=c.CreateCommand()){q.CommandText="SELECT r.Id,r.StudentId,r.RequiredSessions,(SELECT COUNT(*) FROM Assignment a WHERE a.LessonRequestId=r.Id AND a.IsLocked=1) FROM LessonRequest r;";await using var d=await q.ExecuteReaderAsync(t);while(await d.ReadAsync(t))demands.Add(new(d.GetInt64(0),d.GetInt64(1),d.GetInt32(2),d.GetInt32(3)));}
        var candidates=new List<PlacementCandidate>();await using(var q=c.CreateCommand()){q.CommandText="""
            SELECT r.Id,r.StudentId,tq.TeacherId,ds.OpenDateId,ds.TimeSlotId
            FROM LessonRequest r JOIN TeacherQualification tq ON tq.SubjectId=r.SubjectId AND tq.CanTeach=1
            JOIN Teacher t ON t.Id=tq.TeacherId AND t.Active=1 CROSS JOIN OpenDateTimeSlot ds
            JOIN OpenDate d ON d.Id=ds.OpenDateId AND d.IsOpen=1 JOIN TimeSlot ts ON ts.Id=ds.TimeSlotId AND ts.Active=1
            WHERE NOT EXISTS(SELECT 1 FROM TeacherUnavailability u WHERE u.TeacherId=tq.TeacherId AND u.OpenDateId=ds.OpenDateId AND u.TimeSlotId=ds.TimeSlotId)
              AND NOT EXISTS(SELECT 1 FROM Assignment a WHERE a.IsLocked=1 AND a.OpenDateId=ds.OpenDateId AND a.TimeSlotId=ds.TimeSlotId AND a.TeacherId=tq.TeacherId)
              AND NOT EXISTS(SELECT 1 FROM Assignment a JOIN LessonRequest ar ON ar.Id=a.LessonRequestId WHERE a.IsLocked=1 AND a.OpenDateId=ds.OpenDateId AND a.TimeSlotId=ds.TimeSlotId AND ar.StudentId=r.StudentId);
            """;await using var d=await q.ExecuteReaderAsync(t);while(await d.ReadAsync(t))candidates.Add(new(d.GetInt64(0),d.GetInt64(1),d.GetInt64(2),d.GetInt64(3),d.GetInt64(4)));}
        return new(demands,candidates);
    }
    private static async Task SaveValidated(SqliteConnection c,ScheduleProblem problem,ScheduleSolution solution,CancellationToken t)
    {
        await using var tx=await c.BeginTransactionAsync(t);await using(var clear=c.CreateCommand()){clear.Transaction=(SqliteTransaction)tx;clear.CommandText="DELETE FROM Assignment WHERE IsLocked=0;";await clear.ExecuteNonQueryAsync(t);}
        foreach(var p in solution.Placements){await using var add=c.CreateCommand();add.Transaction=(SqliteTransaction)tx;add.CommandText="INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source) VALUES(@r,@te,@d,@s,0,'cp-sat');";add.Parameters.AddWithValue("@r",p.RequestId);add.Parameters.AddWithValue("@te",p.TeacherId);add.Parameters.AddWithValue("@d",p.OpenDateId);add.Parameters.AddWithValue("@s",p.TimeSlotId);await add.ExecuteNonQueryAsync(t);}
        await using var validate=c.CreateCommand();validate.Transaction=(SqliteTransaction)tx;validate.CommandText="""
            SELECT
              (SELECT COUNT(*) FROM (SELECT r.StudentId,a.OpenDateId,a.TimeSlotId FROM Assignment a JOIN LessonRequest r ON r.Id=a.LessonRequestId GROUP BY r.StudentId,a.OpenDateId,a.TimeSlotId HAVING COUNT(*)>1))
            + (SELECT COUNT(*) FROM (SELECT TeacherId,OpenDateId,TimeSlotId FROM Assignment GROUP BY TeacherId,OpenDateId,TimeSlotId HAVING COUNT(*)>1))
            + (SELECT COUNT(*) FROM Assignment a JOIN LessonRequest r ON r.Id=a.LessonRequestId
               WHERE NOT EXISTS(SELECT 1 FROM OpenDateTimeSlot ds JOIN OpenDate d ON d.Id=ds.OpenDateId WHERE ds.OpenDateId=a.OpenDateId AND ds.TimeSlotId=a.TimeSlotId AND d.IsOpen=1)
                  OR NOT EXISTS(SELECT 1 FROM TeacherQualification tq WHERE tq.TeacherId=a.TeacherId AND tq.SubjectId=r.SubjectId AND tq.CanTeach=1)
                  OR EXISTS(SELECT 1 FROM TeacherUnavailability u WHERE u.TeacherId=a.TeacherId AND u.OpenDateId=a.OpenDateId AND u.TimeSlotId=a.TimeSlotId))
            + (SELECT COUNT(*) FROM (SELECT r.Id FROM LessonRequest r LEFT JOIN Assignment a ON a.LessonRequestId=r.Id GROUP BY r.Id,r.RequiredSessions HAVING COUNT(a.Id)>r.RequiredSessions));
            """;var invalid=Convert.ToInt64(await validate.ExecuteScalarAsync(t));if(invalid>0)throw new InvalidDataException("保存前validatorが制約違反を検出しました。");await tx.CommitAsync(t);
    }
    private static async Task<SqliteConnection> Open(string p,CancellationToken t){var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.GetFullPath(p),Mode=SqliteOpenMode.ReadWrite,ForeignKeys=true,Pooling=false}.ToString());await c.OpenAsync(t);return c;}
    private static Task EnsureSchema(SqliteConnection connection, CancellationToken cancellationToken) =>
        SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken);
}
