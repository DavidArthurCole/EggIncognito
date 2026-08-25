using EggIncognito.Models.Events;
using EggIncognito.Services.Events;

namespace EggIncognito.Tests;

public class GameEventMergeTests {
    private static readonly DateTimeOffset Start = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private static GameEventObservation Obs(
        string id = "piggy-cap-boost", string source = GameEventSources.Device,
        DateTimeOffset? start = null, DateTimeOffset? end = null, DateTimeOffset? seenAt = null) {
        var s = start ?? Start;
        return new GameEventObservation(id, "piggy-boost", "TRIPLE PIGGY GROWTH!!", 3, false,
            s, end ?? s.AddDays(3), source,
            source == GameEventSources.Device ? seenAt ?? Start.AddHours(1) : null);
    }

    [Fact]
    public void SameOccurrence_WithinWindow() {
        var row = GameEventMerge.Create(Obs());
        Assert.True(GameEventMerge.SameOccurrence(row, Obs(start: Start.AddHours(47))));
        Assert.False(GameEventMerge.SameOccurrence(row, Obs(start: Start.AddHours(49))));
        Assert.False(GameEventMerge.SameOccurrence(row, Obs(id: "other", start: Start)));
    }

    [Fact]
    public void Create_CarpetRow_HasNoSeenTimestamps() {
        var row = GameEventMerge.Create(Obs(source: GameEventSources.Carpet));
        Assert.Equal(GameEventSources.Carpet, row.Source);
        Assert.Null(row.FirstSeenAt);
        Assert.Null(row.LastSeenAt);
    }

    [Fact]
    public void Apply_CarpetNeverOverwritesDevice() {
        var row = GameEventMerge.Create(Obs());
        bool changed = GameEventMerge.Apply(row, Obs(source: GameEventSources.Carpet, end: Start.AddDays(9)));
        Assert.False(changed);
        Assert.Equal(Start.AddDays(3), row.EndTime);
        Assert.Equal(GameEventSources.Device, row.Source);
    }

    [Fact]
    public void Apply_DeviceUpgradesCarpetRow() {
        var row = GameEventMerge.Create(Obs(source: GameEventSources.Carpet));
        bool changed = GameEventMerge.Apply(row, Obs(seenAt: Start.AddHours(2)));
        Assert.True(changed);
        Assert.Equal(GameEventSources.Device, row.Source);
        Assert.Equal(Start.AddHours(2), row.FirstSeenAt);
        Assert.Equal(Start.AddHours(2), row.LastSeenAt);
    }

    [Fact]
    public void Apply_ExtendsEndAndKeepsEarliestStart() {
        var row = GameEventMerge.Create(Obs());
        bool changed = GameEventMerge.Apply(row,
            Obs(start: Start.AddHours(1), end: Start.AddDays(5), seenAt: Start.AddDays(1)));
        Assert.True(changed);
        Assert.Equal(Start, row.StartTime);
        Assert.Equal(Start.AddDays(5), row.EndTime);
        Assert.Equal(Start.AddHours(1), row.FirstSeenAt);
        Assert.Equal(Start.AddDays(1), row.LastSeenAt);
    }

    [Fact]
    public void Apply_SeenAtOnlyWidens() {
        var row = GameEventMerge.Create(Obs(seenAt: Start.AddHours(5)));
        GameEventMerge.Apply(row, Obs(seenAt: Start.AddHours(1)));
        GameEventMerge.Apply(row, Obs(seenAt: Start.AddHours(9)));
        Assert.Equal(Start.AddHours(1), row.FirstSeenAt);
        Assert.Equal(Start.AddHours(9), row.LastSeenAt);
    }

    [Fact]
    public void Apply_CarpetReimportUpdatesCarpetRow() {
        var row = GameEventMerge.Create(Obs(source: GameEventSources.Carpet));
        bool changed = GameEventMerge.Apply(row,
            Obs(source: GameEventSources.Carpet, end: Start.AddDays(4)));
        Assert.True(changed);
        Assert.Equal(Start.AddDays(4), row.EndTime);
        Assert.Equal(GameEventSources.Carpet, row.Source);
    }
}
