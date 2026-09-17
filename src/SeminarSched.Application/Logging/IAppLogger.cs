namespace SeminarSched.Application.Logging;

/// <summary>
/// Operational logging only. Callers must never pass student/teacher names, addresses, or other
/// personal information here (see PRIVACY.md) — counts and action labels are fine.
/// </summary>
public interface IAppLogger
{
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
}
