using System.Text.RegularExpressions;

namespace Grimoire.Trace;

/// <summary>
/// Reads the requirement ids and their proof kinds out of <c>docs/capabilities/*.md</c>. Those
/// files are the as-is description of the system (Constitution IV.2); this reader takes only the
/// three columns it needs and judges nothing else about them.
/// </summary>
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

    public static IReadOnlyList<Requirement> Read(string capabilitiesDirectory)
    {
        if (!Directory.Exists(capabilitiesDirectory))
        {
            throw new TraceInputException($"no capability files: {capabilitiesDirectory} does not exist");
        }

        var requirements = new List<Requirement>();

        foreach (var file in Directory.GetFiles(capabilitiesDirectory, "*.md").Order(StringComparer.Ordinal))
        {
            var retiredSection = false;

            var number = 0;

            foreach (var line in File.ReadLines(file))
            {
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
                            $"{Path.GetFileName(file)} line {number} opens like a requirement row and does not read as one: "
                            + $"\"{line.Trim()}\". A row is | <CAPABILITY>-NNN | text | test, eval or review |");
                    }

                    continue;
                }

                var id = row.Groups["id"].Value;
                requirements.Add(new Requirement(
                    id,
                    CapabilityOf(id),
                    TextColumn(line),
                    ParseProof(row.Groups["proof"].Value, id, file),
                    retiredSection));
            }
        }

        var duplicate = requirements.GroupBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new TraceInputException($"{duplicate.Key} is registered more than once; an id is never reused (Constitution IV.1)");
        }

        return requirements;
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

    private static ProofKind ParseProof(string proof, string id, string file) => proof.ToLowerInvariant() switch
    {
        "test" => ProofKind.Test,
        "eval" => ProofKind.Eval,
        "review" => ProofKind.Review,
        _ => throw new TraceInputException(
            $"{id} in {Path.GetFileName(file)} declares proof '{proof}'; Constitution III.1 allows test, eval or review"),
    };
}
