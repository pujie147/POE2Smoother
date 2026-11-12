using System.Text.Json;
using PsRules;

namespace PsCore;

public sealed class Profile
{
    public string Name { get; set; } = "";
    public string Preset { get; set; } = "safe";
    public List<string> Categories { get; set; } = [];
    public List<string> Ignore { get; set; } = [];

    /// <summary>Legacy mirror of <see cref="ExperimentalTier"/>; see <see cref="AppSettings.IncludeExperimental"/>.</summary>
    public bool IncludeExperimental { get; set; }

    /// <summary>"safe" | "fx" | "material", stored as a string for the same reason as in AppSettings.</summary>
    public string? ExperimentalTier { get; set; }

    /// <summary>A profile saved before tiers existed meant "all blanking strategies", so it migrates to material.</summary>
    public RiskTier EffectiveTier() =>
        RiskTiers.TryParse(ExperimentalTier, out var tier)
            ? tier
            : IncludeExperimental ? RiskTier.Material : RiskTier.Safe;

    public void SetTier(RiskTier tier)
    {
        ExperimentalTier = RiskTiers.ToKey(tier);
        IncludeExperimental = tier > RiskTier.Safe;
    }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;

    public ProfileStore(AppPaths paths)
    {
        _paths = paths;
    }

    public IReadOnlyList<string> List()
    {
        if (!Directory.Exists(_paths.ProfilesDir))
            return [];

        return Directory.EnumerateFiles(_paths.ProfilesDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Profile? Load(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(Profile profile)
    {
        profile.Name = SanitizeName(profile.Name);
        if (profile.Name.Length == 0)
            throw new ArgumentException("Profile name cannot be empty.");
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        _paths.Ensure();
        var path = PathFor(profile.Name);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(profile, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }

    public bool Delete(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
            return false;
        File.Delete(path);
        return true;
    }

    private string PathFor(string name)
        => Path.Combine(_paths.ProfilesDir, SanitizeName(name) + ".json");

    public static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars).Trim();
    }
}