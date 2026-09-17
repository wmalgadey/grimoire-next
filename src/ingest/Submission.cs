using System.Text;

namespace Grimoire.Ingest;

/// <summary>
/// What the user sent, before it is anything else. Validating it is the one decision the system
/// makes without asking the agent anything (FR-001).
/// </summary>
/// <param name="Kind">Pasted text or a URL.</param>
/// <param name="Value">The pasted text, or the URL as typed.</param>
public sealed record Submission(SourceKind Kind, string Value)
{
    /// <summary>
    /// Turns a submission into a <see cref="Source"/>, or says why it is not one.
    /// </summary>
    /// <remarks>
    /// The rule is "empty after trimming", not "contains no whitespace": padding is not emptiness,
    /// and a source that is only spaces is not a source. A rejected submission creates <b>no
    /// task</b> — it never became work (FR-001).
    /// </remarks>
    public static SubmissionResult Accept(SourceKind kind, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SubmissionResult.Rejected(
                kind is SourceKind.Url
                    ? "A URL submission needs a URL. Nothing was submitted but whitespace."
                    : "A text submission needs text. Nothing was submitted but whitespace.");
        }

        // The submitted value is kept as typed. Only the emptiness test trims: an ingest of
        // pasted notes should get the notes, indentation and all (FR-029).
        return SubmissionResult.Accepted(new Source(
            kind,
            value,
            RetrievedText: null,
            RetrievedAt: null,
            ByteLength: kind is SourceKind.Text ? Encoding.UTF8.GetByteCount(value) : 0));
    }
}

/// <summary>Whether a submission became a source, and why it did not.</summary>
public readonly record struct SubmissionResult
{
    private SubmissionResult(Source? source, string? rejection)
    {
        Source = source;
        Rejection = rejection;
    }

    /// <summary>The source, when the submission was accepted.</summary>
    public Source? Source { get; }

    /// <summary>Why the submission was refused, in words the operator sees.</summary>
    public string? Rejection { get; }

    /// <summary>Whether this submission becomes a task.</summary>
    public bool IsAccepted => Source is not null;

    internal static SubmissionResult Accepted(Source source) => new(source, null);

    internal static SubmissionResult Rejected(string rejection) => new(null, rejection);
}
