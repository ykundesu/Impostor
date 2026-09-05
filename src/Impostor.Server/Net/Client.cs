using System;
using System.Linq;
using System.Threading.Tasks;
using Impostor.Api;
using Impostor.Api.Config;
using Impostor.Api.Games;
using Impostor.Api.Innersloth;
using Impostor.Api.Innersloth.GameOptions;
using Impostor.Api.Net;
using Impostor.Api.Net.Custom;
using Impostor.Api.Net.Messages;
using Impostor.Api.Net.Messages.C2S;
using Impostor.Api.Net.Messages.S2C;
using Impostor.Hazel;
using Impostor.Server.Net.Manager;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Impostor.Server.Net
{
    internal class Client : ClientBase
    {
        private readonly ILogger<Client> _logger;
        private readonly AntiCheatConfig _antiCheatConfig;
        private readonly ClientManager _clientManager;
        private readonly GameManager _gameManager;
        private readonly ICustomMessageManager<ICustomRootMessage> _customMessageManager;

        public Client(ILogger<Client> logger, IOptions<AntiCheatConfig> antiCheatOptions, ClientManager clientManager, GameManager gameManager, ICustomMessageManager<ICustomRootMessage> customMessageManager, string name, GameVersion gameVersion, Language language, QuickChatModes chatMode, PlatformSpecificData platformSpecificData, IHazelConnection connection)
            : base(name, gameVersion, language, chatMode, platformSpecificData, connection)
        {
            _logger = logger;
            _antiCheatConfig = antiCheatOptions.Value;
            _clientManager = clientManager;
            _gameManager = gameManager;
            _customMessageManager = customMessageManager;
        }

        public override async ValueTask<bool> ReportCheatAsync(CheatContext context, CheatCategory category, string message)
        {
            if (!_antiCheatConfig.Enabled)
            {
                return false;
            }

            if (Player != null && Player.Game.ModGuid != null)
            {
                return false;
            }

            if (Player != null && Player.IsHost)
            {
                var isHostCheatingAllowed = _antiCheatConfig.AllowCheatingHosts switch {
                    CheatingHostMode.Always => true,
                    CheatingHostMode.IfRequested => GameVersion.HasDisableServerAuthorityFlag,
                    CheatingHostMode.Never => false,
                    _ => false,
                };

                if (isHostCheatingAllowed)
                {
                    return false;
                }
            }

            bool LogUnknownCategory(CheatCategory category)
            {
                _logger.LogWarning("Unknown cheat category {Category} was used when reporting", category);
                return true;
            }

            var isCategoryEnabled = category switch
            {
                CheatCategory.ProtocolExtension => _antiCheatConfig.ForbidProtocolExtensions,
                CheatCategory.GameFlow => _antiCheatConfig.EnableGameFlowChecks,
                CheatCategory.InvalidObject => _antiCheatConfig.EnableInvalidObjectChecks,
                CheatCategory.MustBeHost => _antiCheatConfig.EnableMustBeHostChecks,
                CheatCategory.ColorLimits => _antiCheatConfig.EnableColorLimitChecks,
                CheatCategory.NameLimits => _antiCheatConfig.EnableNameLimitChecks,
                CheatCategory.Ownership => _antiCheatConfig.EnableOwnershipChecks,
                CheatCategory.VoteBanOwnership => _antiCheatConfig.EnableVoteBanOwnershipChecks,
                CheatCategory.Role => _antiCheatConfig.EnableRoleChecks,
                CheatCategory.Target => _antiCheatConfig.EnableTargetChecks,
                CheatCategory.HostOnlyExtension => _antiCheatConfig.AllowHostOnlyExtensions switch {
                    CheatingHostMode.Always => false,
                    CheatingHostMode.IfRequested => !GameVersion.HasDisableServerAuthorityFlag,
                    CheatingHostMode.Never => true,
                    _ => true,
                },
                CheatCategory.PacketSize => _antiCheatConfig.EnablePacketSizeChecks,
                CheatCategory.Other => true,
                _ => LogUnknownCategory(category),
            };

            if (!isCategoryEnabled)
            {
                return false;
            }

            var supportCode = Random.Shared.Next(0, 999_999).ToString("000-000");

            _logger.LogWarning("Client {Name} ({Id}) was caught cheating: [{SupportCode}] [{Context}-{Category}] {Message}", Name, Id, supportCode, context.Name, category, message);

            if (Player is { } player)
            {
                if (_antiCheatConfig.BanIpFromGame)
                {
                    player.Game.BanIp(Connection.GetEffectiveEndPoint().Address);
                }

                await player.Game.HandleRemovePlayer(Id, DisconnectReason.Hacking);
            }

            var disconnectMessage =
                $"""
                 You have been caught cheating and were {(_antiCheatConfig.BanIpFromGame ? "banned" : "kicked")} from the lobby.
                 For questions, contact your server admin and share the following code: {supportCode}.
                 """;

            await DisconnectWithReasonDetailAsync(
                DisconnectReason.Custom,
                $"Anticheat enforcement: [{context.Name}-{category}] {message}",
                disconnectMessage);

            return true;
        }

        public override async ValueTask HandleMessageAsync(IMessageReader reader, MessageType messageType)
        {
            var flag = reader.Tag;

            _logger.LogTrace("[{0}] Server got {1}.", Id, MessageFlags.FlagToString(flag));

            switch (flag)
            {
                case MessageFlags.HostGame:
                case MessageFlags.HostModdedGame:
                {
                    IGameOptions gameOptions;
                    GameFilterOptions gameFilterOptions;
                    Guid? modGuid = null;

                    try
                    {
                        if (flag == MessageFlags.HostModdedGame)
                        {
                            Message25HostModdedGameC2S.Deserialize(reader, out gameOptions, out _, out gameFilterOptions, out var parsedModGuid);
                            modGuid = parsedModGuid;
                        }
                        else
                        {
                            Message00HostGameC2S.Deserialize(reader, out gameOptions, out _, out gameFilterOptions);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Client {Name} ({Id}) sent an invalid {Flag} packet, disconnecting",
                            Name,
                            Id,
                            MessageFlags.FlagToString(flag));
                        await DisconnectAsync(DisconnectReason.Custom, "The server could not parse the host game request.");
                        return;
                    }

                    IGame? game;
                    try
                    {
                        game = await _gameManager.CreateAsync(this, gameOptions, gameFilterOptions, modGuid);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Client {Name} ({Id}) failed to create a game", Name, Id);
                        await DisconnectAsync(DisconnectReason.ServerError);
                        return;
                    }

                    if (game == null)
                    {
                        await DisconnectAsync(DisconnectReason.GameNotFound);
                        return;
                    }

                    _logger.LogInformation(
                        "Client {Name} ({Id}) created game {Code} via {Flag}",
                        Name,
                        Id,
                        game.Code,
                        MessageFlags.FlagToString(flag));

                    if (modGuid != null)
                    {
                        _logger.LogInformation("Client {Name} ({Id}) hosted a modded game with mod GUID {ModGuid}.", Name, Id, modGuid);
                    }

                    // Code in the packet below will be used in JoinGame.
                    using (var writer = MessageWriter.Get(MessageType.Reliable))
                    {
                        Message00HostGameS2C.Serialize(writer, game.Code);
                        await Connection.SendAsync(writer);
                    }

                    break;
                }

                case MessageFlags.JoinGame:
                {
                    Message01JoinGameC2S.Deserialize(reader, out var gameCode);

                    var game = _gameManager.Find(gameCode);
                    if (game == null)
                    {
                        await DisconnectAsync(DisconnectReason.GameNotFound);
                        return;
                    }

                    var result = await game.AddClientAsync(this);

                    switch (result.Error)
                    {
                        case GameJoinError.None:
                            break;
                        case GameJoinError.InvalidClient:
                            await DisconnectAsync(DisconnectReason.Custom, "Client is in an invalid state.");
                            break;
                        case GameJoinError.Banned:
                            await DisconnectWithReasonDetailAsync(
                                DisconnectReason.Banned,
                                GetPendingDisconnectDetail(DisconnectReason.Banned) ?? "Join denied: player is banned from this lobby.");
                            break;
                        case GameJoinError.GameFull:
                            await DisconnectAsync(DisconnectReason.GameFull);
                            break;
                        case GameJoinError.InvalidLimbo:
                            await DisconnectAsync(DisconnectReason.Custom, "Invalid limbo state while joining.");
                            break;
                        case GameJoinError.GameStarted:
                            await DisconnectAsync(DisconnectReason.GameStarted);
                            break;
                        case GameJoinError.GameDestroyed:
                            await DisconnectAsync(DisconnectReason.Custom, DisconnectMessages.Destroyed);
                            break;
                        case GameJoinError.ClientOutdated:
                            await DisconnectAsync(DisconnectReason.Custom, DisconnectMessages.ClientOutdated);
                            break;
                        case GameJoinError.ClientTooNew:
                            await DisconnectAsync(DisconnectReason.Custom, DisconnectMessages.ClientTooNew);
                            break;
                        case GameJoinError.Custom:
                            await DisconnectAsync(DisconnectReason.Custom, result.Message);
                            break;
                        default:
                            await DisconnectAsync(DisconnectReason.Custom, "Unknown error.");
                            break;
                    }

                    break;
                }

                case MessageFlags.StartGame:
                {
                    if (!IsPacketAllowed(reader, true, flag))
                    {
                        return;
                    }

                    await Player!.Game.HandleStartGame(reader);
                    break;
                }

                // No idea how this flag is triggered.
                case MessageFlags.RemoveGame:
                    break;

                case MessageFlags.RemovePlayer:
                {
                    if (!IsPacketAllowed(reader, true, flag))
                    {
                        return;
                    }

                    Message04RemovePlayerC2S.Deserialize(
                        reader,
                        out var playerId,
                        out var reason);

                    await Player!.Game.HandleRemovePlayer(playerId, (DisconnectReason)reason);
                    break;
                }

                case MessageFlags.GameData:
                case MessageFlags.GameDataTo:
                {
                    if (!IsPacketAllowed(reader, false, flag))
                    {
                        return;
                    }

                    var toPlayer = flag == MessageFlags.GameDataTo;

                    var position = reader.Position;
                    var verified = await Player!.Game.HandleGameDataAsync(reader, Player, toPlayer);
                    reader.Seek(position);

                    if (verified && Player != null)
                    {
                        // Broadcast packet to all other players.
                        using (var writer = MessageWriter.Get(messageType))
                        {
                            if (toPlayer)
                            {
                                var target = reader.ReadPackedInt32();
                                reader.CopyTo(writer);
                                await Player.Game.SendToAsync(writer, target);
                            }
                            else
                            {
                                reader.CopyTo(writer);
                                await Player.Game.SendToAllExceptAsync(writer, Id);
                            }
                        }
                    }

                    break;
                }

                case MessageFlags.PackedGameDataTo:
                {
                    if (Player == null)
                    {
                        return;
                    }

                    var game = Player.Game;

                    // Innersloth Special: this message uses PackedInt32 instead of a normal int32
                    var code = reader.ReadPackedInt32();

                    if (code != game.Code.Value)
                    {
                        _logger.LogWarning("gcm2 {0} {1}", code, game.Code.Value);
                        return;
                    }

                    // We're limiting this to hosts right now. If you have a use case for this for
                    // players to use this feature, we're open to changing this.
                    if (game.HostId != Id)
                    {
                        await ReportCheatAsync(
                            new CheatContext(MessageFlags.FlagToString(flag)),
                            CheatCategory.MustBeHost,
                            "Client sent a PackedGameDataTo message");
                        return;
                    }

                    if (await ReportCheatAsync(
                        new CheatContext(MessageFlags.FlagToString(flag)),
                        CheatCategory.HostOnlyExtension,
                        "Client sent a PackedGameDataTo message"))
                    {
                        return;
                    }

                    while (reader.Position < reader.Length)
                    {
                        using var packed = reader.ReadMessage();

                        if (packed.Tag != MessageFlags.GameDataTo)
                        {
                            _logger.LogWarning("PackedGameDataTo contained non-GameDataTo flag {0}.", packed.Tag);
                            return;
                        }

                        if (packed.ReadInt32() != game.Code.Value)
                        {
                            _logger.LogWarning("PackedGameDataTo contained GameDataTo for the wrong game.");
                            return;
                        }

                        var position = packed.Position;
                        var verified = await Player.Game.HandleGameDataAsync(packed, Player, true);
                        packed.Seek(position);

                        if (!verified || Player == null)
                        {
                            return;
                        }

                        using var writer = MessageWriter.Get(messageType);
                        var target = packed.ReadPackedInt32();
                        packed.CopyTo(writer);
                        await Player.Game.SendToAsync(writer, target);
                    }

                    break;
                }

                case MessageFlags.EndGame:
                {
                    if (!IsPacketAllowed(reader, true, flag))
                    {
                        return;
                    }

                    Message08EndGameC2S.Deserialize(
                        reader,
                        out var gameOverReason);

                    await Player!.Game.HandleEndGame(reader, gameOverReason);
                    break;
                }

                case MessageFlags.AlterGame:
                {
                    if (!IsPacketAllowed(reader, true, flag))
                    {
                        return;
                    }

                    Message10AlterGameC2S.Deserialize(
                        reader,
                        out var gameTag,
                        out var value);

                    if (gameTag != AlterGameTags.ChangePrivacy)
                    {
                        return;
                    }

                    await Player!.Game.HandleAlterGame(reader, Player, value);
                    break;
                }

                case MessageFlags.KickPlayer:
                {
                    if (!IsPacketAllowed(reader, true, flag))
                    {
                        return;
                    }

                    Message11KickPlayerC2S.Deserialize(
                        reader,
                        out var playerId,
                        out var isBan);

                    await Player!.Game.HandleKickPlayer(playerId, isBan, Player);
                    break;
                }

                case MessageFlags.GetGameListV2:
                {
                    await DisconnectAsync(DisconnectReason.Custom, DisconnectMessages.UdpMatchmakingUnsupported);
                    return;
                }

                case MessageFlags.SetActivePodType:
                {
                    Message21SetActivePodType.Deserialize(reader, out _);
                    break;
                }

                case MessageFlags.QueryPlatformIds:
                {
                    Message22QueryPlatformIdsC2S.Deserialize(reader, out var gameCode);
                    await OnQueryPlatformIds(gameCode);
                    break;
                }

                default:
                    if (_customMessageManager.TryGet(flag, out var customRootMessage))
                    {
                        await customRootMessage.HandleMessageAsync(this, reader, messageType);
                        break;
                    }

                    _logger.LogWarning("Server received unknown flag {0}.", flag);
                    break;
            }

#if DEBUG
            if (flag != MessageFlags.GameData &&
                flag != MessageFlags.GameDataTo &&
                flag != MessageFlags.PackedGameDataTo &&
                flag != MessageFlags.EndGame &&
                reader.Position < reader.Length)
            {
                _logger.LogWarning(
                    "Server did not consume all bytes from {0} ({1} < {2}).",
                    flag,
                    reader.Position,
                    reader.Length);
            }
#endif
        }

        public override async ValueTask HandleDisconnectAsync(string reason)
        {
            var disconnectContext = ConsumePendingDisconnectContext();
            var expectedReason = disconnectContext.Reason?.ToString();
            var detail = expectedReason == reason ? disconnectContext.Detail : null;
            var customReason = expectedReason == reason && disconnectContext.Reason == DisconnectReason.Custom
                ? disconnectContext.CustomMessage
                : null;
            var isRemote = reason == "The remote sent a disconnect request";
            var remotePayload = ConsumeRemoteDisconnectPayload();
            var gameCode = Player?.Game.Code.ToString();

            try
            {
                if (Player != null)
                {
                    // Among Us does send a disconnect reason in the Hazel payload, but older Impostor
                    // builds ignored it and treated every remote disconnect as ExitGame.
                    await Player.Game.HandleRemovePlayer(Id, isRemote ? DisconnectReason.ExitGame : DisconnectReason.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception caught in client disconnection.");
            }

            if (isRemote && remotePayload.ParseFailed)
            {
                _logger.LogInformation(
                    "Client {Name} ({Id}) disconnecting, reason: {Reason}, game: {Game}, amongUsPayload: unparsable ({PayloadLength} bytes)",
                    Name,
                    Id,
                    reason,
                    gameCode ?? "(none)",
                    remotePayload.PayloadLength);
            }
            else if (isRemote && (remotePayload.HasPayload || remotePayload.PayloadLength > 0))
            {
                _logger.LogInformation(
                    "Client {Name} ({Id}) disconnecting, reason: {Reason}, game: {Game}, amongUsReason: {AmongUsReason}, customMessage: {CustomMessage}",
                    Name,
                    Id,
                    reason,
                    gameCode ?? "(none)",
                    remotePayload.HasPayload ? remotePayload.Reason?.ToString() ?? "(null)" : "(none)",
                    remotePayload.CustomMessage);
            }
            else if (!string.IsNullOrWhiteSpace(detail) && !string.IsNullOrWhiteSpace(customReason))
            {
                _logger.LogInformation("Client {Name} ({Id}) disconnecting, reason: {Reason}, game: {Game}, detail: {Detail}, custom reason: {CustomReason}", Name, Id, reason, gameCode ?? "(none)", detail, customReason);
            }
            else if (!string.IsNullOrWhiteSpace(detail))
            {
                _logger.LogInformation("Client {Name} ({Id}) disconnecting, reason: {Reason}, game: {Game}, detail: {Detail}", Name, Id, reason, gameCode ?? "(none)", detail);
            }
            else if (!string.IsNullOrWhiteSpace(customReason))
            {
                _logger.LogInformation("Client {Name} ({Id}) disconnecting, reason: {Reason}, game: {Game}, custom reason: {CustomReason}", Name, Id, reason, gameCode ?? "(none)", customReason);
            }
            else
            {
                _logger.LogInformation("Client {Name} ({Id}) disconnecting, reason: {Reason}, game: {Game}", Name, Id, reason, gameCode ?? "(none)");
            }

            _clientManager.Remove(this);
            await _gameManager.OnClientDisconnectAsync(this);
        }

        private bool IsPacketAllowed(IMessageReader message, bool hostOnly, byte flag)
        {
            if (Player == null)
            {
                return false;
            }

            var game = Player.Game;

            // GameCode must match code of the current game assigned to the player.
            var code = message.ReadInt32();
            if (code != game.Code.Value)
            {
                return false;
            }

            // Some packets should only be sent by the host of the game.
            if (hostOnly)
            {
                if (game.HostId == Id)
                {
                    return true;
                }

                _logger.LogWarning(
                    "[{0}] Client sent packet {1} only allowed by the host ({2}).",
                    Id,
                    MessageFlags.FlagToString(flag),
                    game.HostId);
                return false;
            }

            return true;
        }

        /// <summary>
        ///     Triggered when the connected client requests the PlatformSpecificData.
        /// </summary>
        /// <param name="code">
        ///     The GameCode of the game whose platform id's are checked.
        /// </param>
        private ValueTask OnQueryPlatformIds(GameCode code)
        {
            using var message = MessageWriter.Get(MessageType.Reliable);

            var playerSpecificData = _gameManager.Find(code)?.Players.Select(p => p.Client.PlatformSpecificData) ?? Enumerable.Empty<PlatformSpecificData>();

            Message22QueryPlatformIdsS2C.Serialize(message, code, playerSpecificData);

            return Connection.SendAsync(message);
        }
    }
}
