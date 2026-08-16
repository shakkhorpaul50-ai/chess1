using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private string UserId => Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        public ChatHub(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public override async Task OnConnectedAsync()
        {
            if (!string.IsNullOrEmpty(UserId))
                await Groups.AddToGroupAsync(Context.ConnectionId, "user:" + UserId);
            await base.OnConnectedAsync();
        }

        public async Task SendGameMessage(Guid gameId, string content)
        {
            content = content.Trim();
            if (content.Length == 0 || content.Length > 500)
                throw new HubException("Message must be 1-500 characters.");

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sender = await db.Users.FindAsync(UserId);
            if (sender == null) throw new HubException("User not found.");

            var msg = new ChatMessageRecord { SenderId = UserId, GameId = gameId, Content = content };
            db.ChatMessages.Add(msg);
            await db.SaveChangesAsync();

            var dto = new ChatMessageDto(msg.Id, msg.SenderId, sender.UserName ?? "?", null, msg.GameId, msg.Content, msg.SentAt);
            await Clients.Group("game:" + gameId).SendAsync("GameMessageReceived", dto);
        }

        public async Task SendPrivate(string receiverUserId, string content)
        {
            content = content.Trim();
            if (content.Length == 0 || content.Length > 500)
                throw new HubException("Message must be 1-500 characters.");

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sender = await db.Users.FindAsync(UserId);
            if (sender == null) throw new HubException("User not found.");

            var friends = await db.Friends.AnyAsync(f =>
                ((f.RequesterId == UserId && f.AddresseeId == receiverUserId) ||
                 (f.RequesterId == receiverUserId && f.AddresseeId == UserId)) && f.Status == "Accepted");
            if (!friends) throw new HubException("You can only message your friends.");

            var msg = new ChatMessageRecord { SenderId = UserId, ReceiverId = receiverUserId, Content = content };
            db.ChatMessages.Add(msg);
            await db.SaveChangesAsync();

            var dto = new ChatMessageDto(msg.Id, msg.SenderId, sender.UserName ?? "?", msg.ReceiverId, null, msg.Content, msg.SentAt);
            await Clients.Group("user:" + receiverUserId).SendAsync("PrivateMessageReceived", dto);
            await Clients.Caller.SendAsync("PrivateMessageReceived", dto);
        }
    }
}
