using System.Reflection;

namespace SeminarSched.Domain;

public static class DomainAssembly
{
    public static Assembly Reference { get; } = typeof(DomainAssembly).Assembly;
}
