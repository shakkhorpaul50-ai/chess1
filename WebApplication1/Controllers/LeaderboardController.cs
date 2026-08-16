using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    public class LeaderboardController : Controller
    {
        private readonly ApplicationDbContext _db;

        public LeaderboardController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var users = await _db.Users
                .OrderByDescending(u => u.Elo)
                .Take(100)
                .ToListAsync();
            return View(users);
        }
    }
}
