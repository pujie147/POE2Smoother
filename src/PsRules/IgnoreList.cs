namespace PsRules;

public sealed class IgnoreList
{
    private readonly List<string> _prefixes = [];

    public IReadOnlyList<string> Prefixes => _prefixes;

    public static IgnoreList Default()
    {
        var list = new IgnoreList();
        foreach (var line in DefaultEntries())
            list.Add(line);
        return list;
    }

    public static IEnumerable<string> DefaultEntries()
    {
        // Default policy: protect monsters, bosses and dangerous mechanics.
        // Player skills / MTX / league visuals remain selectable.
        yield return "metadata/monsters";
        yield return "metadata/effects/spells/monsters_effects/monster_mods";
        yield return "metadata/effects/spells/monsters_effects/league_azmeri/resources/wisp_doodads/wisp_sacred_big.ao";
        yield return "metadata/effects/spells/monsters_effects/league_azmeri/resources/wisp_doodads/fx/wisp_primal/idle_01/fwdline_big_sacred.pet";
        yield return "metadata/terrain/leagues/affliction/objects/afflictionator.ot";
        yield return "metadata/effects/environment/league_deepwater/water/water.ao";
        yield return "metadata/terrain/worldmaps/maps/doodads/maps/castaway01.ao";
        yield return "metadata/terrain/worldmaps/maps/doodads/maps/untaintedparadise01.ao";
    }

    public static IgnoreList Load(string path)
    {
        var list = new IgnoreList();
        foreach (var raw in File.ReadLines(path))
            list.Add(raw);
        return list;
    }

    public static IgnoreList FromLines(IEnumerable<string> lines)
    {
        var list = new IgnoreList();
        foreach (var line in lines)
            list.Add(line);
        return list;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllLines(path, Prefixes);
    }

    public void Add(string raw)
    {
        var line = Normalize(raw);
        if (line.Length == 0)
            return;
        if (!_prefixes.Contains(line, StringComparer.OrdinalIgnoreCase))
            _prefixes.Add(line);
    }

    public void AddRange(IEnumerable<string> lines)
    {
        foreach (var line in lines)
            Add(line);
    }

    public bool Remove(string raw)
    {
        var line = Normalize(raw);
        return _prefixes.RemoveAll(p => p.Equals(line, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    public bool IsIgnored(string path)
    {
        var normalized = Normalize(path);
        if (normalized.Length == 0)
            return false;

        foreach (var prefix in _prefixes)
        {
            if (normalized.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
            if (normalized.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static string Normalize(string raw)
    {
        var line = raw.Trim();
        if (line.StartsWith('#'))
            return "";
        var hash = line.IndexOf(" #", StringComparison.Ordinal);
        if (hash >= 0)
            line = line[..hash].Trim();
        line = line.Replace('\\', '/').TrimStart('/');
        line = line.TrimEnd('/');
        return line;
    }
}