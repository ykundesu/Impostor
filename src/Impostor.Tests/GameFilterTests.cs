using System;
using System.Text.Json;
using Impostor.Api.Innersloth.GameFilters;
using Xunit;

namespace Impostor.Tests;

public sealed class GameFilterTests
{
    [Fact]
    public void Deserialize_AcceptsModFilter()
    {
        const string modRegistrationGuid = "c8b09b38-aa55-4b3a-b325-88fbbe36fd9b";
        const string filterJson =
            """
            {
              "FilterSets": [
                {
                  "GameMode": 0,
                  "Filters": [
                    {
                      "OptionType": "mod",
                      "Key": "mod",
                      "SubFilterString": "{\"FilterType\":\"mod\",\"AcceptedValues\":\"c8b09b38-aa55-4b3a-b325-88fbbe36fd9b\"}"
                    }
                  ]
                }
              ]
            }
            """;

        var filters = JsonSerializer.Deserialize<GameFiltersList>(filterJson);

        var filterSet = Assert.Single(filters!.FilterSets);
        var filter = Assert.Single(filterSet.Filters);
        var modFilter = Assert.IsType<ModGameFilter>(filter.SubFilter);
        Assert.Equal(Guid.Parse(modRegistrationGuid), modFilter.AcceptedValues);
    }
}
