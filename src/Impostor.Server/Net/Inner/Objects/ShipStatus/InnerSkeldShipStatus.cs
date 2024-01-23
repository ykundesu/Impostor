using System.Collections.Generic;
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
    internal class InnerSkeldShipStatus : InnerShipStatus, IInnerSkeldShipStatus
    {
        public InnerSkeldShipStatus(ICustomMessageManager<ICustomRpc> customMessageManager, IOptions<AntiCheatConfig> antiCheatConfig, Game game) : base(customMessageManager, antiCheatConfig, game, MapTypes.Skeld)
        {
        }

        protected override void AddSystems(Dictionary<SystemTypes, ISystemType> systems)
        {
            base.AddSystems(systems);

            systems.Add(SystemTypes.Doors, new AutoDoorsSystemType(Doors));
            systems.Add(SystemTypes.Comms, new HudOverrideSystemType());
            systems.Add(SystemTypes.Security, new SecurityCameraSystemType());
            systems.Add(SystemTypes.Reactor, new ReactorSystemType());
            systems.Add(SystemTypes.LifeSupp, new LifeSuppSystemType());
        }
    }
}
