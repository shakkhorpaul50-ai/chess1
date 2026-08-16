using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Models;

namespace WebApplication1.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<GameRecord> Games => Set<GameRecord>();
        public DbSet<TournamentRecord> Tournaments => Set<TournamentRecord>();
        public DbSet<TournamentPlayerRecord> TournamentPlayers => Set<TournamentPlayerRecord>();
        public DbSet<FriendRecord> Friends => Set<FriendRecord>();
        public DbSet<ChatMessageRecord> ChatMessages => Set<ChatMessageRecord>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<GameRecord>(e =>
            {
                e.HasOne(g => g.WhitePlayer).WithMany().HasForeignKey(g => g.WhitePlayerId).OnDelete(DeleteBehavior.SetNull);
                e.HasOne(g => g.BlackPlayer).WithMany().HasForeignKey(g => g.BlackPlayerId).OnDelete(DeleteBehavior.SetNull);
                e.HasOne(g => g.Tournament).WithMany(t => t.Games).HasForeignKey(g => g.TournamentId).OnDelete(DeleteBehavior.SetNull);
                e.HasIndex(g => g.Status);
                e.HasIndex(g => g.TournamentId);
            });

            builder.Entity<TournamentRecord>(e =>
            {
                e.HasOne(t => t.Creator).WithMany().HasForeignKey(t => t.CreatorId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<TournamentPlayerRecord>(e =>
            {
                e.HasOne(tp => tp.Tournament).WithMany(t => t.Players).HasForeignKey(tp => tp.TournamentId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(tp => tp.User).WithMany().HasForeignKey(tp => tp.UserId).OnDelete(DeleteBehavior.Cascade);
                e.HasIndex(tp => new { tp.TournamentId, tp.UserId }).IsUnique();
            });

            builder.Entity<FriendRecord>(e =>
            {
                e.HasOne(f => f.Requester).WithMany().HasForeignKey(f => f.RequesterId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(f => f.Addressee).WithMany().HasForeignKey(f => f.AddresseeId).OnDelete(DeleteBehavior.Restrict);
                e.HasIndex(f => new { f.RequesterId, f.AddresseeId }).IsUnique();
                e.HasIndex(f => f.Status);
            });

            builder.Entity<ChatMessageRecord>(e =>
            {
                e.HasOne(m => m.Sender).WithMany().HasForeignKey(m => m.SenderId).OnDelete(DeleteBehavior.Cascade);
                e.HasIndex(m => new { m.GameId, m.SentAt });
                e.HasIndex(m => new { m.ReceiverId, m.SentAt });
            });
        }
    }
}
