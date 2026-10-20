using System.Reflection;
using Plumbline;
using Plumbline.Cli;

try
{
    var options = CliOptions.Parse(args);
    switch (options.Command)
    {
        case "help":
            Console.WriteLine(CliOptions.Usage);
            return 0;
        case "version":
            Console.WriteLine(typeof(SuiteLoader).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]);
            return 0;
        case "validate":
            var suite = SuiteLoader.LoadFile(options.SuitePath!);
            Console.WriteLine($"OK  {suite.Name}: {suite.Cases.Count} cases, {suite.Cases.Sum(c => c.Assertions.Count)} assertions, model {suite.Model.Name}");
            return 0;
        default:
            using (var cts = new CancellationTokenSource())
            {
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };
                var stepSummary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
                return await new RunCommand(Console.Out, stepSummaryPath: stepSummary).ExecuteAsync(options, cts.Token);
            }
    }
}
catch (Exception ex) when (ex is CliException or SuiteException or InvalidDataException or FileNotFoundException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 2;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("cancelled");
    return 130;
}
