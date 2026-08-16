using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    public class ProfileController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public ProfileController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? id)
        {
            ApplicationUser? user;
            if (!string.IsNullOrEmpty(id))
            {
                user = await _db.Users.FindAsync(id);
                if (user == null) return NotFound();
            }
            else if (User.Identity?.IsAuthenticated == true)
            {
                user = await _userManager.GetUserAsync(User);
                if (user == null) return NotFound();
            }
            else
            {
                return RedirectToAction("Login", "Account");
            }

            var games = await _db.Games
                .Include(g => g.WhitePlayer)
                .Include(g => g.BlackPlayer)
                .Where(g => g.WhitePlayerId == user!.Id || g.BlackPlayerId == user.Id)
                .OrderByDescending(g => g.CreatedAt)
                .Take(50)
                .ToListAsync();

            ViewData["Games"] = games;
            ViewData["IsMe"] = id == null || (User.Identity?.IsAuthenticated == true && _userManager.GetUserId(User) == id);
            return View(user);
        }
    }
}
