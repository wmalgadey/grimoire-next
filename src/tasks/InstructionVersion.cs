namespace Grimoire.Tasks;

/// <summary>
/// Which revision of the instruction file a run's behaviour is attributable to (FR-014, SC-009).
/// Recorded at dispatch, before the first model call.
/// </summary>
/// <param name="Path">Repo-relative path of the instruction file loaded.</param>
/// <param name="Sha256">Full content hash of the file's bytes, lowercase hex.</param>
/// <param name="ByteLength">Byte length of the instruction file as loaded.</param>
public sealed record InstructionVersion(string Path, string Sha256, int ByteLength)
{
    /// <summary>
    /// The form the task view shows: <c>sha256:</c> plus the first 12 hex characters (research R10).
    /// </summary>
    public string ShortDisplay => $"sha256:{Sha256[..Math.Min(12, Sha256.Length)]}";
}
