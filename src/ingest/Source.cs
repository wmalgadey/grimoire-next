namespace Grimoire.Ingest;

/// <summary>What the user submitted.</summary>
public enum SourceKind
{
    /// <summary>Pasted text. The text handed to the run is the submitted value itself.</summary>
    Text,

    /// <summary>A URL. The text handed to the run is what was retrieved from it before dispatch.</summary>
    Url,
}

/// <summary>
/// What the user submitted, belonging to exactly one task and retained as long as the task is
/// (FR-004). There is no size limit and no truncation (FR-029): the text is handed to the run whole.
/// </summary>
/// <param name="Kind">Pasted text or a URL.</param>
/// <param name="SubmittedValue">
/// The pasted text, or the URL as typed. Non-empty after trimming, else the submission is
/// rejected with no task created (FR-001).
/// </param>
/// <param name="RetrievedText">
/// For <see cref="SourceKind.Url"/>, the text fetched before dispatch: <c>null</c> until
/// retrieved and permanently <c>null</c> if retrieval failed (FR-003).
/// </param>
/// <param name="RetrievedAt">Set with <paramref name="RetrievedText"/>.</param>
/// <param name="ByteLength">
/// Byte length of the text handed to the run, recorded so an oversized-source failure can name
/// the size (FR-029).
/// </param>
public sealed record Source(
    SourceKind Kind,
    string SubmittedValue,
    string? RetrievedText,
    DateTimeOffset? RetrievedAt,
    int ByteLength)
{
    /// <summary>
    /// The text handed to the run, whole: the submitted value for pasted text, the retrieved
    /// text for a URL. <c>null</c> when a URL has not been retrieved or retrieval failed — in
    /// which case no run is dispatched (FR-003).
    /// </summary>
    public string? TextForRun => Kind is SourceKind.Text ? SubmittedValue : RetrievedText;
}
