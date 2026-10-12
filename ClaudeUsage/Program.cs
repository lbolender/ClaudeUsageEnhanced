using JPSoftworks.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsage;

public static class Program
{
    [MTAThread]
    public static async Task Main(string[] args)
    {
        await ExtensionHostRunner.RunAsync(
            args,
            new ExtensionHostRunnerParameters
            {
                PublisherMoniker = "LBolender",
                ProductMoniker = "ClaudeUsage",
                ExtensionFactories = new()
                {
                    new DelegateExtensionFactory(disposed => new ClaudeUsageExtension(disposed)),
                },
            });
    }
}
