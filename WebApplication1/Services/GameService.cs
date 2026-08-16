using System.Text.Json;
using ChessDotNet;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Hubs;
using WebApplication1.Models;

namespace WebApplication1.Services
{
    public class GameService
    {
        public static readonly int[] AllowedMinutes = { 10, 30, 60 };
        public const int IncrementSeconds = 5;
        private const string DefaultFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHubContext<GameHub> _hub;
        private readonly ILogger<GameService> _logger;
        private readonly object _gate = new();
        private readonly Dictionary<Guid, ActiveGame> _active = new();
        private readonly Dictionary<string, string> _connToUser = new();
        private readonly Dictionary<string, int> _userConnections = new();
        private readonly Dictionary<Guid, RematchOffer> _rematchOffers = new();
        private DateTimeOffset _lastSync = DateTimeOffset.UtcNow;
        private int _tickCount;

        private sealed class RematchOffer
        {
            public string? OfferedBy { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
        }

        public GameService(IServiceScopeFactory scopeFactory, IHubContext<GameHub> hub, ILogger<GameService> logger)
        {
            _scopeFactory = scopeFactory;
            _hub = hub;
            _logger = logger;
            _ = TickerLoopAsync();
        }

        public void RegisterConnection(string connectionId, string userId)
        {
            lock (_gate)
            {
                _connToUser[connectionId] = userId;
                _userConnections.TryGetValue(userId, out var n);
                _userConnections[userId] = n + 1;
            }
        }

        public void UnregisterConnection(string connectionId)
        {
            lock (_gate)
            {
                if (_connToUser.Remove(connectionId, out var userId) &&
                    _userConnections.TryGetValue(userId, out var n))
                {
                    if (n <= 1) _userConnections.Remove(userId);
                    else _userConnections[userId] = n - 1;
                }
            }
        }

        private async Task TickerLoopAsync()
        {
            while (true)
            {
                try
                {
                    await Task.Delay(1000);
                    await TickAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Game ticker error");
                }
            }
        }

        private async Task TickAsync()
        {
            var now = DateTimeOffset.UtcNow;
            var toEnd = new List<(ActiveGame g, string code, string reason, long w, long b)>();
            var syncs = new List<ActiveGame>();

            lock (_gate)
            {
                foreach (var g in _active.Values)
                {
                    if (g.Status != "InProgress") continue;
                    var elapsed = (long)(now - g.LastMoveUtc).TotalMilliseconds;
                    var isWhiteTurn = g.Chess.WhoseTurn == Player.White;
                    var moverMs = isWhiteTurn ? g.WhiteMs : g.BlackMs;
                    if (moverMs - elapsed <= 0)
                    {
                        toEnd.Add((g, isWhiteTurn ? "BlackWon" : "WhiteWon", "Timeout", g.WhiteMs, g.BlackMs));
                        continue;
                    }

                    var missing = g.WhiteUserId != null && !_userConnections.ContainsKey(g.WhiteUserId) ? g.WhiteUserId
                        : g.BlackUserId != null && !_userConnections.ContainsKey(g.BlackUserId) ? g.BlackUserId : null;
                    if (missing != null)
                    {
                        g.DisconnectedSinceUtc ??= now;
                        if (now - g.DisconnectedSinceUtc.Value >= TimeSpan.FromSeconds(30))
                        {
                            toEnd.Add((g, missing == g.WhiteUserId ? "BlackWon" : "WhiteWon",
                                "Abandoned", g.WhiteMs, g.BlackMs));
                        }
                    }
                    else
                    {
                        g.DisconnectedSinceUtc = null;
                        syncs.Add(g);
                    }
                }
            }

            if (now - _lastSync >= TimeSpan.FromSeconds(5))
            {
                _lastSync = now;
                foreach (var g in syncs)
                {
                    await _hub.Clients.Group($"game:{g.Id}").SendAsync("ClockSync", new ClockSyncDto(
                        g.Id, g.Chess.WhoseTurn == Player.White ? "White" : "Black", g.WhiteMs, g.BlackMs, now.ToUnixTimeMilliseconds()));
                }
            }

            foreach (var (g, code, reason, w, b) in toEnd)
                await EndGameAsync(g, code, reason, w, b);

            _tickCount++;
            if (_tickCount % 600 == 0) await CleanupStaleLobbiesAsync();
            if (_tickCount % 30 == 0) CleanupStaleRematchOffers(now);
        }

