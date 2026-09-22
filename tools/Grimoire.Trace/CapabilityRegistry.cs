using System.Text.RegularExpressions;

namespace Grimoire.Trace;

/// <summary>A capability file: the name an error message quotes, and the file's text.</summary>
internal readonly record struct CapabilityFile(string Name, string Text);

/// <summary>
/// Reads the requirement ids and their proof kinds out of <c>docs/capabilities/*.md</c>. Those
/// files are the as-is description of the system (Constitution IV.2); this reader takes only the
/// three columns it needs and judges nothing else about them.
/// </summary>
/// <remarks>
/// Finding the files is <see cref="Read(string)"/> and reading them is <see cref="Parse"/>. The
/// split is not decoration: every judgment the gate makes about a registration is in
/// <see cref="Parse"/>, which is given text, so the gate's own rules are provable without a
/// directory to put files in (Constitution IV.3).
/// </remarks>
internal static partial class CapabilityRegistry
{
    /// <summary>
    /// A table row registering a requirement: <c>| ID | text | proof |</c>. Separator rows and
    /// prose do not match.
    /// </summary>
    [GeneratedRegex(@"^\|\s*(?<id>[A-Z][A-Z0-9]*-\d{3})\s*\|.*\|\s*(?<proof>[A-Za-z]+)\s*\|\s*$")]
    private static partial Regex RequirementRow { get; }

    /// <summary>
    /// A row that opens with something shaped like a requirement id. One that then fails
    /// <see cref="RequirementRow"/> is a registration this reader cannot read — a lower-case or
    /// mis-numbered id, a missing proof column, text after the last pipe — and it fails the read
    /// rather than being passed over (Constitution IV.3: what the check cannot read, it fails on
    /// rather than skips). Skipping it would drop the requirement out of the gate silently.
    /// </summary>
    [GeneratedRegex(@"^\|\s*[A-Za-z][A-Za-z0-9]*-\d+[a-z]*\s*\|")]
    private static partial Regex RequirementRowOpening { get; }

    /// <summary>Prefixes Constitution I.2 reserves; they are never capability names.</summary>
    private static readonly string[] ReservedPrefixes = ["OUT", "DEC"];

    public static bool IsReserved(string requirementId) =>
        ReservedPrefixes.Contains(CapabilityOf(requirementId), StringComparer.Ordinal);

    public static string CapabilityOf(string requirementId)
    {
        var dash = requirementId.IndexOf('-', StringComparison.Ordinal);
        return dash < 0 ? requirementId : requirementId[..dash];
    }

    /// <summary>
    /// Every requirement the capability files register. The only part of the registry that
    /// touches the filesystem: it finds the files, reads them, and hands the text on.
    /// </summary>
    public static IReadOnlyList<Requirement> Read(string capabilitiesDirectory)
    {
        if (!Directory.Exists(capabilitiesDirectory))
        {
            throw new TraceInputException($"no capability files: {capabilitiesDirectory} does not exist");
        }

        return Parse(Directory.GetFiles(capabilitiesDirectory, "*.md")
            .Order(StringComparer.Ordinal)
            .Select(file => new CapabilityFile(Path.GetFileName(file), File.ReadAllText(file))));
    }

    /// <summary>
    /// The same reading, given the files as text. Every judgment is here: which rows register a
    /// requirement, which row fails the read, what is retired, and that no id appears twice.
    /// </summary>
    public static IReadOnlyList<Requirement> Parse(IEnumerable<CapabilityFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var requirements = new List<Requirement>();
        var read = 0;

        foreach (var file in files)
        {
            read++;
            requirements.AddRange(ParseOne(file));
        }

        if (read == 0)
        {
            // No file at all is not a registry of no requirements; it is a registry that could
            // not be read. Taken as empty, the gate would pass over nothing and report success,
            // so deleting or renaming the capability files would switch the gate off rather than
            // fail it — which is the one thing IV.3 says a gate must not do.
            throw new TraceInputException("no capability files: nothing to read a requirement from");
        }

        var duplicate = requirements.GroupBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new TraceInputException($"{duplicate.Key} is registered more than once; an id is never reused (Constitution IV.1)");
        }

        return requirements;
    }

    private static IEnumerable<Requirement> ParseOne(CapabilityFile file)
    {
        var retiredSection = false;

        var number = 0;

        foreach (var raw in file.Text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            number++;

            if (line.StartsWith('#'))
            {
                retiredSection = line.TrimStart('#', ' ').StartsWith("Retired", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var row = RequirementRow.Match(line);
            if (!row.Success)
            {
                if (RequirementRowOpening.IsMatch(line))
                {
                    throw new TraceInputException(
                        $"{file.Name} line {number} opens like a requirement row and does not read as one: "
                        + $"\"{line.Trim()}\". A row is | <CAPABILITY>-NNN | text | test, eval or review |");
                }

                continue;
            }

            var id = row.Groups["id"].Value;
            yield return new Requirement(
                id,
                CapabilityOf(id),
                TextColumn(line),
                ParseProof(row.Groups["proof"].Value, id, file.Name),
                retiredSection);
        }
    }

    /// <summary>
    /// Everything between the first and the last column. Joining the middle back together keeps a
    /// requirement whose text contains a pipe readable.
    /// </summary>
    private static string TextColumn(string line)
    {
        var columns = line.Trim().Trim('|').Split('|');
        return columns.Length <= 2
            ? string.Empty
            : string.Join('|', columns[1..^1]).Trim();
    }

    private static ProofKind ParseProof(string proof, string id, string fileName) => proof.ToLowerInvariant() switch
    {
        "test" => ProofKind.Test,
        "eval" => ProofKind.Eval,
        "review" => ProofKind.Review,
        _ => throw new TraceInputException(
            $"{id} in {fileName} declares proof '{proof}'; Constitution III.1 allows test, eval or review"),
    };
}
