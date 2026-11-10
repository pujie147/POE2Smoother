using System.Text.Json;
using PsCore;
using PsRules;

namespace PsCli.Commands;

internal static class ProfileCommand
{
    public static int Run(ParsedArgs args)
    {
        var paths = CommandUtil.Paths();
        var store = new ProfileStore(paths);
        var action = (args.Get("action") ?? "list").ToLowerInvariant();

        switch (action)
        {
            case "list":
            {
                var names = store.List();
                if (args.HasFlag("json"))
                {
                    Console.WriteLine(JsonSerializer.Serialize(names, new JsonSerializerOptions { WriteIndented = true }));
                    return 0;
                }
                foreach (var name in names)
                    Console.WriteLine(name);
                return 0;
            }
            case "show":
            {
                var name = args.Require("name");
                var profile = store.Load(name);
                if (profile == null)
                {
                    Console.Error.WriteLine($"error: profile not found: {name}");
                    return 2;
                }
                Console.WriteLine(JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            case "save":
            {
                var name = args.Require("name");
                var ignore = string.IsNullOrWhiteSpace(args.Get("ignore"))
                    ? CommandUtil.LoadIgnore(args, paths)
                    : IgnoreList.Load(args.Get("ignore")!);

                var categories = (args.Get("categories") ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();

                store.Save(new Profile
                {
                    Name = name,
                    Preset = args.Get("preset") ?? "safe",
                    Categories = categories,
                    Ignore = ignore.Prefixes.ToList(),
                    IncludeExperimental = args.HasFlag("include-experimental")
                });
                Console.WriteLine($"saved profile: {name}");
                return 0;
            }
            case "delete":
            {
                var name = args.Require("name");
                return store.Delete(name) ? 0 : 2;
            }
            default:
                Console.Error.WriteLine($"error: unknown profile action: {action}");
                return 2;
        }
    }
}