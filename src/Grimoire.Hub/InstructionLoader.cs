using Grimoire.Runs;

namespace Grimoire.Hub;

/// <summary>
/// The two texts every run receives, and the only thing that puts text into the agent's prompt
/// (Constitution V.1).
/// </summary>
/// <remarks>
/// <para>
/// Both are read from the paths the hub was started with. The instruction is Grimoire's own,
/// versioned in this repository, and changing it is an owner decision named in the PR; the purpose
/// description is the user's, and Grimoire neither creates nor changes it.
/// </para>
/// <para>
/// Nothing else reaches the prompt. The run is started with no settings loaded from the machine
/// and in a working directory Grimoire owns, so nothing in the wiki or on the host can add to what
/// is assembled here (research.md R-11).
/// </para>
/// </remarks>
public sealed class InstructionLoader(string instructionPath, string purposeDescriptionPath)
{
    /// <summary>
    /// Whether each of the two is in place <em>now</em>. Checked per submission rather than once
    /// at start-up, because INGEST-003 is about the state of those paths when a text is submitted.
    /// The instruction is looked at first, so a submission made with neither in place is refused
    /// for the instruction and the user is told exactly one thing.
    /// </summary>
    public StartUpInputs Read() => new(
        InstructionPresent: File.Exists(instructionPath),
        PurposeDescriptionPresent: File.Exists(purposeDescriptionPath));

    /// <summary>
    /// What a run is given: the instruction, the purpose description, the run's identifier and the
    /// submitted text, in that order and nothing besides (INGEST-002,
    /// <c>contracts/agent-cli-protocol.md</c>).
    /// </summary>
    public string Assemble(string text, Guid runId) =>
        Payload(File.ReadAllText(instructionPath), File.ReadAllText(purposeDescriptionPath), text, runId);

    /// <summary>
    /// The payload's shape, apart from where its parts are read from — which is what lets the Fast
    /// suite prove INGEST-002 without a filesystem.
    /// </summary>
    public static string Payload(string instruction, string purposeDescription, string text, Guid runId) =>
        $"{instruction}\n\n{purposeDescription}\n\nRun id: {runId}\n\n{text}";
}
