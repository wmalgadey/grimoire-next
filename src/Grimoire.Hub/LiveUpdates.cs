using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Grimoire.Hub;

/// <summary>
/// How far through what it is watching one subscriber has been sent (research.md R-05).
/// </summary>
/// <remarks>
/// The one thing a stream keeps per subscriber, and it exists for exactly one stream: a record
/// reaches the low hundreds of kilobytes, so it is the one view whose events carry an increment
/// rather than the whole of what the view shows (ACCESS-006). The submissions list and the chat
/// leave it untouched.
/// <para>
/// Mutable, and moved by the delegate that made the payload: what was sent and how far that took
/// the subscriber are one decision, and split in two a failure between them would either repeat
/// bytes or skip them.
/// </para>
/// </remarks>
public sealed class Sent
{
    /// <summary>How many bytes of what this subscriber watches it has already been given.</summary>
    public int Bytes { get; set; }

    /// <summary>How many lost entries this subscriber has already been told about.</summary>
    public int EntriesLost { get; set; }
}

/// <summary>
/// What the browser is sent while it has a page open (ACCESS-005, ACCESS-006, ACCESS-007).
/// </summary>
/// <remarks>
/// <para>
/// A plain class — <b>no port and no interface</b>. Nothing outside the process is behind it and no
/// second implementation exists, which is when Constitution II.4 forbids one. What is outside the
/// process is the connection each stream is written to, and that is the host's.
/// </para>
/// <para>
/// One unbounded <see cref="Channel"/> per subscriber, drained by the endpoint serving that
/// subscriber's stream. Unbounded because the alternative to dropping a browser's events is
/// bounding them, and a bound reached would either block the board under its lock or lose a change
/// the page then never shows. What is on a channel is a bare signal rather than a payload, so a
/// slow reader's queue is a handful of bytes however long it is behind (research.md R-05).
/// </para>
/// <para>
/// <b>Nothing is replayed.</b> What is published while nobody is subscribed reaches nobody: the
/// snapshot every stream opens with is what answers a reconnect, so a browser that comes back reads
/// what it is looking at as it then stands, including what arrived while it was away (ACCESS-007,
/// research.md R-01). There is therefore no buffer, no <c>Last-Event-ID</c> and nothing to expire.
/// </para>
/// </remarks>
public sealed class LiveUpdates
{
    /// <summary>The submissions list — one topic, because every browser reading it reads the same list.</summary>
    public const string Submissions = "submissions";

    /// <summary>The chat — one topic too, because there is exactly one chat (QUERY-005).</summary>
    public const string Chat = "chat";

    /// <summary>
    /// A signal, and the whole of what a channel carries. What to send is worked out per subscriber
    /// when it wakes, which is what keeps a slow reader's queue small however far behind it is.
    /// </summary>
    private const byte Something = 0;

    private readonly Lock gate = new();

    /// <summary>
    /// Every subscriber, by topic. A list rather than a dictionary keyed by subscriber: there is no
    /// identity to key on and nothing addresses one subscriber — a change wakes all of them.
    /// </summary>
    private readonly Dictionary<string, List<Channel<byte>>> watchers = new(StringComparer.Ordinal);

    /// <summary>One run's record, as a topic of its own.</summary>
    public static string RecordOf(Guid runId) => $"record/{runId}";

    /// <summary>
    /// Something a stream shows has changed: every subscriber to that topic is woken, and each then
    /// works out for itself what it has not been sent.
    /// </summary>
    /// <remarks>
    /// It never blocks and it never throws. The callers are the board under its lock and the
    /// conductor under a run's, and a publish that could wait would hold one of those for as long as
    /// a browser is slow — so the channels are unbounded and a write to one always succeeds.
    /// </remarks>
    public void Changed(string topic)
    {
        lock (gate)
        {
            if (!watchers.TryGetValue(topic, out var subscribers))
            {
                return;
            }

            foreach (var subscriber in subscribers)
            {
                subscriber.Writer.TryWrite(Something);
            }
        }
    }

    /// <summary>
    /// One subscriber's stream: the opening payload, and then one payload per change that has
    /// something to tell this subscriber.
    /// </summary>
    /// <param name="opening">
    /// The snapshot, carrying the whole of what the view shows. Every stream opens with one, which
    /// is what answers ACCESS-007's reconnect clause with no replay buffer behind it.
    /// </param>
    /// <param name="next">
    /// What to send this subscriber now, and nothing where there is nothing it has not seen. It is
    /// given this subscriber's own <see cref="Sent"/> and moves it.
    /// <para>
    /// Several rather than one, because one change can be two things to say: a record that grew
    /// while entries of it were also lost is an <c>append</c> and a <c>missing</c>, and a chat that
    /// gained a question whose run then started is two increments. Returning one would make the
    /// second wait for a change that may never come.
    /// </para>
    /// </param>
    /// <remarks>
    /// The subscription is registered <b>before</b> the snapshot is taken, so a change between the
    /// two wakes this subscriber and is worked out again rather than falling in the gap. The cost is
    /// that a subscriber can be woken for a change its snapshot already carried, and
    /// <paramref name="next"/> answering with nothing for that is what makes it cost nothing.
    /// </remarks>
    public async IAsyncEnumerable<T> Watch<T>(
        string topic,
        Func<Sent, IEnumerable<T>> opening,
        Func<Sent, IEnumerable<T>> next,
        [EnumeratorCancellation] CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(opening);
        ArgumentNullException.ThrowIfNull(next);

        var channel = Channel.CreateUnbounded<byte>();
        var sent = new Sent();

        Joined(topic, channel);

        try
        {
            foreach (var first in opening(sent))
            {
                yield return first;
            }

            while (await channel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                // Everything that is queued, read before anything is made of it: several changes
                // while this subscriber was away are one thing to send it, not one each.
                while (channel.Reader.TryRead(out _))
                {
                }

                foreach (var increment in next(sent))
                {
                    yield return increment;
                }
            }
        }
        finally
        {
            // The page closed, or the host cancelled. Left on the list, the subscriber would be
            // written to for the life of the process — a leak the owner would read as memory growing
            // every time a tab is opened.
            Left(topic, channel);
        }
    }

    private void Joined(string topic, Channel<byte> channel)
    {
        lock (gate)
        {
            if (!watchers.TryGetValue(topic, out var subscribers))
            {
                subscribers = [];
                watchers[topic] = subscribers;
            }

            subscribers.Add(channel);
        }
    }

    private void Left(string topic, Channel<byte> channel)
    {
        lock (gate)
        {
            if (!watchers.TryGetValue(topic, out var subscribers))
            {
                return;
            }

            subscribers.Remove(channel);

            // The last reader of a run's record gone means that topic is gone: a run whose page is
            // never opened again would otherwise leave an entry behind for every run Grimoire has
            // ever made.
            if (subscribers.Count == 0)
            {
                watchers.Remove(topic);
            }
        }
    }
}
