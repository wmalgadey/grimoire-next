using Microsoft.Extensions.Time.Testing;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What every test in the Fast suite shares, and the conventions a new test follows.
/// </summary>
/// <remarks>
/// <para>
/// <b>Level.</b> Every test class here carries <c>[Trait("level", "fast")]</c>. The trait goes on
/// the class, not the method: a suite is a level, and a class-level trait reaches every test in it,
/// so a new method cannot be added without one. Constitution III.3 requires the level on every
/// test and <c>trace-check</c> reads it off this assembly's metadata; a test without one — a
/// misspelt value included, since only <c>fast</c>, <c>contract</c>, <c>e2e</c> and <c>deploy</c>
/// count — fails the gate on every push.
/// </para>
/// <para>
/// <b>Requirement id.</b> A test that proves a requirement carries
/// <c>[Trait("req", "&lt;CAPABILITY&gt;-NNN")]</c>, on the class where the whole class proves it
/// and on the method otherwise. The attribute repeats, so a test proving two requirements carries
/// two. The id must be registered in <c>docs/capabilities/</c> (Constitution IV.2): an unknown,
/// retired or reserved one fails the gate. Fast tests may carry the trait and need not
/// (III.5) — the tests of this suite that prove nothing but our own tooling do not.
/// </para>
/// <para>
/// <b>The clock.</b> No test here waits for real time. The suite's whole budget is 15 s
/// (Constitution III.7), and a test that needs time to pass takes <see cref="Clock"/> and moves it
/// itself.
/// </para>
/// </remarks>
internal static class FastSuite
{
    /// <summary>
    /// The instant a Fast clock starts at. A fixed value rather than "now", so a test that reports
    /// an instant reports the same one on every run and on every machine.
    /// </summary>
    internal static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A clock that stands still at <see cref="Start"/> and moves only where a test advances it.
    /// </summary>
    internal static FakeTimeProvider Clock() => new(Start);
}
