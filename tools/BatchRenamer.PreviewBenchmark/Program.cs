using System.Diagnostics;
using System.Text.Json;
using BatchRenamer.Core;

namespace BatchRenamer.PreviewBenchmark;

internal static class Program
{
    public static int Main(string[] args)
    {
        var options = Options.Parse(args);
        if (options.Items < 1 || options.Items > 100_000 || options.Warmup < 0)
        {
            Console.Error.WriteLine("ERROR: --items must be 1..100000 and --warmup must be >= 0.");
            return 2;
        }

        var inputs = Enumerable.Range(1, options.Items)
            .Select(index => new PreviewInputItem(
                Guid.NewGuid(),
                @"C:\PS05\Preview",
                $"IMG_SEARCH_{index:D6}.JPG",
                $"IMG_SEARCH_{index:D6}",
                ".JPG",
                IsIncluded: true))
            .ToArray();
        var rules = new RenameRuleSet(
            "Collection",
            OriginalNameMode.BeforeBaseName,
            "Public_",
            "_Ready",
            "SEARCH",
            "Found",
            NameCaseMode.TitleCaseWords,
            new SequenceConfig(true, 1, 1, 6, BatchRenamer.Core.SequencePosition.AfterName, "_"));

        for (var i = 0; i < options.Warmup; i++)
            Validate(PreviewEngine.Build(inputs, rules), options.Items);

        var wall = Stopwatch.StartNew();
        var result = PreviewEngine.Build(inputs, rules);
        wall.Stop();
        Validate(result, options.Items);

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            items = options.Items,
            warmup = options.Warmup,
            included = result.IncludedCount,
            changed = result.ChangedCount,
            computeMs = result.ComputeTime.TotalMilliseconds,
            wallMs = wall.Elapsed.TotalMilliseconds,
            first = result.Items[0].NewName,
            last = result.Items[^1].NewName,
        }));
        return 0;
    }

    private static void Validate(PreviewBatchResult result, int expected)
    {
        if (result.Items.Count != expected || result.IncludedCount != expected || result.ChangedCount != expected)
            throw new InvalidOperationException("Preview result count mismatch.");
    }

    private sealed class Options
    {
        public int Items { get; init; } = 20_000;
        public int Warmup { get; init; } = 1;

        public static Options Parse(string[] args)
        {
            var items = 20_000;
            var warmup = 1;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--items" when i + 1 < args.Length && int.TryParse(args[++i], out var value):
                        items = value;
                        break;
                    case "--warmup" when i + 1 < args.Length && int.TryParse(args[++i], out var value):
                        warmup = value;
                        break;
                    default:
                        throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
                }
            }
            return new Options { Items = items, Warmup = warmup };
        }
    }
}
