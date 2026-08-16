using Microsoft.AspNetCore.Identity;

namespace WebApplication1.Models
{
    public class ApplicationUser : IdentityUser
    {
        public int Elo { get; set; } = 1200;
        public int Points { get; set; }
        public int Wins { get; set; }
        public int Draws { get; set; }
        public int Losses { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
