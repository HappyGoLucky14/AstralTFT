namespace AstralTFT.Core.Models;

public readonly record struct TftPatchVersion(int Major, int Minor, int Hotfix = 0) : IComparable<TftPatchVersion>
{
    public static bool TryParse(string? value, out TftPatchVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var numeric = new string(value.Trim().TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        var parts = numeric.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Length > 3) return false;
        if (!int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor)) return false;
        var hotfix = 0;
        if (parts.Length == 3 && !int.TryParse(parts[2], out hotfix)) return false;
        if (major < 0 || minor < 0 || hotfix < 0) return false;

        version = new TftPatchVersion(major, minor, hotfix);
        return true;
    }

    public int CompareTo(TftPatchVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Hotfix.CompareTo(other.Hotfix);
    }

    public override string ToString() => Hotfix > 0 ? $"{Major}.{Minor}.{Hotfix}" : $"{Major}.{Minor}";
}