        private void CleanupStaleRematchOffers(DateTimeOffset now)
        {
            lock (_gate)
            {
                var cutoff = now.AddMinutes(-5);
                foreach (var stale in _rematchOffers.Where(x => x.Value.CreatedAt < cutoff).ToList())
                    _rematchOffers.Remove(stale.Key);
            }
        }

        private async Task CleanupStaleLobbiesAsync()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var cutoff = DateTime.UtcNow.AddHours(-1);
                var stale = await db.Games.Where(x => x.Status == "Waiting" && x.CreatedAt < cutoff).ToListAsync();
                if (stale.Count > 0)
                {
                    db.Games.RemoveRange(stale);
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lobby cleanup failed");
            }
        }

        // ---------- Game lifecycle ----------

        public async Task<object> CreateGameAsync(string userId, int minutes, bool isRanked, string? invitedUserId)
        {
            if (!AllowedMinutes.Contains(minutes))
                throw new HubException("Time control must be 10, 30 or 60 minutes.");
            if (invitedUserId == userId)
                throw new HubException("You cannot challenge yourself.");
            if (invitedUserId != null && isRanked)
                throw new HubException("Friendly challenges are never ranked.");

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var me = await db.Users.FindAsync(userId) ?? throw new HubException("User not found.");

            if (invitedUserId != null)
            {
                var invited = await db.Users.FindAsync(invitedUserId);
                if (invited == null) throw new HubException("Invited user not found.");
                var friends = await db.Friends.AnyAsync(f =>
                    ((f.RequesterId == userId && f.AddresseeId == invitedUserId) ||
                     (f.RequesterId == invitedUserId && f.AddresseeId == userId)) && f.Status == "Accepted");
                if (!friends) throw new HubException("You can only challenge friends.");
            }

            var record = new GameRecord
            {
                WhitePlayerId = userId,
                BlackPlayerId = invitedUserId,
                Minutes = minutes,
                Increment = IncrementSeconds,
                Status = "Waiting",
                IsRanked = isRanked
            };
            db.Games.Add(record);
            await db.SaveChangesAsync();

            return new GameDto
            {
                Id = record.Id,
                White = ToDto(me),
                Minutes = minutes,
                Increment = IncrementSeconds,
                Status = "Waiting",
                IsRanked = isRanked
            };
        }

        public async Task<object> CreateBotGameAsync(string userId, int minutes, bool asWhite)
        {
            if (!AllowedMinutes.Contains(minutes))
                throw new HubException("Time control must be 10, 30 or 60 minutes.");

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var me = await db.Users.FindAsync(userId) ?? throw new HubException("User not found.");

            var record = new GameRecord
            {
                WhitePlayerId = asWhite ? userId : null,
                BlackPlayerId = asWhite ? null : userId,
                IsVsBot = true,
                Minutes = minutes,
                Increment = IncrementSeconds,
                Status = "InProgress",
                IsRanked = false,
                StartedAt = DateTime.UtcNow
            };
            db.Games.Add(record);
            await db.SaveChangesAsync();

            var g = new ActiveGame
            {
                Id = record.Id,
                RecordId = record.Id,
                WhiteUserId = asWhite ? userId : null,
                BlackUserId = asWhite ? null : userId,
                WhiteUser = asWhite ? ToDto(me) : null,
                BlackUser = asWhite ? null : ToDto(me),
                IsVsBot = true,
                IsRanked = false,
                Minutes = minutes,
                Increment = IncrementSeconds,
                WhiteMs = minutes * 60_000L,
                BlackMs = minutes * 60_000L,
                LastMoveUtc = DateTimeOffset.UtcNow,
                StartedAtUtc = DateTimeOffset.UtcNow
            };
            lock (_gate) _active[g.Id] = g;
            return ToDto(g);
        }

