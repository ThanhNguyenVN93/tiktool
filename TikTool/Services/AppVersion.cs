using System.Reflection;

namespace TikTool.Services;

/// <summary>A semantic version with pre-release ordering (1.0.0-beta.2 is older than 1.0.0), enough for this project's release tags.</summary>
internal readonly struct SemVer : IComparable<SemVer>, IEquatable<SemVer>
{
    private readonly string[]? _pre;

    public SemVer(int major, int minor, int patch, string[]? pre = null)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        _pre = pre is { Length: > 0 } ? pre : null;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public IReadOnlyList<string> Pre => _pre ?? Array.Empty<string>();

    public bool IsPrerelease => _pre is not null;

    /// <summary>Accepts "1.2.3", "v1.2.3", "1.2.3-beta.4" and ignores build metadata ("+abc123").</summary>
    public static bool TryParse(string? text, out SemVer version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
        {
            s = s[1..];
        }

        var plus = s.IndexOf('+');
        if (plus >= 0)
        {
            s = s[..plus];
        }

        string[]? pre = null;
        var dash = s.IndexOf('-');
        if (dash >= 0)
        {
            pre = s[(dash + 1)..].Split('.');
            if (pre.Any(string.IsNullOrEmpty))
            {
                return false;
            }

            s = s[..dash];
        }

        var parts = s.Split('.');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor) || !int.TryParse(parts[2], out var patch)
            || major < 0 || minor < 0 || patch < 0)
        {
            return false;
        }

        version = new SemVer(major, minor, patch, pre);
        return true;
    }

    public int CompareTo(SemVer other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;

        var a = Pre;
        var b = other.Pre;
        if (a.Count == 0 && b.Count == 0) return 0;
        if (a.Count == 0) return 1;   // a release is newer than any of its pre-releases
        if (b.Count == 0) return -1;

        for (var i = 0; i < Math.Min(a.Count, b.Count); i++)
        {
            var an = int.TryParse(a[i], out var ai);
            var bn = int.TryParse(b[i], out var bi);
            c = an && bn ? ai.CompareTo(bi) : an ? -1 : bn ? 1 : string.CompareOrdinal(a[i], b[i]);
            if (c != 0) return c;
        }

        return a.Count.CompareTo(b.Count);
    }

    public bool Equals(SemVer other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemVer v && Equals(v);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', Pre));

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (IsPrerelease ? "-" + string.Join('.', Pre) : "");

    public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;

    public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;

    public static bool operator >=(SemVer a, SemVer b) => a.CompareTo(b) >= 0;

    public static bool operator <=(SemVer a, SemVer b) => a.CompareTo(b) <= 0;
}

/// <summary>The version of the running app (from the build). TIKTOOL_VERSION_OVERRIDE exists only so the update path can be tested.</summary>
internal static class AppVersion
{
    public static SemVer Current { get; } = Load();

    public static string Text => Current.ToString();

    private static SemVer Load()
    {
        if (SemVer.TryParse(Environment.GetEnvironmentVariable("TIKTOOL_VERSION_OVERRIDE"), out var overridden))
        {
            return overridden;
        }

        var info = typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (SemVer.TryParse(info, out var parsed))
        {
            return parsed;
        }

        var v = typeof(AppVersion).Assembly.GetName().Version;
        return new SemVer(v?.Major ?? 1, v?.Minor ?? 0, Math.Max(0, v?.Build ?? 0));
    }
}