using Microsoft.Data.Sqlite;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.MasterData;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Tests;

// ユーザー指示「通常授業について、複数の行で同じ氏名、科目があった場合、重複を許してください」への
// 対応。既存のプロジェクトファイル（旧UNIQUE(ProjectId,StudentId,SubjectId)のまま）を開いたときに、
// SqliteProjectSchema.RelaxRegularLessonProfileUniqueConstraintAsyncが自己修復として新しい制約
// （通常担当講師も含む）へテーブルを作り直し、既存データを失わずに移行できることを確認する。
public sealed class SqliteProjectSchemaTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SeminarSched.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task EnsureCurrentAsync_MigratesLegacyRegularLessonProfileUniqueConstraint_PreservingExistingRowsAndAllowingNewDuplicates()
    {
        var path = Path.Combine(_directory, "legacy.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(
            2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 22)));

        var repository = new SqliteMasterDataRepository();
        var student = await repository.SaveStudentAsync(path, new Student(0, "S-001", "架空", "生徒", "中2"));
        var subject = await repository.SaveSubjectAsync(path, new Subject(0, "JH_ENGLISH", "英語", "英", "中学", 1));
        var teacherA = await repository.SaveTeacherAsync(path, new Teacher(0, "T-001", "架空", "講師A"));
        var teacherB = await repository.SaveTeacherAsync(path, new Teacher(0, "T-002", "架空", "講師B"));

        // 新規作成直後のDBは既に新しい制約を持つため、テーブルを旧制約の形へ一度作り直し、
        // 「以前から使われていた、まだ移行していないプロジェクトファイル」の状態を再現する。
        await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var rebuild = connection.CreateCommand();
            rebuild.CommandText = """
                ALTER TABLE RegularLessonProfile RENAME TO RegularLessonProfile_legacy;
                CREATE TABLE RegularLessonProfile (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProjectId INTEGER NOT NULL REFERENCES CourseProject(Id) ON DELETE CASCADE,
                    StudentId INTEGER NOT NULL REFERENCES Student(Id) ON DELETE CASCADE,
                    SubjectId INTEGER NOT NULL REFERENCES Subject(Id) ON DELETE CASCADE,
                    RegularTeacherId INTEGER REFERENCES Teacher(Id) ON DELETE SET NULL,
                    RegularTeacherPriority INTEGER NOT NULL DEFAULT 3 CHECK (RegularTeacherPriority BETWEEN 1 AND 5),
                    OneToOneRequired INTEGER NOT NULL DEFAULT 0 CHECK (OneToOneRequired IN (0, 1)),
                    Note TEXT NOT NULL DEFAULT '',
                    UNIQUE (ProjectId, StudentId, SubjectId)
                );
                DROP TABLE RegularLessonProfile_legacy;
                """;
            await rebuild.ExecuteNonQueryAsync();
        }
        await repository.SaveRegularLessonAsync(path, new RegularLessonProfile(0, student.Id, subject.Id, teacherA.Id, 5, false, "既存データ"));

        // 旧制約の下では、同じ生徒・科目で別の通常担当講師を登録しようとすると、1行へ上書きされて
        // しまっていた（今回の不具合そのもの）。EnsureSchemaAsync（内部でEnsureCurrentAsyncを呼ぶ）が
        // 動く2回目以降の呼び出し（SaveTeacherAsync等、何でもよい）で自己修復が走るはずだが、ここでは
        // 明示的にもう一度保存操作を行うことで、移行後に新規の2行目が正しく追加できることを確認する。
        await repository.SaveRegularLessonAsync(path, new RegularLessonProfile(0, student.Id, subject.Id, teacherB.Id, 3, false));

        var rows = await repository.GetRegularLessonsAsync(path);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.RegularTeacherId == teacherA.Id && r.Note == "既存データ");
        Assert.Contains(rows, r => r.RegularTeacherId == teacherB.Id);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
