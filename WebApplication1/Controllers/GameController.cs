using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    [Authorize]
    public class GameController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public GameController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        [HttpGet]
        public IActionResult Play(Guid id)
        {
            ViewData["GameId"] = id;
            return View();
        }

        [HttpGet]
        public IActionResult Bot()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> History()
        {
            var me = await _userManager.GetUserAsync(User);
            var games = await _db.Games
                .Include(g => g.WhitePlayer)
                .Include(g => g.BlackPlayer)
                .Where(g => g.WhitePlayerId == me!.Id || g.BlackPlayerId == me.Id)
                .OrderByDescending(g => g.CreatedAt)
                .Take(100)
                .ToListAsync();
            return View(games);
        }

        [HttpGet]
        public async Task<IActionResult> Replay(Guid id)
        {
            var game = await _db.Games
                .Include(g => g.WhitePlayer)
                .Include(g => g.BlackPlayer)
                .FirstOrDefaultAsync(g => g.Id == id);
            if (game == null) return NotFound();
            return View(game);
        }

        [HttpGet]
        public async Task<IActionResult> Challenges()
        {
            var me = await _userManager.GetUserAsync(User);
            var challenges = await _db.Games
                .Include(g => g.WhitePlayer)
                .Where(g => g.Status == "Waiting" &&
                            (g.BlackPlayerId == null || g.BlackPlayerId == me!.Id) &&
                            g.WhitePlayerId != me!.Id)
                .OrderByDescending(g => g.CreatedAt)
                .Take(20)
                .ToListAsync();
            return Json(challenges.Select(g => new
            {
                g.Id,
                white = g.WhitePlayer == null ? null : new { g.WhitePlayer.Id, g.WhitePlayer.UserName, g.WhitePlayer.Elo },
                g.Minutes,
                g.IsRanked,
                g.CreatedAt
            }));
        }

        [HttpGet]
        public async Task<IActionResult> GameChatData(Guid id)
        {
            var messages = await _db.ChatMessages
                .Include(m => m.Sender)
                .Where(m => m.GameId == id)
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
                m.SentAt
            }));
        }

        [HttpGet]
        public async Task<IActionResult> Active()
        {
            var games = await _db.Games
                .Include(g => g.WhitePlayer)
                .Include(g => g.BlackPlayer)
                .Where(g => g.Status == "InProgress")
                .OrderByDescending(g => g.StartedAt)
                .Take(50)
                .ToListAsync();
            return Json(games.Select(g => new
            {
                g.Id,
                white = g.WhitePlayer == null ? null : new { g.WhitePlayer.Id, g.WhitePlayer.UserName, g.WhitePlayer.Elo },
                black = g.BlackPlayer == null ? null : new { g.BlackPlayer.Id, g.BlackPlayer.UserName, g.BlackPlayer.Elo },
                g.IsVsBot,
                g.Minutes,
                g.StartedAt
            }));
        }
    }
}
