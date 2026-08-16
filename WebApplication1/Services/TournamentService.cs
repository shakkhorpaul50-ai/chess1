using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Services
{
    public class TournamentService
    {
        private readonly ApplicationDbContext _db;

        public TournamentService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<Guid> CreateAsync(string creatorId, string name, int maxPlayers, int minutes)
        {
            if (maxPlayers is not (4 or 6))
                throw new ArgumentException("Tournament must have 4 or 6 players.");
            if (minutes is not (10 or 30 or 60))
                throw new ArgumentException("Time control must be 10, 30 or 60 minutes.");
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 40)
                throw new ArgumentException("Tournament name must be 1-40 characters.");

            var tournament = new TournamentRecord
            {
                Name = name.Trim(),
                CreatorId = creatorId,
                MaxPlayers = maxPlayers,
                Minutes = minutes,
                Status = "Open"
            };
            _db.Tournaments.Add(tournament);
            await _db.SaveChangesAsync();

            _db.TournamentPlayers.Add(new TournamentPlayerRecord { TournamentId = tournament.Id, UserId = creatorId });
            await _db.SaveChangesAsync();
            return tournament.Id;
        }

        public async Task JoinAsync(string userId, Guid tournamentId)
        {
            var t = await _db.Tournaments.FirstOrDefaultAsync(x => x.Id == tournamentId)
                ?? throw new ArgumentException("Tournament not found.");
            if (t.Status != "Open") throw new ArgumentException("Tournament is not open.");
            var count = await _db.TournamentPlayers.CountAsync(x => x.TournamentId == tournamentId);
            if (count >= t.MaxPlayers) throw new ArgumentException("Tournament is full.");
            if (await _db.TournamentPlayers.AnyAsync(x => x.TournamentId == tournamentId && x.UserId == userId))
                throw new ArgumentException("You already joined this tournament.");

            _db.TournamentPlayers.Add(new TournamentPlayerRecord { TournamentId = tournamentId, UserId = userId });
            await _db.SaveChangesAsync();
        }

        public async Task StartAsync(string creatorId, Guid tournamentId)
        {
            var t = await _db.Tournaments.Include(x => x.Players).ThenInclude(p => p.User)
                .FirstOrDefaultAsync(x => x.Id == tournamentId)
                ?? throw new ArgumentException("Tournament not found.");
            if (t.CreatorId != creatorId) throw new ArgumentException("Only the creator can start the tournament.");
            if (t.Status != "Open") throw new ArgumentException("Tournament already started.");
            if (t.Players.Count != t.MaxPlayers)
                throw new ArgumentException($"Tournament needs {t.MaxPlayers} players, currently has {t.Players.Count}.");

            t.Status = "InProgress";
            t.StartedAt = DateTime.UtcNow;

            var users = t.Players.OrderBy(p => p.JoinedAt).Select(p => p.User!).ToList();
            foreach (var (round, white, black) in RoundRobinPairings(users))
            {
                _db.Games.Add(new GameRecord
                {
                    WhitePlayerId = white.Id,
                    BlackPlayerId = black.Id,
                    Minutes = t.Minutes,
                    Increment = GameService.IncrementSeconds,
                    Status = "Waiting",
                    IsRanked = true,
                    TournamentId = t.Id,
                    Round = round
                });
            }
            await _db.SaveChangesAsync();
        }

        private static List<(int round, ApplicationUser white, ApplicationUser black)> RoundRobinPairings(List<ApplicationUser> players)
        {
            var result = new List<(int, ApplicationUser, ApplicationUser)>();
            int n = players.Count;
            var arr = players.ToArray();

            // Circle method: keep first player fixed, rotate the rest
            for (int round = 0; round < n - 1; round++)
            {
                for (int i = 0; i < n / 2; i++)
                {
                    var a = arr[i];
                    var b = arr[n - 1 - i];
                    (ApplicationUser white, ApplicationUser black) = (round + i) % 2 == 0 ? (a, b) : (b, a);
                    result.Add((round + 1, white, black));
                }

                var last = arr[^1];
                for (int i = n - 1; i > 1; i--) arr[i] = arr[i - 1];
                arr[1] = last;
            }
            return result;
        }

        public async Task<TournamentDto?> DetailAsync(Guid tournamentId)
        {
            var t = await _db.Tournaments
                .Include(x => x.Creator)
                .Include(x => x.Players).ThenInclude(p => p.User)
                .Include(x => x.Games).ThenInclude(g => g.WhitePlayer)
                .Include(x => x.Games).ThenInclude(g => g.BlackPlayer)
                .FirstOrDefaultAsync(x => x.Id == tournamentId);
            if (t == null) return null;

            var standings = new List<TournamentStandingDto>();
            foreach (var p in t.Players)
            {
                var u = p.User;
                if (u == null) continue;
                var games = t.Games.Where(g => g.WhitePlayerId == u.Id || g.BlackPlayerId == u.Id).ToList();
                double points = 0;
                int wins = 0, draws = 0, losses = 0;
                foreach (var g in games)
                {
                    if (g.Status == "WhiteWon")
                    {
                        if (g.WhitePlayerId == u.Id) { points += 1; wins++; } else { losses++; }
                    }
                    else if (g.Status == "BlackWon")
                    {
                        if (g.BlackPlayerId == u.Id) { points += 1; wins++; } else { losses++; }
                    }
                    else if (g.Status == "Draw")
                    {
                        points += 0.5;
                        draws++;
                    }
                }
                standings.Add(new TournamentStandingDto(u.Id, u.UserName ?? "?", u.Elo, points, wins, draws, losses));
            }

            standings = standings
                .OrderByDescending(s => s.Points)
                .ThenByDescending(s => s.Wins)
                .ThenByDescending(s => s.Elo)
                .ToList();
            for (int i = 0; i < standings.Count; i++)
                standings[i] = standings[i] with { Rank = i + 1 };

            var dto = new TournamentDto
            {
                Id = t.Id,
                Name = t.Name,
                CreatorId = t.CreatorId,
                MaxPlayers = t.MaxPlayers,
                Minutes = t.Minutes,
                Status = t.Status,
                CreatedAt = t.CreatedAt,
                StartedAt = t.StartedAt,
                CompletedAt = t.CompletedAt,
                PlayerCount = t.Players.Count,
                Players = t.Players.Where(p => p.User != null).Select(p => new UserDto(p.User!.Id, p.User.UserName ?? "?", p.User.Elo)).ToList(),
                Standings = standings,
                Games = t.Games.Select(g => new GameListItemDto
                {
                    Id = g.Id,
                    White = g.WhitePlayer != null ? new UserDto(g.WhitePlayer.Id, g.WhitePlayer.UserName ?? "?", g.WhitePlayer.Elo) : null,
                    Black = g.BlackPlayer != null ? new UserDto(g.BlackPlayer.Id, g.BlackPlayer.UserName ?? "?", g.BlackPlayer.Elo) : null,
                    IsRanked = true,
                    Minutes = g.Minutes,
                    Status = g.Status,
                    ResultReason = g.ResultReason,
                    Round = g.Round,
                    CreatedAt = g.CreatedAt
                }).OrderBy(g => g.Round).ToList()
            };
            return dto;
        }
    }
}
