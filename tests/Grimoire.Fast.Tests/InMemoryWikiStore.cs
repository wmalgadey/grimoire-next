using Grimoire.Wiki;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The Fast suite's wiki: a dictionary of paths to text. An in-memory adapter at an owned port,
/// which is the only kind of double this project uses (Constitution III.9); what a real filesystem
/// does at this port is the Contract suite's business.
/// </summary>
internal sealed class InMemoryWikiStore : IWikiStore
{
    public const string LogPath = "log.md";

    private readonly Dictionary<string, string> files = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Files => files;

    /// <summary>
    /// Run inside <see cref="ReadAsync"/>, which is the one call on the run's stopping path that
    /// leaves the process — and so the one place a run can end while the hub is waiting.
    /// </summary>
    public Action? WhileReading { get; set; }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([.. files.Keys.Order(StringComparer.Ordinal)]);

    public Task<string?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        WhileReading?.Invoke();

        return Task.FromResult(files.GetValueOrDefault(path));
    }

    public Task WriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        files[path] = content;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Appended the way <c>FileSystemWikiStore</c> appends it: whatever the entry ended with, the
    /// log ends with exactly one blank line after it. A double that shaped the file differently
    /// from the adapter it stands in for would not merely miss a difference — it would hide one.
    /// </summary>
    public Task AppendLogAsync(string entry, CancellationToken cancellationToken)
    {
        files[LogPath] = files.GetValueOrDefault(LogPath, string.Empty) + entry.TrimEnd('\n') + "\n\n";
        return Task.CompletedTask;
    }
}
