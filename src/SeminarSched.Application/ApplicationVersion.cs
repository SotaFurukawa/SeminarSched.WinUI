using System.Reflection;

namespace SeminarSched.Application;

public sealed record ApplicationVersion(int Major, int Minor, int Patch, string? Prerelease)
{
    public string SemanticVersion => Prerelease is null
        ? $"{Major}.{Minor}.{Patch}"
        : $"{Major}.{Minor}.{Patch}-{Prerelease}";

    public string DisplayVersion => Prerelease is null
        ? $"v{Major}.{Minor}.{Patch}"
        : $"v{Major}.{Minor}.{Patch} ({Prerelease})";

    /// <summary>Prerelease（常に"beta"）は順序付けに使わず、Major.Minor.Patchのみで比較する。</summary>
    public bool IsNewerThan(ApplicationVersion other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (Major != other.Major)
        {
            return Major > other.Major;
        }

        if (Minor != other.Minor)
        {
            return Minor > other.Minor;
        }

        return Patch > other.Patch;
    }

    public static ApplicationVersion FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        var assemblyVersion = assembly.GetName().Version;
        return Parse(informationalVersion ?? assemblyVersion?.ToString(3)
            ?? throw new InvalidOperationException("The application assembly does not contain version metadata."));
    }

    public static ApplicationVersion Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var withoutMetadata = value.Split('+', 2)[0];
        var segments = withoutMetadata.Split('-', 2);
        var numbers = segments[0].Split('.');
        if (numbers.Length < 3 ||
            !int.TryParse(numbers[0], out var major) ||
            !int.TryParse(numbers[1], out var minor) ||
            !int.TryParse(numbers[2], out var patch))
        {
            throw new FormatException($"Invalid semantic version: {value}");
        }

        var prerelease = segments.Length == 2 && segments[1].Length > 0 ? segments[1] : null;
        return new ApplicationVersion(major, minor, patch, prerelease);
    }
}
