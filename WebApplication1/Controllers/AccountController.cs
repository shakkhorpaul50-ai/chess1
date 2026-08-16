using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(string username, string? email, string password, string confirmPassword)
        {
            if (password != confirmPassword)
                ModelState.AddModelError("", "Passwords do not match.");

            username = username.Trim();
            if (username.Length < 3 || username.Length > 24)
                ModelState.AddModelError("", "Username must be 3-24 characters.");

            if (!ModelState.IsValid)
                return View();

            if (await _userManager.FindByNameAsync(username) != null)
            {
                ModelState.AddModelError("", "Username is already taken.");
                return View();
            }
            if (!string.IsNullOrEmpty(email) && await _userManager.FindByEmailAsync(email) != null)
            {
                ModelState.AddModelError("", "Email is already registered.");
                return View();
            }

            var user = new ApplicationUser { UserName = username, Email = string.IsNullOrWhiteSpace(email) ? null : email };
            var result = await _userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError("", error.Description);
                return View();
            }

            await _signInManager.SignInAsync(user, isPersistent: true);
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string usernameOrEmail, string password, bool rememberMe, string? returnUrl = null)
        {
            var user = await _userManager.FindByNameAsync(usernameOrEmail.Trim())
                       ?? await _userManager.FindByEmailAsync(usernameOrEmail.Trim());

            if (user == null || !await _userManager.CheckPasswordAsync(user, password))
            {
                ModelState.AddModelError("", "Invalid username or password.");
                return View();
            }

            await _signInManager.SignInAsync(user, isPersistent: rememberMe);
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }
    }
}
