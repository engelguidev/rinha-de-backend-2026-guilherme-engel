using Rinha.FraudDetection.Tools;

var mode = args.Length > 0 ? args[0] : "build";

return mode switch
{
	"build" => await new IndexBuildRunner(IndexBuildOptions.FromEnvironment()).RunAsync(CancellationToken.None),
	"probe" => await new ProbeRunner(ProbeOptions.FromArgs(args.Skip(1).ToArray())).RunAsync(CancellationToken.None),
	_ => 1
};
