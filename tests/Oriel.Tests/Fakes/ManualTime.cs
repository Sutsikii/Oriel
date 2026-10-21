namespace Oriel.Tests.Fakes;

/// <summary>A clock that moves only when told to.</summary>
internal sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
