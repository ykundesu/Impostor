using System;
using Impostor.Api.Innersloth;
using Impostor.Api.Innersloth.GameOptions;
using Impostor.Api.Net.Messages;
using Impostor.Api.Net.Messages.C2S;
using Impostor.Hazel;
using Impostor.Hazel.Abstractions;
using Impostor.Hazel.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;
using Xunit;

namespace Impostor.Tests;

public sealed class HostModdedGameTests
{
    [Fact]
    public void Deserialize_ConsumesModRegistrationGuid()
    {
        var gameOptions = new NormalGameOptions
        {
            MaxPlayers = 10,
            NumImpostors = 2,
        };
        var gameFilterOptions = GameFilterOptions.CreateDefault();
        var modRegistrationGuid = Guid.Parse("c8b09b38-aa55-4b3a-b325-88fbbe36fd9b");

        using var serviceProvider = CreateServiceProvider();
        var readerPool = serviceProvider.GetRequiredService<ObjectPool<MessageReader>>();
        using var writer = MessageWriter.Get(MessageType.Reliable);
        writer.StartMessage(MessageFlags.HostModdedGame);
        GameOptionsFactory.Serialize(writer, gameOptions);
        writer.Write((int)CrossplayFlags.All);
        gameFilterOptions.Serialize(writer);
        writer.Write(modRegistrationGuid.ToByteArray());
        writer.EndMessage();

        using var rootMessage = readerPool.Get();
        rootMessage.Update(writer.ToByteArray(includeHeader: false));
        using var message = (MessageReader)rootMessage.ReadMessage();
        Message25HostModdedGameC2S.Deserialize(message, out var parsedGameOptions, out var parsedCrossplayFlags, out var parsedGameFilterOptions, out var parsedModRegistrationGuid);

        var parsedNormalGameOptions = Assert.IsType<NormalGameOptions>(parsedGameOptions);
        Assert.Equal(gameOptions.MaxPlayers, parsedNormalGameOptions.MaxPlayers);
        Assert.Equal(gameOptions.NumImpostors, parsedNormalGameOptions.NumImpostors);
        Assert.Equal(CrossplayFlags.All, parsedCrossplayFlags);
        Assert.True(parsedGameFilterOptions.FilterTags.SetEquals(gameFilterOptions.FilterTags));
        Assert.Equal(modRegistrationGuid, parsedModRegistrationGuid);
        Assert.Equal(0, message.BytesRemaining);
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddHazel();
        return services.BuildServiceProvider();
    }
}
