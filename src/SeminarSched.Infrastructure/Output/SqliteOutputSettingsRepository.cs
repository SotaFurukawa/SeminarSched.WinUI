using System.Text.Json;
using Microsoft.Data.Sqlite;
using SeminarSched.Application.Output;
using SeminarSched.Domain.Output;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Output;

/// <summary>
/// 既存の(未使用だった)OutputSettingテーブルのうち、本アプリで実際に使う列
/// (PaperSize/Orientation/MarginMm/FileNamePattern/StyleRulesJson)だけを読み書きする。
/// 他の列（Python版のOutputSettingsに存在する表示項目ON/OFF・ロゴ・1ページの日数/講師列数等）は
/// 今回未実装のためスキーマの既定値のまま触れない。
/// </summary>
public sealed class SqliteOutputSettingsRepository : IOutputSettingsRepository
{
    private sealed record StyleColors(string Closed, string Unavailable, string Group);

    public async Task<OutputSettings> GetAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken); await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PaperSize,Orientation,MarginMm,FileNamePattern,StyleRulesJson FROM OutputSetting WHERE ProjectId=1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return OutputSettings.Default;

        var paperSize = reader.GetString(0);
        var orientation = reader.GetString(1);
        var marginMm = reader.GetDouble(2);
        var fileNamePattern = reader.GetString(3);
        var styleJson = reader.GetString(4);
        var colors = string.IsNullOrWhiteSpace(styleJson) || styleJson == "{}"
            ? null
            : JsonSerializer.Deserialize<StyleColors>(styleJson);

        return new OutputSettings(
            paperSize.Length > 0 ? paperSize : OutputSettings.Default.PaperSize,
            orientation.Length > 0 ? orientation : OutputSettings.Default.Orientation,
            marginMm,
            fileNamePattern.Length > 0 ? fileNamePattern : OutputSettings.Default.FileNamePattern,
            colors?.Closed ?? OutputSettings.Default.ClosedFillHex,
            colors?.Unavailable ?? OutputSettings.Default.UnavailableFillHex,
            colors?.Group ?? OutputSettings.Default.GroupFillHex);
    }

    public async Task SaveAsync(string projectPath, OutputSettings settings, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken); await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken);
        var styleJson = JsonSerializer.Serialize(new StyleColors(settings.ClosedFillHex, settings.UnavailableFillHex, settings.GroupFillHex));
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO OutputSetting(ProjectId,PaperSize,Orientation,MarginMm,FileNamePattern,StyleRulesJson) VALUES(1,@paper,@orientation,@margin,@pattern,@style)
            ON CONFLICT(ProjectId) DO UPDATE SET PaperSize=excluded.PaperSize,Orientation=excluded.Orientation,MarginMm=excluded.MarginMm,FileNamePattern=excluded.FileNamePattern,StyleRulesJson=excluded.StyleRulesJson;
            """;
        command.Parameters.AddWithValue("@paper", settings.PaperSize);
        command.Parameters.AddWithValue("@orientation", settings.Orientation);
        command.Parameters.AddWithValue("@margin", settings.MarginMm);
        command.Parameters.AddWithValue("@pattern", settings.FileNamePattern);
        command.Parameters.AddWithValue("@style", styleJson);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<SqliteConnection> OpenAsync(string path, CancellationToken token)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true, Pooling = false }.ToString());
        await connection.OpenAsync(token);
        return connection;
    }
}
