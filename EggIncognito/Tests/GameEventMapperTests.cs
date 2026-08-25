using EggIncognito.Models.Events;
using EggIncognito.Services.Events;
using Ei;

namespace EggIncognito.Tests;

public class GameEventMapperTests {
    private static PeriodicalsResponse Response(params EggIncEvent[] events) {
        var response = new PeriodicalsResponse { Events = new EggIncCurrentEvents() };
        response.Events.Events.AddRange(events);
        return response;
    }

    [Fact]
    public void FromPeriodicals_MapsFields() {
        var seenAt = DateTimeOffset.UtcNow;
        var observations = GameEventMapper.FromPeriodicals(Response(new EggIncEvent {
            Identifier = "piggy-cap-boost",
            Type = "piggy-boost",
            Subtitle = "TRIPLE PIGGY GROWTH!!",
            Multiplier = 3,
            StartTime = 1609517288.348589,
            Duration = 265813.673827,
            CcOnly = true
        }), seenAt);
        var obs = Assert.Single(observations);
        Assert.Equal("piggy-cap-boost", obs.EventId);
        Assert.Equal("piggy-boost", obs.EventType);
        Assert.Equal("TRIPLE PIGGY GROWTH!!", obs.Message);
        Assert.Equal(3, obs.Multiplier);
        Assert.True(obs.Ultra);
        Assert.Equal(UnixSeconds.ToTime(1609517288.348589), obs.Start);
        Assert.Equal(obs.Start.AddSeconds(265813.673827), obs.End);
        Assert.Equal(GameEventSources.Device, obs.Source);
        Assert.Equal(seenAt, obs.SeenAt);
    }

    [Fact]
    public void FromPeriodicals_SkipsRowsWithoutIdentifierOrTimes() {
        var observations = GameEventMapper.FromPeriodicals(Response(
            new EggIncEvent { Type = "piggy-boost", StartTime = 100, Duration = 100 },
            new EggIncEvent { Identifier = "x", StartTime = 0, Duration = 100 },
            new EggIncEvent { Identifier = "y", StartTime = 100, Duration = 0 }), DateTimeOffset.UtcNow);
        Assert.Empty(observations);
    }

    [Fact]
    public void FromPeriodicals_NoEventsBlock_ReturnsEmpty() {
        Assert.Empty(GameEventMapper.FromPeriodicals(new PeriodicalsResponse(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void FromCarpet_MapsAndSkips() {
        var observations = GameEventMapper.FromCarpet([
            new CarpetEvent {
                Id = "covid-1-1",
                Type = "piggy-boost",
                Message = "TRIPLE PIGGY GROWTH!!",
                Multiplier = 3,
                Ultra = false,
                StartTimestamp = 1609517288.348589,
                EndTimestamp = 1609783102.0224159
            },
            new CarpetEvent { Id = "", StartTimestamp = 1, EndTimestamp = 2 },
            new CarpetEvent { Id = "bad", StartTimestamp = 5, EndTimestamp = 5 }
        ]);
        var obs = Assert.Single(observations);
        Assert.Equal("covid-1-1", obs.EventId);
        Assert.Equal(GameEventSources.Carpet, obs.Source);
        Assert.Null(obs.SeenAt);
        Assert.Equal(UnixSeconds.ToTime(1609783102.0224159), obs.End);
    }
}
