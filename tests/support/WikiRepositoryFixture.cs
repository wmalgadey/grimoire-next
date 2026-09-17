using System.Diagnostics;
using System.Security.Cryptography;

namespace Grimoire.Tests.Support;

/// <summary>
/// A real wiki git repository on a real filesystem, seeded with a commit. Not a double: the LLM is
/// the only sanctioned double in this system (constitution III.2).
/// </summary>
public sealed class WikiRepositoryFixture : IDisposable
{
    /// <param name="seed">Pages the wiki already contains, so a run can be seen to consult them.</param>
    public WikiRepositoryFixture(IReadOnlyDictionary<string, string>? seed = null)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"grimoire-wiki-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);

        Git("init", "--initial-branch=main");
        Git("config", "user.email", "grimoire@test.invalid");
        Git("config", "user.name", "Grimoire");

        foreach (var (page, content) in seed ?? new Dictionary<string, string> { ["index.md"] = "# Index\n" })
        {
            Write(page, content);
        }

        Git("add", "-A");
        Git("commit", "-m", "seed");
    }

    /// <summary>The working tree the agent writes into and the hub commits from.</summary>
    public string Path { get; }

    /// <summary>The current tip.</summary>
    public string Head() => Git("rev-parse", "HEAD").Trim();

    /// <summary>How many commits history holds — "exactly one commit per changed run" is a count.</summary>
    public int CommitCount() =>
        int.Parse(Git("rev-list", "--count", "HEAD").Trim(), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Writes a page directly, for seeding and for planting content a run will read.</summary>
    public void Write(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    /// <summary>Whether a page exists in the working tree.</summary>
    public bool Exists(string relativePath) => File.Exists(System.IO.Path.Combine(Path, relativePath));

    /// <summary>A page's content, or <c>null</c> when there is no such page.</summary>
    public string? Read(string relativePath)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        return File.Exists(full) ? File.ReadAllText(full) : null;
    }

    /// <summary>
    /// A content hash per tracked page. Comparing two snapshots is how "byte-identical to the
    /// pre-run commit" is asserted (FR-017, SC-003).
    /// </summary>
    public Dictionary<string, string> Snapshot()
    {
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
        {
            var relative = System.IO.Path.GetRelativePath(Path, file);
            if (relative.StartsWith(".git", StringComparison.Ordinal))
            {
                continue;
            }

            snapshot[relative] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        }

        return snapshot;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!Directory.Exists(Path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private string Git(params string[] args)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode is not 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
        }

        return stdout;
    }
}
