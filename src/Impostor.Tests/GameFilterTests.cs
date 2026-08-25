using System.Text.Json;
using Impostor.Api.Innersloth.GameFilters;
using Xunit;

namespace Impostor.Tests;

public sealed class GameFilterTests
{
    [Fact]
    public void Deserialize_AcceptsModFilterPayload()
    {
        var filter = DeserializeModFilter("{\"FilterType\":\"mod\",\"AcceptedValues\":\"c8b09b38-aa55-4b3a-b325-88fbbe36fd9b\"}");

        Assert.IsType<ModGameFilter>(filter.SubFilter);
    }

    [Fact]
    public void Deserialize_AcceptsNullModFilterPayloadFromLobbyRequest()
    {
        const string filterJson =
            """
            {
              "FilterSets": [
                {
                  "GameMode": 1,
                  "Filters": [
                    {
                      "OptionType": "mod",
                      "Key": "mod",
                      "SubFilterString": "null"
                    },
                    {
                      "OptionType": "chat",
                      "Key": "Chat",
                      "SubFilterString": "{\"AcceptedValues\":1,\"FilterType\":\"chat\"}"
                    },
                    {
                      "OptionType": "languages",
                      "Key": "Language",
                      "SubFilterString": "{\"AcceptedValues\":256,\"FilterType\":\"languages\"}"
                    }
                  ]
                }
              ]
            }
            """;

        var filters = JsonSerializer.Deserialize<GameFiltersList>(filterJson);

        var filterSet = Assert.Single(filters!.FilterSets);
        Assert.IsType<ModGameFilter>(filterSet.Filters[0].SubFilter);
        Assert.IsType<ChatModeGameFilter>(filterSet.Filters[1].SubFilter);
        Assert.IsType<LanguageFilter>(filterSet.Filters[2].SubFilter);
    }

    private static GameFilter DeserializeModFilter(string subFilterString)
    {
        var filterJson = JsonSerializer.Serialize(new
        {
            OptionType = "mod",
            Key = "mod",
            SubFilterString = subFilterString,
        });

        return JsonSerializer.Deserialize<GameFilter>(filterJson)!;
    }
}
