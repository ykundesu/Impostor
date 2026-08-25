using System;
using System.Text.Json.Serialization;

namespace Impostor.Api.Innersloth.GameFilters
{
    [Serializable]
    public class ModGameFilter : ISubFilter
    {
        [JsonPropertyName("FilterType")]
        public string FilterType { get; } = "mod";

        [JsonPropertyName("AcceptedValues")]
        public Guid AcceptedValues { get; set; }
    }
}
