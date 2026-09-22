using Microsoft.Extensions.Time.Testing;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What every test in the Fast suite shares.
/// </summary>
/// <remarks>
/// The conventions a new test here follows — the <c>level</c> and <c>req</c> traits
/// <c>trace-check</c> reads, and how a test is named — are written down once, in
/// <c>tests/README.md</c>.
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
