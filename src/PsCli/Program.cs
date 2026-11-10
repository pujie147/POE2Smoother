using PsCli;
using PsCli.Commands;

return Cli.Run(args);

internal static class Cli
{
    private const string Version = "1.0.0";

    private static readonly HashSet<string> FlagOptions = new(StringComparer.Ordinal)
    {
        "help", "yes", "keep", "force", "json", "clean-custom", "clean-only",
        "dry-run", "include-experimental", "text", "prune"
    };

    private static readonly HashSet<string> ValueOptions = new(StringComparer.Ordinal)
    {
        "index", "bundles", "filter", "limit", "out", "game-path", "backup-dir", "backup",
        "path", "content", "game", "pack", "preset", "ignore", "name", "build",
        "language", "profile", "categories", "action", "entry", "index-url"
    };

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        var command = args[0];
        ParsedArgs parsed;
        try
        {
            parsed = ParsedArgs.Parse(args.Skip(1).ToArray(), FlagOptions, ValueOptions);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }

        if (parsed.HasFlag("help"))
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return command switch
            {
                "inspect" => InspectCommand.Run(parsed),
                "peek" => PeekCommand.Run(parsed),
                "dump-paths" => DumpPathsCommand.Run(parsed),
                "read-file" => ReadFileCommand.Run(parsed),
                "detect" => DetectCommand.Run(parsed),
                "status" => StatusCommand.Run(parsed),
                "build-rules" => BuildRulesCommand.Run(parsed),
                "list-rules" => ListRulesCommand.Run(parsed),
                "plan" => PlanCommand.Run(parsed),
                "apply" => ApplyCommand.Run(parsed),
                "self-test" => SelfTestCommand.Run(parsed),
                "restore" => RestoreCommand.Run(parsed),
                "backups" => BackupsCommand.Run(parsed),
                "profiles" => ProfileCommand.Run(parsed),
                "fixture" => FixtureCommand.Run(parsed),
                "version" => PrintVersion(),
                _ => UnknownCommand(command)
            };
        }
        catch (DllNotFoundException ex)
        {
            Console.Error.WriteLine("error: native Oodle library not found.");
            Console.Error.WriteLine($"       {ex.Message}");
            Console.Error.WriteLine("       Linux: put liboo2core.so next to the pscli executable, or set LD_LIBRARY_PATH.");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.GetType().Name}: {ex.Message}");
            if (Environment.GetEnvironmentVariable("PSCLI_DEBUG") == "1")
                Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int PrintVersion()
    {
        Console.WriteLine($"pscli {Version}");
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"error: unknown command '{command}'");
        PrintHelp();
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine($"""
pscli {Version} - PoE2 SteamOS patcher

Usage:
  pscli detect      [--json]
  pscli inspect     --index <Bundles2/_.index.bin> [--filter <prefix>] [--limit <n>] [--json]
  pscli peek        --index <Bundles2/_.index.bin>
  pscli dump-paths  --index <Bundles2/_.index.bin> --out <file.txt> [--filter <prefix>]
  pscli read-file   --index <Bundles2/_.index.bin> --path <metadata/path> [--out <file>] [--text]
  pscli build-rules --index <Bundles2/_.index.bin> [--out <pack.json>] [--name <name>] [--build <build>]
  pscli list-rules  [--pack <pack.json>] [--language zh|en] [--json]
  pscli plan        [--index <...>|--game <root>] [--pack <pack.json>] --preset safe|clean|performance|full [--profile <name>] [--categories a,b] [--json] [--out <plan.json>]
  pscli apply       [--index <...>|--game <root>] [--pack <pack.json>] --preset safe|clean|performance|full [--profile <name>] [--categories a,b] [--yes] [--dry-run] [--force] [--include-experimental]
  pscli status      [--index <...>|--game <root>] [--json]
  pscli restore     [--index <...>|--game <root>] [--backup <file>|--index-url <url>] [--clean-only] [--force] [--yes]
  pscli profiles    --action list|show|save|delete [--name <name>] [--preset ...] [--categories ...] [--ignore <file>]
  pscli backups     [--prune]
  pscli self-test   --index <Bundles2/_.index.bin> [--game-path <metadata/path>] [--backup-dir <dir>] [--yes] [--keep] [--force]
  pscli fixture     --out <Bundles2-dir> [--path <metadata/path.ao>] [--content <text>]
  pscli version

Common options:
  --index <file>      Bundles2/_.index.bin
  --game <dir>        PoE2 game root (contains Bundles2)
  --pack <file>       Rule pack JSON (default: app data rulepacks/default.json)
  --preset <name>     safe (default), clean, performance, full
  --profile <name>    Load preset/categories/ignore from a saved profile
  --categories <a,b>  Restrict to these category ids
  --ignore <file>     Ignore list file
  --yes               Actually perform a modifying operation
  --dry-run           Build the plan without writing
  --force             Continue despite game running / changed index / existing custom bundles
  --include-experimental  Include .pet/.epk/.mat/.fxgraph rules
  --json              JSON output

M0 diagnostics:
  inspect/peek/dump-paths/self-test/fixture operate directly on an index and do not use the rule pack.
""");
    }
}

internal sealed class ParsedArgs
{
    private readonly Dictionary<string, string> _values;
    private readonly HashSet<string> _flags;

    private ParsedArgs(Dictionary<string, string> values, HashSet<string> flags)
    {
        _values = values;
        _flags = flags;
    }

    public static ParsedArgs Parse(string[] args, HashSet<string> flagOptions, HashSet<string> valueOptions)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"unexpected argument '{arg}'");

            var name = arg[2..];
            if (flagOptions.Contains(name))
            {
                flags.Add(name);
                continue;
            }

            if (!valueOptions.Contains(name))
                throw new ArgumentException($"unknown option '--{name}'");

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"option '--{name}' requires a value");

            values[name] = args[++i];
        }

        return new ParsedArgs(values, flags);
    }

    public string? Get(string name) => _values.TryGetValue(name, out var value) ? value : null;

    public string Require(string name)
    {
        var value = Get(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"missing required option '--{name}'");
        return value;
    }

    public bool HasFlag(string name) => _flags.Contains(name);
}