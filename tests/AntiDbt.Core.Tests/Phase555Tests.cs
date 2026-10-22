using System.Text.Json;

internal sealed record Phase555TestResult(string Name, string Suite, bool Passed, string? Error);

internal static class Phase555Tests
{
    public static async Task<IReadOnlyList<Phase555TestResult>> RunCasesAsync(string? suite, string? name)
    {
        var results = new List<Phase555TestResult>();

        if (suite == null || suite == "AQ01")
        {
            results.AddRange(await DisplayGroupRevisionTests.RunAllAsync(name));
        }

        if (suite == null || suite == "AQ03" || suite == "Regression")
        {
            results.AddRange(await TranscriptOrderTests.RunAllAsync(suite, name));
        }

        return results;
    }

    public static async Task<int> RunCommandAsync(string[] args)
    {
        if (args.Contains("--r02-repeat"))
        {
            int count = 30;
            for (int j = 0; j < args.Length; j++)
            {
                if (args[j] == "--r02-repeat" && j + 1 < args.Length && int.TryParse(args[j + 1], out var parsed))
                    count = parsed;
            }
            try
            {
                await TranscriptOrderTests.RunR02RepeatsAsync(count);
                Console.WriteLine(JsonSerializer.Serialize(new { total = count, failed = 0, passed = true }));
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { total = count, failed = 1, error = ex.ToString() }));
                return 1;
            }
        }

        string? suite = null;
        string? name = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--suite" && i + 1 < args.Length)
            {
                suite = args[++i];
            }
            else if (args[i] == "--case" && i + 1 < args.Length)
            {
                name = args[++i];
            }
        }

        var results = await RunCasesAsync(suite, name);
        if (results.Count == 0)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                total = 0,
                failed = 0,
                error = "Zero tests matched or invalid arguments",
                results
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 2;
        }

        int failed = results.Count(r => !r.Passed);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            total = results.Count,
            failed,
            results
        }, new JsonSerializerOptions { WriteIndented = true }));

        return failed == 0 ? 0 : 1;
    }
}
