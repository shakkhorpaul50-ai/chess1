using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Services;

namespace WebApplication1.Controllers
{
    public class TournamentController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly TournamentService _tournaments;

        public TournamentController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, TournamentService tournaments)
        {
            _db = db;
            _userManager = userManager;
            _tournaments = tournaments;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var tournaments = await _db.Tournaments
                .Include(t => t.Players)
                .OrderByDescending(t => t.CreatedAt)
                .Take(50)
                .ToListAsync();

            var me = await _userManager.GetUserAsync(User);
            var viewModel = tournaments.Select(t => new TournamentListViewModel
            {
                Id = t.Id,
                Name = t.Name,
                MaxPlayers = t.MaxPlayers,
                Minutes = t.Minutes,
                Status = t.Status,
                PlayerCount = t.Players.Count,
                CreatedAt = t.CreatedAt,
                JoinedByMe = me != null && t.Players.Any(p => p.UserId == me.Id)
            }).ToList();

            return View(viewModel);
        }

        [HttpGet]
        [Authorize]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> Create(string name, int maxPlayers, int minutes)
        {
            try
            {
                var me = await _userManager.GetUserAsync(User);
                var id = await _tournaments.CreateAsync(me!.Id, name, maxPlayers, minutes);
                return RedirectToAction("Detail", new { id });
            }
            catch (ArgumentException ex)
            {
                ModelState.AddModelError("", ex.Message);
                return View();
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> Join(Guid id)
        {
            var me = await _userManager.GetUserAsync(User);
            try
            {
                await _tournaments.JoinAsync(me!.Id, id);
                TempData["Message"] = "You joined the tournament.";
            }
            catch (ArgumentException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction("Detail", new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> Start(Guid id)
        {
            var me = await _userManager.GetUserAsync(User);
            try
            {
                await _tournaments.StartAsync(me!.Id, id);
                TempData["Message"] = "Tournament started! Your games are ready.";
            }
            catch (ArgumentException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction("Detail", new { id });
        }

        [HttpGet]
        public async Task<IActionResult> Detail(Guid id)
        {
            var dto = await _tournaments.DetailAsync(id);
            if (dto == null) return NotFound();

            var me = await _userManager.GetUserAsync(User);
            ViewData["JoinedByMe"] = me != null && dto.Players.Any(p => p.Id == me.Id);
            ViewData["IsCreator"] = me != null && dto.CreatorId == me.Id;
            return View(dto);
        }
    }

    public class TournamentListViewModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public int MaxPlayers { get; set; }
        public int Minutes { get; set; }
        public string Status { get; set; } = "";
        public int PlayerCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool JoinedByMe { get; set; }
    }
}
