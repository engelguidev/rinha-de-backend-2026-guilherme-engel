namespace Rinha.FraudDetection.Tools;

public sealed class ProbeOptions
{
    public string PayloadPath { get; set; } = "resources/example-payloads.json";
    public string ResourcesPath { get; set; } = "resources";
    public string IndexPath { get; set; } = "data/knn.idx";
    public string ReferencesPath { get; set; } = "resources/example-references.json";
    public int K { get; set; } = 5;

    public static ProbeOptions FromArgs(string[] args)
    {
        var options = new ProbeOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--payload" && i + 1 < args.Length)
            {
                options.PayloadPath = args[++i];
            }
            else if (arg == "--resources" && i + 1 < args.Length)
            {
                options.ResourcesPath = args[++i];
            }
            else if (arg == "--index" && i + 1 < args.Length)
            {
                options.IndexPath = args[++i];
            }
            else if (arg == "--references" && i + 1 < args.Length)
            {
                options.ReferencesPath = args[++i];
            }
            else if (arg == "--k" && i + 1 < args.Length && int.TryParse(args[++i], out var k))
            {
                options.K = k;
            }
        }

        return options;
    }
}
