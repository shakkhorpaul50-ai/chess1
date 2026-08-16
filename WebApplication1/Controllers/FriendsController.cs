using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    [Authorize]
    public class FriendsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public FriendsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var me = await _userManager.GetUserAsync(User);

            var accepted = await _db.Friends
                .Include(f => f.Requester)
                .Include(f => f.Addressee)
                .Where(f => f.Status == "Accepted" && (f.RequesterId == me!.Id || f.AddresseeId == me.Id))
                .OrderBy(f => f.CreatedAt)
                .ToListAsync();

            var requests = await _db.Friends
                .Include(f => f.Requester)
                .Where(f => f.Status == "Pending" && f.AddresseeId == me!.Id)
                .OrderByDescending(f => f.CreatedAt)
                .ToListAsync();

            var model = new FriendsViewModel
            {
                Friends = accepted.Select(f =>
                {
                    var other = f.RequesterId == me!.Id ? f.Addressee! : f.Requester!;
                    return new FriendItem { Id = f.Id, UserId = other.Id, Username = other.UserName ?? "?", Elo = other.Elo, Since = f.CreatedAt };
                }).ToList(),
                Requests = requests.Select(f => new FriendItem { Id = f.Id, UserId = f.Requester!.Id, Username = f.Requester.UserName ?? "?", Elo = f.Requester.Elo, Since = f.CreatedAt }).ToList()
            };
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Search(string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return Json(Array.Empty<object>());
            var me = await _userManager.GetUserAsync(User);
            var users = await _db.Users
                .Where(u => u.Id != me!.Id && u.UserName!.Contains(q))
                .OrderBy(u => u.UserName)
                .Take(20)
                .ToListAsync();
            return Json(users.Select(u => new
            {
                u.Id,
                u.UserName,
                u.Elo,
                isFriend = _db.Friends.Any(f => f.Status == "Accepted" &&
                    ((f.RequesterId == me!.Id && f.AddresseeId == u.Id) || (f.RequesterId == u.Id && f.AddresseeId == me.Id)))
            }));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(string userId)
        {
            var me = await _userManager.GetUserAsync(User);
            if (userId == me!.Id) return RedirectToAction(nameof(Index));
            var target = await _db.Users.FindAsync(userId);
            if (target == null) return NotFound();

            var exists = await _db.Friends.AnyAsync(f =>
                (f.RequesterId == me.Id && f.AddresseeId == userId) ||
                (f.RequesterId == userId && f.AddresseeId == me.Id));
            if (!exists)
            {
                _db.Friends.Add(new FriendRecord { RequesterId = me.Id, AddresseeId = userId, Status = "Pending" });
                await _db.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Accept(Guid id)
        {
            var me = await _userManager.GetUserAsync(User);
            var friend = await _db.Friends.FirstOrDefaultAsync(f => f.Id == id && f.AddresseeId == me!.Id && f.Status == "Pending");
            if (friend != null)
            {
                friend.Status = "Accepted";
                await _db.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(Guid id)
        {
            var me = await _userManager.GetUserAsync(User);
            var friend = await _db.Friends.FirstOrDefaultAsync(f => f.Id == id && f.AddresseeId == me!.Id && f.Status == "Pending");
            if (friend != null)
            {
                _db.Friends.Remove(friend);
                await _db.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Remove(Guid id)
        {
            var me = await _userManager.GetUserAsync(User);
            var friend = await _db.Friends.FirstOrDefaultAsync(f => f.Id == id &&
                (f.RequesterId == me!.Id || f.AddresseeId == me.Id));
            if (friend != null)
            {
                _db.Friends.Remove(friend);
                await _db.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Chat(string userId)
        {
            var me = await _userManager.GetUserAsync(User);
            var other = await _db.Users.FindAsync(userId);
            if (other == null) return NotFound();

            var areFriends = await _db.Friends.AnyAsync(f => f.Status == "Accepted" &&
                ((f.RequesterId == me!.Id && f.AddresseeId == userId) || (f.RequesterId == userId && f.AddresseeId == me.Id)));
            if (!areFriends) return Forbid();

            ViewData["OtherId"] = other.Id;
            ViewData["OtherName"] = other.UserName;
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> ChatData(string userId)
        {
            var me = await _userManager.GetUserAsync(User);
            var messages = await _db.ChatMessages
                .Include(m => m.Sender)
                .Where(m => (m.SenderId == me!.Id && m.ReceiverId == userId) ||
                            (m.SenderId == userId && m.ReceiverId == me.Id))
                .OrderByDescending(m => m.SentAt)
                .Take(100)
                .ToListAsync();
            messages.Reverse();
            return Json(messages.Select(m => new
            {
                m.Id,
                m.SenderId,
                senderName = m.Sender?.UserName,
                m.Content,
                m.SentAt,
                mine = m.SenderId == me!.Id
            }));
        }
    }

    public class FriendsViewModel
    {
        public List<FriendItem> Friends { get; set; } = new();
        public List<FriendItem> Requests { get; set; } = new();
    }

    public class FriendItem
    {
        public Guid Id { get; set; }
        public string UserId { get; set; } = "";
        public string Username { get; set; } = "";
        public int Elo { get; set; }
        public DateTime Since { get; set; }
    }
}
