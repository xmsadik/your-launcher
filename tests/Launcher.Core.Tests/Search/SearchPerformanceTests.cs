using System.Diagnostics;
using YourLauncher.Core.Model;
using YourLauncher.Core.Search;
using Xunit.Abstractions;

namespace YourLauncher.Core.Tests.Search;

/// <summary>Perf guard for spec §7's "< 16 ms even at 5,000 nodes" requirement.</summary>
[Collection(TimingCollection.Name)]
public class SearchPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public SearchPerformanceTests(ITestOutputHelper output) => _output = output;

    private static readonly string[] NamePool =
    {
        "Visual Studio Code", "Şifre Yöneticisi", "Haftalık Rapor", "SAP Dev System", "Build & Deploy",
        "Notepad", "Documents", "Downloads", "Anthropic", "Google Chrome", "Slack", "Microsoft Teams",
        "Excel Rapor", "İzmir Şube", "Ankara Ofis", "Configuration", "Backup Script", "Database Client",
        "Docker Desktop", "Git Bash", "PowerShell", "Windows Terminal", "Outlook", "OneNote", "Calculator",
        "Paint", "Task Manager", "Control Panel", "Network Settings", "VPN Client", "Şubeler", "Işık Ayarları",
    };

    private static FolderNode BuildTree(int totalNodes, int seed)
    {
        var root = new FolderNode { Id = "root", Name = "Root" };
        var rng = new Random(seed);
        var folders = new List<FolderNode> { root };

        for (var id = 1; id <= totalNodes; id++)
        {
            var parent = folders[rng.Next(folders.Count)];
            var namePart = NamePool[rng.Next(NamePool.Length)];
            var name = $"{namePart} {id}";

            // Keep ~1 in 5 new nodes a folder (capped) so the tree gets several levels deep, like a real config.
            var makeFolder = folders.Count < totalNodes / 4 && rng.NextDouble() < 0.25;
            if (makeFolder)
            {
                var folder = new FolderNode { Id = $"f{id}", Name = name };
                parent.Children.Add(folder);
                folders.Add(folder);
            }
            else
            {
                Node leaf = (id % 4) switch
                {
                    0 => new AppNode { Id = $"a{id}", Name = name, Target = @"C:\x.exe" },
                    1 => new PathNode { Id = $"p{id}", Name = name, Target = @"C:\x" },
                    2 => new CommandNode { Id = $"c{id}", Name = name, Command = "echo hi" },
                    _ => new UrlNode { Id = $"u{id}", Name = name, Target = "https://example.com" },
                };
                parent.Children.Add(leaf);
            }
        }

        return root;
    }

    [Fact]
    public void Search_5000Nodes_MedianUnder16Milliseconds()
    {
        var root = BuildTree(5000, seed: 42);
        var index = FlatIndex.Build(root);
        Assert.Equal(5000, index.Entries.Count);

        var engine = new SearchEngine();
        var queries = new[]
        {
            "a", "vs", "sap", "rapor", "izmir", "docker", "notepad", "şube", "isik", "build",
            "sd", "gd", "vsc", "ofis", "excel", "sap dev", "git bash", "control", "conf", "z",
        };

        // Warm up (JIT, any lazy allocation paths) before timing.
        foreach (var q in queries)
        {
            engine.Search(index, q);
        }

        // Best of 5 runs per query: a single GC pause or scheduler hiccup is noise, not search cost.
        var timings = new List<double>();
        foreach (var q in queries)
        {
            var best = double.MaxValue;
            for (var run = 0; run < 5; run++)
            {
                var sw = Stopwatch.StartNew();
                engine.Search(index, q);
                sw.Stop();
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }

            timings.Add(best);
        }

        timings.Sort();
        var median = timings[timings.Count / 2];
        var p95 = timings[(int)Math.Ceiling(timings.Count * 0.95) - 1];

        _output.WriteLine($"Search perf over {queries.Length} queries on 5000 nodes: median={median:F3} ms, p95={p95:F3} ms");
        _output.WriteLine($"All timings (ms): {string.Join(", ", timings.Select(t => t.ToString("F3")))}");

        Assert.True(median < 16.0, $"Median search time {median:F3} ms exceeded 16 ms budget");
    }
}
