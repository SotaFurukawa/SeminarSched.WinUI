using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.MasterData;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Tests;

public sealed class MasterDataWorkbookServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SeminarSched.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExportPreviewApply_RoundTripsFiveSheetMasterDataTransactionally()
    {
        var project = await CreateProjectAsync();
        var workbookPath = Path.Combine(_directory, "共通基本情報.xlsx");
        var service = new MasterDataWorkbookService();
        await service.ExportAsync(project, workbookPath);

        using (var workbook = new XLWorkbook(workbookPath))
        {
            Assert.Equal(new[] { "生徒", "講師", "科目", "講師対応科目", "受講希望" }, workbook.Worksheets.Select(sheet => sheet.Name));
            SetRow(workbook.Worksheet("生徒"), 3, new Dictionary<string, object?> { ["例示行"]="いいえ",["生徒ID"]="S-001",["氏名"]="架空 生徒",["学年"]="J2",["標準最大連続コマ数"]=2,["空きコマ許可"]="いいえ",["有効"]="はい" });
            SetRow(workbook.Worksheet("講師"), 3, new Dictionary<string, object?> { ["例示行"]="いいえ",["講師ID"]="T-001",["氏名"]="架空 講師",["空きコマ許可"]="いいえ",["有効"]="はい" });
            SetRow(workbook.Worksheet("科目"), 3, new Dictionary<string, object?> { ["例示行"]="いいえ",["科目コード"]="JH_MATH",["表示名"]="数学",["略称"]="数",["学校段階"]="中学校",["並び順"]=1,["有効"]="はい" });
            SetRow(workbook.Worksheet("講師対応科目"), 3, new Dictionary<string, object?> { ["例示行"]="いいえ",["講師ID"]="T-001",["科目コード"]="JH_MATH",["指導可能"]="はい" });
            SetRow(workbook.Worksheet("受講希望"), 3, new Dictionary<string, object?> { ["例示行"]="いいえ",["生徒ID"]="S-001",["科目コード"]="JH_MATH",["必要授業回数"]=4,["通常担当講師ID"]="T-001",["担当講師優先度"]=5,["第1希望講師ID"]="T-001",["1対1必須"]="いいえ" });
            workbook.Save();
        }

        var preview = await service.PreviewAsync(project, workbookPath);
        Assert.False(preview.HasErrors, string.Join(Environment.NewLine, preview.Issues.Select(issue => issue.Message)));
        Assert.All(preview.NewCounts, item => Assert.Equal(1, item.Value));
        var result = await service.ApplyAsync(project, preview);
        Assert.Equal(5, result.ImportedRows);

        await using var connection = new SqliteConnection($"Data Source={project};Pooling=False");
        await connection.OpenAsync();
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM Student WHERE ExternalId='S-001' AND DefaultMaxConsecutiveSlots=2"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM TeacherQualification q JOIN Teacher t ON t.Id=q.TeacherId JOIN Subject s ON s.Id=q.SubjectId WHERE t.ExternalId='T-001' AND s.Code='JH_MATH' AND q.CanTeach=1"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM LessonRequest r JOIN Teacher t ON t.Id=r.RegularTeacherId WHERE r.RequiredSessions=4 AND r.RegularTeacherPriority=5 AND t.ExternalId='T-001'"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM ImportSourceSnapshot WHERE ImportType='master_workbook' AND length(Content)>1000"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM AuditLog WHERE Action='master_workbook_import'"));
    }

    [Fact]
    public async Task Preview_InvalidReference_BlocksEveryDatabaseChange()
    {
        var project = await CreateProjectAsync();var workbookPath=Path.Combine(_directory,"不正.xlsx");var service=new MasterDataWorkbookService();await service.ExportAsync(project,workbookPath);
        using(var workbook=new XLWorkbook(workbookPath)){SetRow(workbook.Worksheet("受講希望"),3,new Dictionary<string,object?>{{"例示行","いいえ"},{"生徒ID","S-UNKNOWN"},{"科目コード","SUBJECT-UNKNOWN"},{"必要授業回数",1},{"担当講師優先度",3},{"1対1必須","いいえ"}});workbook.Save();}
        var preview=await service.PreviewAsync(project,workbookPath);Assert.True(preview.HasErrors);await Assert.ThrowsAsync<InvalidDataException>(()=>service.ApplyAsync(project,preview));
        await using var connection=new SqliteConnection($"Data Source={project};Pooling=False");await connection.OpenAsync();Assert.Equal(0L,await ScalarAsync(connection,"SELECT COUNT(*) FROM LessonRequest"));Assert.Equal(0L,await ScalarAsync(connection,"SELECT COUNT(*) FROM ImportBatch"));
    }

    [Fact]
    public async Task Apply_FileChangedAfterPreview_IsRejected()
    {
        var project=await CreateProjectAsync();var workbookPath=Path.Combine(_directory,"変更検知.xlsx");var service=new MasterDataWorkbookService();await service.ExportAsync(project,workbookPath);var preview=await service.PreviewAsync(project,workbookPath);
        using(var workbook=new XLWorkbook(workbookPath)){workbook.Worksheet("生徒").Cell(3,2).Value="S-CHANGED";workbook.Save();}
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.ApplyAsync(project,preview));
    }

    [Fact]
    public async Task Apply_DatabaseFailure_RollsBackAllEarlierSheets()
    {
        var project=await CreateProjectAsync();var workbookPath=Path.Combine(_directory,"ロールバック.xlsx");var service=new MasterDataWorkbookService();await service.ExportAsync(project,workbookPath);
        using(var workbook=new XLWorkbook(workbookPath)){SetRow(workbook.Worksheet("生徒"),3,new Dictionary<string,object?>{{"例示行","いいえ"},{"生徒ID","S-ROLLBACK"},{"氏名","架空 生徒"},{"学年","J1"},{"有効","はい"}});SetRow(workbook.Worksheet("講師"),3,new Dictionary<string,object?>{{"例示行","いいえ"},{"講師ID","T-ROLLBACK"},{"氏名","架空 講師"},{"有効","はい"}});workbook.Save();}
        var preview=await service.PreviewAsync(project,workbookPath);Assert.False(preview.HasErrors);
        await using(var setup=new SqliteConnection($"Data Source={project};Pooling=False")){await setup.OpenAsync();await using var trigger=setup.CreateCommand();trigger.CommandText="CREATE TRIGGER fail_teacher BEFORE INSERT ON Teacher BEGIN SELECT RAISE(ABORT,'test failure'); END;";await trigger.ExecuteNonQueryAsync();}
        await Assert.ThrowsAsync<SqliteException>(()=>service.ApplyAsync(project,preview));
        await using var connection=new SqliteConnection($"Data Source={project};Pooling=False");await connection.OpenAsync();Assert.Equal(0L,await ScalarAsync(connection,"SELECT COUNT(*) FROM Student"));Assert.Equal(0L,await ScalarAsync(connection,"SELECT COUNT(*) FROM ImportBatch"));
    }

    private async Task<string> CreateProjectAsync(){Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,$"{Guid.NewGuid():N}.jukuschedule");await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,8,20)));return path;}
    private static async Task<long> ScalarAsync(SqliteConnection connection,string sql){await using var command=connection.CreateCommand();command.CommandText=sql;return Convert.ToInt64(await command.ExecuteScalarAsync());}
    private static void SetRow(IXLWorksheet sheet,int row,IReadOnlyDictionary<string,object?> values){foreach(var cell in sheet.Row(1).CellsUsed()){var header=cell.GetString().Replace("（必須）",string.Empty,StringComparison.Ordinal);if(values.TryGetValue(header,out var value))cell.Worksheet.Cell(row,cell.Address.ColumnNumber).Value=value switch{null=>Blank.Value,string text=>text,int number=>number,bool boolean=>boolean,_=>value.ToString()??string.Empty};}}
    public void Dispose(){if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
}
