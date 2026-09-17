namespace SeminarSched.Application.Output;

public sealed record OutputPackageResult(string DirectoryPath,string ExcelPath,string PdfPath,int AssignmentCount,int UnassignedCount,string TeacherPacketDirectory);
public interface IOutputPackageService
{
    Task<OutputPackageResult> GenerateAsync(string projectPath,string parentDirectory,CancellationToken cancellationToken=default);
}
