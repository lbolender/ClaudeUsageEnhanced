using System.Runtime.InteropServices;
using Microsoft.CommandPalette.Extensions;

namespace ClaudeUsage;

[Guid("3033a89c-3cdb-47ff-ba53-14c8ae9f53e2")]
public sealed partial class ClaudeUsageExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _disposed;
    private readonly ClaudeUsageCommandsProvider _provider = new();

    public ClaudeUsageExtension(ManualResetEvent disposed)
    {
        _disposed = disposed;
    }

    public object? GetProvider(ProviderType providerType) =>
        providerType == ProviderType.Commands ? _provider : null;

    public void Dispose() => _disposed.Set();
}