        public async Task<object> JoinGameAsync(string userId, string connectionId, Guid gameId)
        {
            ActiveGame? g;
            lock (_gate) g = _active.GetValueOrDefault(gameId);

            if (g != null)
            {
                if (g.IsVsBot)
                {
                    var human = g.WhiteUser ?? g.BlackUser;
                    if (human?.Id != userId) throw new HubException("This bot game belongs to another player.");
                }
                else if (g.WhiteUserId != userId && g.BlackUserId != userId)
                {
                    throw new HubException("You are not a player in this game.");
                }
                await _hub.Groups.AddToGroupAsync(connectionId, $"game:{gameId}");
                return ToDto(g);
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var record = await db.Games.Include(x => x.WhitePlayer).Include(x => x.BlackPlayer)
                .FirstOrDefaultAsync(x => x.Id == gameId) ?? throw new HubException("Game not found.");

            if (record.Status is "WhiteWon" or "BlackWon" or "Draw")
                return ToCompletedDto(record);
            if (record.Status != "Waiting")
                throw new HubException("This game is no longer waiting for players.");

            if (record.BlackPlayerId == null)
            {
                if (record.WhitePlayerId == userId) throw new HubException("You are already the white player.");
                record.BlackPlayerId = userId;
            }
            else if (record.BlackPlayerId != userId)
            {
                throw new HubException("This game already has an opponent.");
            }

            var me = await db.Users.FindAsync(userId) ?? throw new HubException("User not found.");
            var white = record.WhitePlayer ?? await db.Users.FindAsync(record.WhitePlayerId!);
            if (white == null) throw new HubException("White player missing.");

            record.Status = "InProgress";
            record.StartedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var active = new ActiveGame
            {
                Id = record.Id,
                RecordId = record.Id,
                WhiteUserId = record.WhitePlayerId,
                BlackUserId = record.BlackPlayerId,
                WhiteUser = ToDto(white),
                BlackUser = ToDto(me),
                IsVsBot = false,
                IsRanked = record.IsRanked,
                Minutes = record.Minutes,
                Increment = record.Increment,
                WhiteMs = record.Minutes * 60_000L,
                BlackMs = record.Minutes * 60_000L,
                LastMoveUtc = DateTimeOffset.UtcNow,
                StartedAtUtc = DateTimeOffset.UtcNow,
                TournamentId = record.TournamentId,
                Round = record.Round
            };
            lock (_gate) _active[active.Id] = active;

            await _hub.Groups.AddToGroupAsync(connectionId, $"game:{gameId}");
            await _hub.Clients.Group($"game:{gameId}").SendAsync("GameStarted", ToDto(active));
            return ToDto(active);
        }

        public async Task PlayMoveAsync(string userId, string connectionId, Guid gameId,
            string from, string to, string? promotion, bool isBot)
        {
            ActiveGame g;
            lock (_gate)
            {
                g = _active.GetValueOrDefault(gameId) ?? throw new HubException("Game not found or already finished.");
                if (g.Status != "InProgress") throw new HubException("Game is over.");
            }

            Player mover = g.Chess.WhoseTurn;
            if (g.IsVsBot)
            {
                var humanId = g.WhiteUser?.Id ?? g.BlackUser?.Id;
                if (humanId != userId) throw new HubException("Not your game.");
                var humanIsWhite = g.WhiteUserId == userId;
                var botColor = humanIsWhite ? Player.Black : Player.White;
                if (isBot && mover != botColor) throw new HubException("It is not the bot's turn.");
                if (!isBot && mover == botColor) throw new HubException("It is the bot's turn.");
            }
            else
            {
                var expectedId = mover == Player.White ? g.WhiteUserId : g.BlackUserId;
                if (expectedId != userId) throw new HubException("It is not your turn.");
            }

            var move = new Move(new Position(from.ToLowerInvariant()), new Position(to.ToLowerInvariant()), mover,
                string.IsNullOrEmpty(promotion) ? null : char.ToLowerInvariant(promotion[0]));

            MoveType moveType;
            lock (_gate)
            {
                if (g.Status != "InProgress") throw new HubException("Game is over.");
                moveType = g.Chess.MakeMove(move, false);
                if (moveType == MoveType.Invalid)
                    throw new HubException("Invalid move.");
            }

            var elapsed = (long)(DateTimeOffset.UtcNow - g.LastMoveUtc).TotalMilliseconds;
            if (mover == Player.White)
            {
                g.WhiteMs = Math.Max(0, g.WhiteMs - elapsed) + g.Increment * 1000L;
                g.BlackMs = Math.Max(0, g.BlackMs);
            }
            else
            {
                g.BlackMs = Math.Max(0, g.BlackMs - elapsed) + g.Increment * 1000L;
                g.WhiteMs = Math.Max(0, g.WhiteMs);
            }
            g.LastMoveUtc = DateTimeOffset.UtcNow;
            g.Fens.Add(g.Chess.GetFen());

            var san = g.Chess.Moves[^1].SAN;
            await _hub.Clients.Group($"game:{g.Id}").SendAsync("MovePlayed", new MoveEventDto(
                g.Id, from, to, promotion, san, g.Chess.GetFen(),
                g.Chess.WhoseTurn == Player.White ? "White" : "Black",
                g.WhiteMs, g.BlackMs, g.Chess.Moves.Count));

            var opponent = ChessUtilities.GetOpponentOf(mover);
            if (g.Chess.IsCheckmated(opponent))
            {
                await EndGameAsync(g, mover == Player.White ? "WhiteWon" : "BlackWon", "Checkmate", g.WhiteMs, g.BlackMs);
            }
            else if (g.Chess.IsStalemated(opponent))
            {
                await EndGameAsync(g, "Draw", "Stalemate", g.WhiteMs, g.BlackMs);
            }
            else if (g.Chess.IsInsufficientMaterial())
            {
                await EndGameAsync(g, "Draw", "Insufficient material", g.WhiteMs, g.BlackMs);
            }
            else if (g.Chess.HalfMoveClock >= 100)
            {
                await EndGameAsync(g, "Draw", "Fifty-move rule", g.WhiteMs, g.BlackMs);
            }
            else if (IsThreefoldRepetition(g))
            {
                await EndGameAsync(g, "Draw", "Threefold repetition", g.WhiteMs, g.BlackMs);
            }
        }

        public async Task ResignAsync(string userId, Guid gameId)
        {
            ActiveGame g;
            string code;
            lock (_gate)
            {
                g = _active.GetValueOrDefault(gameId) ?? throw new HubException("Game not found.");
                if (g.Status != "InProgress") throw new HubException("Game is over.");
                var isWhite = g.WhiteUserId == userId;
                if (!isWhite && g.BlackUserId != userId) throw new HubException("You are not a player in this game.");
                g.Status = "Ended";
                code = isWhite ? "BlackWon" : "WhiteWon";
            }
            await EndGameAsync(g, code, "Resignation", g.WhiteMs, g.BlackMs);
        }

        public async Task OfferDrawAsync(string userId, Guid gameId)
        {
            ActiveGame g;
            lock (_gate)
            {
                g = _active.GetValueOrDefault(gameId) ?? throw new HubException("Game not found.");
                if (g.Status != "InProgress") throw new HubException("Game is over.");
                if (g.WhiteUserId != userId && g.BlackUserId != userId) throw new HubException("Not your game.");
                if (g.DrawOfferedBy != null) throw new HubException("A draw offer is already pending.");
                g.DrawOfferedBy = userId;
            }
            await _hub.Clients.Group($"game:{gameId}").SendAsync("DrawOffered", gameId, userId);
        }

        public async Task AcceptDrawAsync(string userId, Guid gameId)
        {
            ActiveGame g;
            lock (_gate)
            {
                g = _active.GetValueOrDefault(gameId) ?? throw new HubException("Game not found.");
                if (g.DrawOfferedBy == null) throw new HubException("No pending draw offer.");
                if (g.DrawOfferedBy == userId) throw new HubException("You cannot accept your own offer.");
                if (g.WhiteUserId != userId && g.BlackUserId != userId) throw new HubException("Not your game.");
                g.Status = "Ended";
            }
            await EndGameAsync(g, "Draw", "Draw agreed", g.WhiteMs, g.BlackMs);
        }

        public async Task DeclineDrawAsync(string userId, Guid gameId)
        {
            ActiveGame g;
            lock (_gate)
            {
                g = _active.GetValueOrDefault(gameId) ?? throw new HubException("Game not found.");
                if (g.WhiteUserId != userId && g.BlackUserId != userId) throw new HubException("Not your game.");
                g.DrawOfferedBy = null;
            }
            await _hub.Clients.Group($"game:{gameId}").SendAsync("DrawDeclined", gameId, userId);
        }

        public async Task CancelGameAsync(string userId, Guid gameId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var record = await db.Games.FirstOrDefaultAsync(x => x.Id == gameId);
            if (record == null || record.Status != "Waiting") throw new HubException("Game cannot be cancelled.");
            if (record.WhitePlayerId != userId) throw new HubException("Only the creator can cancel this lobby.");
            db.Games.Remove(record);
            await db.SaveChangesAsync();
        }

        public async Task SpectateAsync(string connectionId, Guid gameId)
        {
            var dto = await GetGameAsync(gameId);
            if (dto == null) throw new HubException("Game not found.");
            await _hub.Groups.AddToGroupAsync(connectionId, $"game:{gameId}");
        }

        public Task LeaveAsync(string connectionId, Guid gameId)
            => _hub.Groups.RemoveFromGroupAsync(connectionId, $"game:{gameId}");

        // ---------- Rematch ----------

        public async Task<Guid?> OfferRematchAsync(string userId, Guid gameId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var record = await db.Games.FirstOrDefaultAsync(x => x.Id == gameId)
                ?? throw new HubException("Game not found.");
            if (record.Status is not ("WhiteWon" or "BlackWon" or "Draw"))
                throw new HubException("Game has not finished.");
            if (record.WhitePlayerId != userId && record.BlackPlayerId != userId)
                throw new HubException("You are not a player in this game.");

            if (record.IsVsBot)
            {
                var dto = (GameDto)await CreateBotGameAsync(userId, record.Minutes, record.WhitePlayerId != userId);
                await _hub.Clients.Group($"game:{gameId}").SendAsync("RematchStarted", dto.Id);
                return dto.Id;
            }

            bool autoAccept;
            lock (_gate)
            {
                var hasOffer = _rematchOffers.TryGetValue(gameId, out var existing);
                if (hasOffer && existing!.OfferedBy != userId)
                    autoAccept = true;
                else
                {
                    autoAccept = false;
                    if (!hasOffer)
                        _rematchOffers[gameId] = new RematchOffer { OfferedBy = userId, CreatedAt = DateTimeOffset.UtcNow };
                }
            }

            if (autoAccept)
            {
                var newId = await CreateRematchRecordAsync(record);
                lock (_gate) _rematchOffers.Remove(gameId);
                await _hub.Clients.Group($"game:{gameId}").SendAsync("RematchStarted", newId);
                return newId;
            }

            await _hub.Clients.Group($"game:{gameId}").SendAsync("RematchOffered", gameId, userId);
            return null;
        }

        public async Task<Guid> AcceptRematchAsync(string userId, Guid gameId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var record = await db.Games.FirstOrDefaultAsync(x => x.Id == gameId)
                ?? throw new HubException("Game not found.");
            if (record.Status is not ("WhiteWon" or "BlackWon" or "Draw"))
                throw new HubException("Game has not finished.");
            if (record.WhitePlayerId != userId && record.BlackPlayerId != userId)
                throw new HubException("You are not a player in this game.");

            lock (_gate)
            {
                if (!_rematchOffers.TryGetValue(gameId, out var offer))
                    throw new HubException("No rematch offer pending.");
                if (offer.OfferedBy == userId)
                    throw new HubException("You cannot accept your own offer.");
                _rematchOffers.Remove(gameId);
            }

            var newId = await CreateRematchRecordAsync(record);
            await _hub.Clients.Group($"game:{gameId}").SendAsync("RematchStarted", newId);
            return newId;
        }

        public Task DeclineRematchAsync(string userId, Guid gameId)
        {
            lock (_gate) _rematchOffers.Remove(gameId);
            return _hub.Clients.Group($"game:{gameId}").SendAsync("RematchDeclined", gameId, userId);
        }

        private async Task<Guid> CreateRematchRecordAsync(GameRecord original)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var record = new GameRecord
            {
                WhitePlayerId = original.BlackPlayerId,
                BlackPlayerId = original.WhitePlayerId,
                Minutes = original.Minutes,
                Increment = original.Increment,
                Status = "Waiting",
                IsRanked = original.IsRanked
            };
            db.Games.Add(record);
            await db.SaveChangesAsync();
            return record.Id;
        }

