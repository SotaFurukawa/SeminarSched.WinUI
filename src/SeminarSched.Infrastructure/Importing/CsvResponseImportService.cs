using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.VisualBasic.FileIO;
using SeminarSched.Application.Importing;

namespace SeminarSched.Infrastructure.Importing;

public sealed class CsvResponseImportService : IResponseImportService
{
    public Task<ResponseImportPreview> PreviewAsync(string projectPath, string studentFilePath, string teacherFilePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => Preview(projectPath, studentFilePath, teacherFilePath), cancellationToken);

    public async Task ApplyAsync(string projectPath, ResponseImportPreview preview, CancellationToken cancellationToken = default)
    {
        if (!preview.CanApply) throw new InvalidOperationException("エラーがあるため反映できません。");
        var current = Preview(projectPath, preview.StudentFilePath, preview.TeacherFilePath);
        if (!current.CanApply || current.StudentFileSha256 != preview.StudentFileSha256 || current.TeacherFileSha256 != preview.TeacherFileSha256)
            throw new InvalidOperationException("検証後にファイルが変更されました。再検証してください。");
        var students = Read(preview.StudentFilePath); var teachers = Read(preview.TeacherFilePath);
        await using var connection = await OpenAsync(projectPath, cancellationToken); await EnsureSchemaAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var row in students.Rows)
        {
            await using var command = connection.CreateCommand(); command.Transaction=(SqliteTransaction)transaction;
            command.CommandText="INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) SELECT 1,s.Id,sub.Id,@count FROM Student s,Subject sub WHERE s.ExternalId=@student AND sub.Code=@subject ON CONFLICT(ProjectId,StudentId,SubjectId) DO UPDATE SET RequiredSessions=excluded.RequiredSessions;";
            command.Parameters.AddWithValue("@student",row[0]); command.Parameters.AddWithValue("@subject",row[1]); command.Parameters.AddWithValue("@count",int.Parse(row[2])); await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var row in teachers.Rows)
        {
            await using var clear=connection.CreateCommand(); clear.Transaction=(SqliteTransaction)transaction; clear.CommandText="DELETE FROM TeacherUnavailability WHERE TeacherId=(SELECT Id FROM Teacher WHERE ExternalId=@id);"; clear.Parameters.AddWithValue("@id",row[0]); await clear.ExecuteNonQueryAsync(cancellationToken);
            foreach(var token in SplitAvailability(row.ElementAtOrDefault(1)??"")){var parts=token.Split('|'); await using var add=connection.CreateCommand();add.Transaction=(SqliteTransaction)transaction;add.CommandText="INSERT INTO TeacherUnavailability(TeacherId,OpenDateId,TimeSlotId) SELECT t.Id,d.Id,s.Id FROM Teacher t,OpenDate d,TimeSlot s WHERE t.ExternalId=@teacher AND d.Date=@date AND s.Code=@slot;";add.Parameters.AddWithValue("@teacher",row[0]);add.Parameters.AddWithValue("@date",parts[0]);add.Parameters.AddWithValue("@slot",parts[1]);await add.ExecuteNonQueryAsync(cancellationToken);}
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private static ResponseImportPreview Preview(string projectPath,string studentPath,string teacherPath)
    {
        var issues=new List<ImportIssue>(); CsvData students; CsvData teachers;
        try { students=Read(studentPath); } catch(Exception e) when(e is IOException or MalformedLineException){issues.Add(new(0,"生徒回答",e.Message));students=new([],[]);}
        try { teachers=Read(teacherPath); } catch(Exception e) when(e is IOException or MalformedLineException){issues.Add(new(0,"講師回答",e.Message));teachers=new([],[]);}
        RequireHeaders(students,["生徒ID","科目コード","必要回数"],issues,"生徒回答"); RequireHeaders(teachers,["講師ID","勤務不可"],issues,"講師回答");
        using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.GetFullPath(projectPath),Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()); connection.Open();
        for(var i=0;i<students.Rows.Count;i++){var r=students.Rows[i]; if(r.Length<3){issues.Add(new(i+2,"行","列が不足しています"));continue;} if(!Exists(connection,"Student","ExternalId",r[0]))issues.Add(new(i+2,"生徒ID","登録されていません"));if(!Exists(connection,"Subject","Code",r[1]))issues.Add(new(i+2,"科目コード","登録されていません"));if(!int.TryParse(r[2],out var n)||n<1)issues.Add(new(i+2,"必要回数","1以上の整数を指定してください"));}
        for(var i=0;i<teachers.Rows.Count;i++){var r=teachers.Rows[i];if(r.Length<1||!Exists(connection,"Teacher","ExternalId",r[0]))issues.Add(new(i+2,"講師ID","登録されていません"));foreach(var token in SplitAvailability(r.ElementAtOrDefault(1)??"")){var p=token.Split('|');if(p.Length!=2||!ExistsPair(connection,p))issues.Add(new(i+2,"勤務不可",$"日付・コマが不正です: {token}"));}}
        return new(studentPath,teacherPath,Hash(studentPath),Hash(teacherPath),students.Rows.Count,teachers.Rows.Count,issues);
    }

