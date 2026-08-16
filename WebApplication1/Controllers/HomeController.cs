using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public HomeController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var activeGames = await _db.Games
                .Include(g => g.WhitePlayer)
                .Include(g => g.BlackPlayer)
                .Where(g => g.Status == "InProgress")
                .OrderByDescending(g => g.StartedAt)
                .Take(10)
                .ToListAsync();

            var tournaments = await _db.Tournaments
                .Include(t => t.Players)
                .Where(t => t.Status != "Completed")
                .OrderByDescending(t => t.CreatedAt)
                .Take(5)
                .ToListAsync();

            var leaderboard = await _db.Users
                .OrderByDescending(u => u.Elo)
                .Take(5)
                .ToListAsync();

            var viewModel = new LobbyViewModel
            {
                ActiveGames = activeGames,
                Tournaments = tournaments.Select(t => new LobbyTournament
                {
                    Id = t.Id,
                    Name = t.Name,
                    MaxPlayers = t.MaxPlayers,
                    Minutes = t.Minutes,
                    Status = t.Status,
                    PlayerCount = t.Players.Count
                }).ToList(),
                Leaderboard = leaderboard
            };
            return View(viewModel);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }

    public class LobbyViewModel
    {
        public List<GameRecord> ActiveGames { get; set; } = new();
        public List<LobbyTournament> Tournaments { get; set; } = new();
        public List<ApplicationUser> Leaderboard { get; set; } = new();
    }

    public class LobbyTournament
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public int MaxPlayers { get; set; }
        public int Minutes { get; set; }
        public string Status { get; set; } = "";
        public int PlayerCount { get; set; }
    }
}
