namespace SeminarSched.Application.Importing;

public interface IResponseImportService
{
    Task<ResponseImportPreview> PreviewAsync(string projectPath, string studentFilePath, string teacherFilePath, CancellationToken cancellationToken = default);
    Task ApplyAsync(string projectPath, ResponseImportPreview preview, bool removeUnlistedAvailability = false, CancellationToken cancellationToken = default);
}
