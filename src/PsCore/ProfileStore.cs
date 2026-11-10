using System.Text.Json;

namespace PsCore;

public sealed class Profile
{
    public string Name { get; set; } = "";
    public string Preset { get; set; } = "safe";
    public List<string> Categories { get; set; } = [];
    public List<string> Ignore { get; set; } = [];
    public bool IncludeExperimental { get; set; }
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