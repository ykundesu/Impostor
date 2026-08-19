using System;
using Impostor.Api.Innersloth;
using Impostor.Api.Innersloth.GameOptions;

namespace Impostor.Api.Net.Messages.C2S
{
    public static class Message25HostModdedGameC2S
    {
        public static void Deserialize(IMessageReader reader, out IGameOptions gameOptions, out CrossplayFlags crossplayFlags, out GameFilterOptions gameFilterOptions, out Guid modRegistrationGuid)
        {
            Message00HostGameC2S.Deserialize(reader, out gameOptions, out crossplayFlags, out gameFilterOptions);
            modRegistrationGuid = new Guid(reader.ReadBytes(16).Span);
        }
    }
}
