using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;

namespace WebApplication1.Controllers
{
    public class SpectateController : Controller
    {
        private readonly ApplicationDbContext _db;

        public SpectateController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var games = await _db.Games
                .Include(g => g.WhitePlayer)
                .Include(g => g.BlackPlayer)
                .Where(g => g.Status == "InProgress")
                .OrderByDescending(g => g.StartedAt)
                .Take(100)
                .ToListAsync();
            return View(games);
        }

        [HttpGet]
        public IActionResult Watch(Guid id)
        {
            return RedirectToAction("Play", "Game", new { id });
        }
    }
}
