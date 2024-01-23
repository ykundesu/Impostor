using System.Collections.Generic;
using System.Numerics;
using Impostor.Api.Config;
using Impostor.Api.Innersloth;
using Impostor.Api.Net.Custom;
using Impostor.Api.Net.Inner.Objects.ShipStatus;
using Impostor.Server.Net.Inner.Objects.Systems;
using Impostor.Server.Net.Inner.Objects.Systems.ShipStatus;
using Impostor.Server.Net.State;
using Microsoft.Extensions.Options;

namespace Impostor.Server.Net.Inner.Objects.ShipStatus
{
    internal class InnerPolusShipStatus : InnerShipStatus, IInnerPolusShipStatus
    {
        public InnerPolusShipStatus(ICustomMessageManager<ICustomRpc> customMessageManager, IOptions<AntiCheatConfig> antiCheatConfig, Game game) : base(customMessageManager, antiCheatConfig, game, MapTypes.Polus)
        {
        }

        public override Vector2 GetSpawnLocation(InnerPlayerControl player, int numPlayers, bool initialSpawn)
        {
            if (initialSpawn)
            {
                return base.GetSpawnLocation(player, numPlayers, initialSpawn);
            }

            var halfPlayers = numPlayers / 2; // floored intentionally
            var spawnId = player.PlayerId % 15;
            if (player.PlayerId < halfPlayers)
            {
                return Data.MeetingSpawnCenter + (new Vector2(0.6f, 0) * spawnId);
            }
            else
            {
                return Data.MeetingSpawnCenter2 + (new Vector2(0.6f, 0) * (spawnId - halfPlayers));
            }
        }

        protected override void AddSystems(Dictionary<SystemTypes, ISystemType> systems)
        {
            base.AddSystems(systems);

            systems.Add(SystemTypes.Doors, new DoorsSystemType(Doors));
            systems.Add(SystemTypes.Comms, new HudOverrideSystemType());
            systems.Add(SystemTypes.Security, new SecurityCameraSystemType());
            systems.Add(SystemTypes.Laboratory, new ReactorSystemType());
        }
    }
}
