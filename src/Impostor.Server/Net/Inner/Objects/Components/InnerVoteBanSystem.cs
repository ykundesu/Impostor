using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Impostor.Api;
using Impostor.Api.Net;
using Impostor.Api.Net.Custom;
using Impostor.Api.Net.Inner;
using Impostor.Api.Net.Inner.Objects;
using Impostor.Api.Net.Messages.Rpcs;
using Impostor.Server.Net.State;
using Microsoft.Extensions.Logging;

namespace Impostor.Server.Net.Inner.Objects.Components
{
    internal class InnerVoteBanSystem : InnerNetObject, IInnerVoteBanSystem
    {
        private readonly ILogger<InnerVoteBanSystem> _logger;
        private readonly Dictionary<int, int[]> _votes;

        public InnerVoteBanSystem(ICustomMessageManager<ICustomRpc> customMessageManager, Game game, ILogger<InnerVoteBanSystem> logger) : base(customMessageManager, game)
        {
            _logger = logger;
            _votes = new Dictionary<int, int[]>();
            Components.Add(this);
        }

        public override ValueTask<bool> SerializeAsync(IMessageWriter writer, bool initialState)
        {
            throw new NotImplementedException();
        }

        public override async ValueTask DeserializeAsync(IClientPlayer sender, IClientPlayer? target, IMessageReader reader, bool initialState)
        {
            if (!await ValidateHost(CheatContext.Deserialize, sender))
            {
                return;
            }

            var votes = _votes;
            var unknown = reader.ReadByte();
            if (unknown != 0)
            {
                for (var i = 0; i < unknown; i++)
                {
                    var v4 = reader.ReadInt32();
                    if (v4 == 0)
                    {
                        break;
                    }

                    if (!votes.TryGetValue(v4, out var v12))
                    {
                        v12 = new int[3];
                        votes[v4] = v12;
                    }

                    for (var j = 0; j < 3; j++)
                    {
                        v12[j] = reader.ReadPackedInt32();
                    }
                }
            }
        }

        public override async ValueTask<bool> HandleRpcAsync(ClientPlayer sender, ClientPlayer? target, RpcCalls call, IMessageReader reader)
        {
            if (call == RpcCalls.AddVote)
            {
                Rpc26AddVote.Deserialize(reader, out var clientId, out var targetClientId);

                var actualSender = GetPlayerIdentity(sender.Client.Id);
                var claimedVoter = GetPlayerIdentity(clientId);
                var voteTarget = GetPlayerIdentity(targetClientId);

                if (clientId != sender.Client.Id)
                {
                    _logger.LogWarning(
                        "VoteBan AddVote spoof: actualSenderId={ActualSenderId} actualSenderName={ActualSenderName} actualSenderFriendCode={ActualSenderFriendCode} actualSenderPuid={ActualSenderPuid} claimedVoterId={ClaimedVoterId} claimedVoterName={ClaimedVoterName} claimedVoterFriendCode={ClaimedVoterFriendCode} claimedVoterPuid={ClaimedVoterPuid} targetId={TargetId} targetName={TargetName} targetFriendCode={TargetFriendCode} targetPuid={TargetPuid}",
                        actualSender.ClientId,
                        actualSender.Name,
                        actualSender.FriendCode,
                        actualSender.ProductUserId,
                        claimedVoter.ClientId,
                        claimedVoter.Name,
                        claimedVoter.FriendCode,
                        claimedVoter.ProductUserId,
                        voteTarget.ClientId,
                        voteTarget.Name,
                        voteTarget.FriendCode,
                        voteTarget.ProductUserId);

                    if (await sender.Client.ReportCheatAsync(RpcCalls.AddVote, CheatCategory.VoteBanOwnership, $"Client sent {nameof(RpcCalls.AddVote)} as other client"))
                    {
                        return false;
                    }
                }
                else
                {
                    _logger.LogInformation(
                        "VoteBan AddVote: voterId={VoterId} voterName={VoterName} voterFriendCode={VoterFriendCode} voterPuid={VoterPuid} targetId={TargetId} targetName={TargetName} targetFriendCode={TargetFriendCode} targetPuid={TargetPuid}",
                        claimedVoter.ClientId,
                        claimedVoter.Name,
                        claimedVoter.FriendCode,
                        claimedVoter.ProductUserId,
                        voteTarget.ClientId,
                        voteTarget.Name,
                        voteTarget.FriendCode,
                        voteTarget.ProductUserId);
                }

                return true;
            }

            return await base.HandleRpcAsync(sender, target, call, reader);
        }

        private (int ClientId, string Name, string FriendCode, string ProductUserId) GetPlayerIdentity(int clientId)
        {
            if (!Game.TryGetPlayer(clientId, out var player))
            {
                return (clientId, "<unknown>", "<unknown>", "<unknown>");
            }

            var playerInfo = player.Character?.PlayerInfo;
            var name = string.IsNullOrWhiteSpace(playerInfo?.PlayerName) ? player.Client.Name : playerInfo.PlayerName;
            var friendCode = string.IsNullOrWhiteSpace(playerInfo?.FriendCode) ? "<unknown>" : playerInfo.FriendCode;
            var productUserId = string.IsNullOrWhiteSpace(playerInfo?.ProductUserId) ? "<unknown>" : playerInfo.ProductUserId;

            return (player.Client.Id, name, friendCode, productUserId);
        }
    }
}