    private static bool Exists(SqliteConnection c,string table,string column,string value){using var cmd=c.CreateCommand();cmd.CommandText=$"SELECT EXISTS(SELECT 1 FROM {table} WHERE {column}=@v AND Active=1);";cmd.Parameters.AddWithValue("@v",value.Trim());return Convert.ToInt64(cmd.ExecuteScalar())==1;}
    private static bool ExistsPair(SqliteConnection c,string[] p){using var cmd=c.CreateCommand();cmd.CommandText="SELECT EXISTS(SELECT 1 FROM OpenDate d,TimeSlot s WHERE d.Date=@d AND d.IsOpen=1 AND s.Code=@s AND s.Active=1);";cmd.Parameters.AddWithValue("@d",p[0]);cmd.Parameters.AddWithValue("@s",p[1]);return Convert.ToInt64(cmd.ExecuteScalar())==1;}
    private static IEnumerable<string> SplitAvailability(string value)=>value.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
    private static void RequireHeaders(CsvData d,string[] expected,List<ImportIssue> issues,string file){if(!expected.All(x=>d.Headers.Contains(x)))issues.Add(new(1,file,$"必須列: {string.Join(", ",expected)}"));}
    private static CsvData Read(string path){using var parser=new TextFieldParser(path,Encoding.UTF8){TextFieldType=FieldType.Delimited,HasFieldsEnclosedInQuotes=true,TrimWhiteSpace=true};parser.SetDelimiters(",");var headers=parser.ReadFields()??[];var rows=new List<string[]>();while(!parser.EndOfData)rows.Add(parser.ReadFields()??[]);return new(headers,rows);}
    private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
    private static async Task<SqliteConnection> OpenAsync(string p,CancellationToken t){var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.GetFullPath(p),Mode=SqliteOpenMode.ReadWrite,ForeignKeys=true,Pooling=false}.ToString());await c.OpenAsync(t);return c;}
    private static async Task EnsureSchemaAsync(SqliteConnection c,CancellationToken t){await using var cmd=c.CreateCommand();cmd.CommandText="CREATE TABLE IF NOT EXISTS LessonRequest(Id INTEGER PRIMARY KEY AUTOINCREMENT,ProjectId INTEGER NOT NULL REFERENCES CourseProject(Id) ON DELETE CASCADE,StudentId INTEGER NOT NULL REFERENCES Student(Id) ON DELETE CASCADE,SubjectId INTEGER NOT NULL REFERENCES Subject(Id) ON DELETE CASCADE,RequiredSessions INTEGER NOT NULL CHECK(RequiredSessions>0),UNIQUE(ProjectId,StudentId,SubjectId));CREATE TABLE IF NOT EXISTS TeacherUnavailability(TeacherId INTEGER NOT NULL REFERENCES Teacher(Id) ON DELETE CASCADE,OpenDateId INTEGER NOT NULL REFERENCES OpenDate(Id) ON DELETE CASCADE,TimeSlotId INTEGER NOT NULL REFERENCES TimeSlot(Id) ON DELETE CASCADE,PRIMARY KEY(TeacherId,OpenDateId,TimeSlotId));";await cmd.ExecuteNonQueryAsync(t);}
    private sealed record CsvData(string[] Headers,List<string[]> Rows);
}
