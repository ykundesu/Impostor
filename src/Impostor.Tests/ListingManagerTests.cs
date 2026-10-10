#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Impostor.Api.Config;
using Impostor.Api.Games;
using Impostor.Api.Games.Managers;
using Impostor.Api.Http;
using Impostor.Api.Innersloth;
using Impostor.Api.Innersloth.GameFilters;
using Impostor.Api.Innersloth.GameOptions;
using Impostor.Server.Http;
using Impostor.Server.Net.Manager;
using Impostor.Server.Net.State;
using Impostor.Server.Statistics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Impostor.Tests;

public sealed class ListingManagerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FindListingsV2_PublicModdedRoomIsVisibleWithOrWithoutModFilter(bool includeModFilter)
    {
        var game = new Game(
            NullLogger<Game>.Instance, null!, null!, new IPEndPoint(IPAddress.Loopback, 22023),
            new GameCode(123456), new NormalGameOptions { MaxPlayers = 15, NumImpostors = 3, Map = MapTypes.Polus, Keywords = GameKeywords.Japanese },
            GameFilterOptions.CreateDefault(), null!, null!, null!, new RpcTelemetryProvider(),
            Options.Create(new CompatibilityConfig()), Options.Create(new TimeoutConfig()),
            Guid.Parse("9f21dfdc-1224-4322-af7b-63db54cea706"));
        var manager = new ListingManager(
            new TestGameManager(game), Array.Empty<IListingFilter>(),
            new CompatibilityManager(NullLogger<CompatibilityManager>.Instance),
            Options.Create(new CompatibilityConfig()), Options.Create(new HttpServerConfig()));
        var context = new DefaultHttpContext();
        // The filter captured from an SNR client's actual HTTP lobby search.
        var filters = JsonSerializer.Deserialize<GameFiltersList>(
            """
            {"FilterSets":[{"GameMode":1,"Filters":[
              {"OptionType":"chat","Key":"Chat","SubFilterString":"{\"AcceptedValues\":1,\"FilterType\":\"chat\"}"},
              {"OptionType":"languages","Key":"Language","SubFilterString":"{\"AcceptedValues\":512,\"FilterType\":\"languages\"}"}
            ]}]}
            """)!;
        if (includeModFilter)
        {
            filters.FilterSets[0].Filters.Add(JsonSerializer.Deserialize<GameFilter>(
                """
                {"OptionType":"mod","Key":"mod","SubFilterString":"{\"AcceptedValues\":\"9f21dfdc-1224-4322-af7b-63db54cea706\",\"FilterType\":\"mod\"}"}
                """)!);
        }

        Assert.Empty(manager.FindListingsV2(context, filters));
        await game.SetPrivacyAsync(true);
        Assert.Same(game, Assert.Single(manager.FindListingsV2(context, filters)));

        // Hide-and-seek searches must not return normal rooms.
        filters.FilterSets[0].GameMode = GameModes.HideNSeek;
        Assert.Empty(manager.FindListingsV2(context, filters));
        filters.FilterSets[0].GameMode = GameModes.Normal;

        await game.SetPrivacyAsync(false);
        Assert.Empty(manager.FindListingsV2(context, filters));
    }

    private sealed class TestGameManager : IGameManager
    {
        public TestGameManager(IGame game)
        {
            Games = new[] { game };
        }

        public IEnumerable<IGame> Games { get; }

        public IGame? Find(GameCode code) => throw new NotSupportedException();

        public ValueTask<IGame?> CreateAsync(IGameOptions options, GameFilterOptions filterOptions) => throw new NotSupportedException();
    }
}
