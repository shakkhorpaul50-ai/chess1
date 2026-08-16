using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using WebApplication1.Models;
using WebApplication1.Services;

namespace WebApplication1.Hubs
{
    [Authorize]
    public class GameHub : Hub
    {
        private readonly GameService _games;
        private string UserId => Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        public GameHub(GameService games)
        {
            _games = games;
        }

        public override async Task OnConnectedAsync()
        {
            _games.RegisterConnection(Context.ConnectionId, UserId);
            if (!string.IsNullOrEmpty(UserId))
                await Groups.AddToGroupAsync(Context.ConnectionId, "user:" + UserId);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _games.UnregisterConnection(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }

        public Task<object> CreateGame(int minutes, bool isRanked, string? invitedUserId = null)
            => _games.CreateGameAsync(UserId, minutes, isRanked, invitedUserId);

        public Task<object> CreateBotGame(int minutes, bool asWhite)
            => _games.CreateBotGameAsync(UserId, minutes, asWhite);

        public Task<object> JoinGame(Guid gameId)
            => _games.JoinGameAsync(UserId, Context.ConnectionId, gameId);

        public Task PlayMove(Guid gameId, string from, string to, string? promotion = null, bool isBot = false)
            => _games.PlayMoveAsync(UserId, Context.ConnectionId, gameId, from, to, promotion, isBot);

        public Task Resign(Guid gameId)
            => _games.ResignAsync(UserId, gameId);

        public Task OfferDraw(Guid gameId)
            => _games.OfferDrawAsync(UserId, gameId);

        public Task AcceptDraw(Guid gameId)
            => _games.AcceptDrawAsync(UserId, gameId);

        public Task DeclineDraw(Guid gameId)
            => _games.DeclineDrawAsync(UserId, gameId);

        public Task CancelGame(Guid gameId)
            => _games.CancelGameAsync(UserId, gameId);

        public Task Spectate(Guid gameId)
            => _games.SpectateAsync(Context.ConnectionId, gameId);

        public Task Leave(Guid gameId)
            => _games.LeaveAsync(Context.ConnectionId, gameId);

        public Task<Guid?> OfferRematch(Guid gameId)
            => _games.OfferRematchAsync(UserId, gameId);

        public Task<Guid> AcceptRematch(Guid gameId)
            => _games.AcceptRematchAsync(UserId, gameId);

        public Task DeclineRematch(Guid gameId)
            => _games.DeclineRematchAsync(UserId, gameId);

        public Task<GameDto?> GetGame(Guid gameId)
            => _games.GetGameAsync(gameId);
    }
}
