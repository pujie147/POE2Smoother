using PsCore;

namespace PsCli.Commands;

internal static class ApplyCommand
{
    public static int Run(ParsedArgs args)
    {
        var paths = CommandUtil.Paths();
        var game = CommandUtil.ResolveGame(args);
        var pack = CommandUtil.LoadPack(args, paths, game);
        var packPath = args.Get("pack") ?? paths.DefaultRulePackPath;
        var service = new AppService(paths);
        var request = CommandUtil.BuildApplyRequest(args, paths, game, pack, packPath);

        var yes = args.HasFlag("yes");
        if (!yes && !request.DryRun)
        {
            var preview = service.BuildPlan(request);
            Console.WriteLine($"Would apply {preview.Changes.Count} files ({ArchiveUtil.FormatBytes(preview.TotalBytes)})");
            Console.WriteLine($"preset: {request.Preset}, pack: {pack.Name}");
            Console.WriteLine($"ignored: {preview.Ignored.Count}, missing: {preview.Missing.Count}");
            Console.WriteLine($"unchanged: {preview.Unchanged.Count}, skippedExperimental: {preview.SkippedExperimental.Count}");
            Console.WriteLine();
            Console.WriteLine("Re-run with --yes to apply.");
            return 0;
        }

        var lastPercent = -1;
        IProgress<PatchProgress>? progress = new Progress<PatchProgress>(p =>
        {
            if (p.Total <= 0)
                return;

            var percent = p.Done * 100 / p.Total;
            if (percent != lastPercent || p.Done == p.Total)
            {
                lastPercent = percent;
                Console.Error.Write($"\r{p.Phase,-6} {p.Done,6}/{p.Total,-6} {percent,3}%");
                if (p.Done == p.Total)
                    Console.Error.WriteLine();
            }
        });

        var result = service.Apply(request, progress);
        Console.WriteLine();
        Console.WriteLine($"files      : {result.Plan.Changes.Count}");
        Console.WriteLine($"unchanged  : {result.Plan.Unchanged.Count}");
        Console.WriteLine($"skippedExp : {result.Plan.SkippedExperimental.Count}");
        Console.WriteLine($"dry run    : {result.DryRun}");
        Console.WriteLine($"original   : {result.OriginalSha256}");
        Console.WriteLine($"applied    : {result.AppliedSha256}");
        if (!result.DryRun)
            Console.WriteLine($"backup     : {result.BackupPath}");
        Console.WriteLine();
        Console.WriteLine(result.DryRun ? "Dry run complete." : "Apply complete. Restart PoE2.");
        return 0;
    }
}