        public async Task<GameDto?> GetGameAsync(Guid gameId)
        {
            ActiveGame? g;
            lock (_gate) g = _active.GetValueOrDefault(gameId);
            if (g != null) return ToDto(g);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var record = await db.Games.Include(x => x.WhitePlayer).Include(x => x.BlackPlayer)
                .FirstOrDefaultAsync(x => x.Id == gameId);
            return record == null ? null : ToCompletedDto(record);
        }

        // ---------- Persistence ----------

        private async Task EndGameAsync(ActiveGame g, string code, string reason, long whiteMs, long blackMs)
        {
            lock (_gate) _active.Remove(g.Id);
            g.Status = "Ended";
            g.Result = code;
            g.ResultReason = reason;

            await _hub.Clients.Group($"game:{g.Id}").SendAsync("GameEnded", new GameEndedEventDto(g.Id, code, reason, whiteMs, blackMs));

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var record = await db.Games.FirstOrDefaultAsync(x => x.Id == g.Id);
                if (record != null)
                {
                    record.Status = code;
                    record.ResultReason = reason;
                    record.MovesJson = JsonSerializer.Serialize(g.Chess.Moves.Select(m => m.SAN).ToList());
                    record.FensJson = JsonSerializer.Serialize(g.Fens);
                    record.WhiteClockLeftMs = whiteMs;
                    record.BlackClockLeftMs = blackMs;
                    record.EndedAt = DateTime.UtcNow;
                    record.StartedAt ??= DateTime.UtcNow;

                    if (g.IsRanked && !g.IsVsBot && g.WhiteUserId != null && g.BlackUserId != null)
                    {
                        var elo = scope.ServiceProvider.GetRequiredService<EloService>();
                        var result = code == "WhiteWon" ? GameResult.WhiteWin
                            : code == "BlackWon" ? GameResult.BlackWin : GameResult.Draw;
                        await elo.ApplyAsync(db, g.WhiteUserId, g.BlackUserId, result);
                    }

                    await db.SaveChangesAsync();

                    if (record.TournamentId != null)
                    {
                        var tournament = await db.Tournaments.Include(t => t.Games)
                            .FirstOrDefaultAsync(t => t.Id == record.TournamentId);
                        if (tournament != null && tournament.Status == "InProgress" && tournament.Games.Count > 0 &&
                            tournament.Games.All(x => x.Status is "WhiteWon" or "BlackWon" or "Draw" or "Abandoned"))
                        {
                            tournament.Status = "Completed";
                            tournament.CompletedAt = DateTime.UtcNow;
                            await db.SaveChangesAsync();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist game {GameId}", g.Id);
            }
        }

        // ---------- DTO helpers ----------

        private static UserDto ToDto(ApplicationUser u) => new(u.Id, u.UserName ?? "unknown", u.Elo);

        private static GameDto ToDto(ActiveGame g) => new()
        {
            Id = g.Id,
            White = g.WhiteUser,
            Black = g.BlackUser,
            IsVsBot = g.IsVsBot,
            IsRanked = g.IsRanked,
            Minutes = g.Minutes,
            Increment = g.Increment,
            Status = g.Status,
            Result = g.Result,
            ResultReason = g.ResultReason,
            Fen = g.Chess.GetFen(),
            Moves = g.Chess.Moves.Select(m => m.SAN).ToList(),
            WhoseTurn = g.Chess.WhoseTurn == Player.White ? "White" : "Black",
            WhiteMsLeft = g.WhiteMs,
            BlackMsLeft = g.BlackMs,
            StartedAt = g.StartedAtUtc,
            TournamentId = g.TournamentId,
            Round = g.Round
        };

        private static GameDto ToCompletedDto(GameRecord r) => new()
        {
            Id = r.Id,
            White = r.WhitePlayer != null ? ToDto(r.WhitePlayer) : null,
            Black = r.BlackPlayer != null ? ToDto(r.BlackPlayer) : null,
            IsVsBot = r.IsVsBot,
            IsRanked = r.IsRanked,
            Minutes = r.Minutes,
            Increment = r.Increment,
            Status = r.Status is "WhiteWon" or "BlackWon" or "Draw" ? "Ended" : r.Status,
            Result = r.Status is "WhiteWon" or "BlackWon" or "Draw" ? r.Status : null,
            ResultReason = r.ResultReason,
            Moves = Deserialize(r.MovesJson),
            Fen = Deserialize(r.FensJson).LastOrDefault() ?? DefaultFen,
            WhoseTurn = "White",
            WhiteMsLeft = r.WhiteClockLeftMs,
            BlackMsLeft = r.BlackClockLeftMs,
            TournamentId = r.TournamentId,
            Round = r.Round
        };

        private static List<string> Deserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<string>();
            try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
            catch { return new List<string>(); }
        }

        private static bool IsThreefoldRepetition(ActiveGame g)
        {
            var current = NormalizeFen(g.Chess.GetFen());
            var count = 0;
            foreach (var fen in g.Fens)
            {
                if (NormalizeFen(fen) != current) continue;
                if (++count >= 3) return true;
            }
            return false;
        }

        private static string NormalizeFen(string fen)
        {
            var parts = fen.Split(' ');
            return parts.Length >= 4 ? $"{parts[0]} {parts[1]} {parts[2]} {parts[3]}" : fen;
        }
    }
}
