namespace WebApplication1.Models
{
    public record UserDto(string Id, string Username, int Elo);

    public class GameDto
    {
        public Guid Id { get; set; }
        public UserDto? White { get; set; }
        public UserDto? Black { get; set; }
        public bool IsVsBot { get; set; }
        public bool IsRanked { get; set; }
        public int Minutes { get; set; }
        public int Increment { get; set; }

        // Waiting | InProgress | Ended
        public string Status { get; set; } = "Waiting";
        public string? Result { get; set; }     // WhiteWon | BlackWon | Draw
        public string? ResultReason { get; set; }

        public string Fen { get; set; } = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
        public List<string> Moves { get; set; } = new();
        public string WhoseTurn { get; set; } = "White";
        public long WhiteMsLeft { get; set; }
        public long BlackMsLeft { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public Guid? TournamentId { get; set; }
        public int? Round { get; set; }
    }

    public class GameListItemDto
    {
        public Guid Id { get; set; }
        public UserDto? White { get; set; }
        public UserDto? Black { get; set; }
        public bool IsVsBot { get; set; }
        public bool IsRanked { get; set; }
        public int Minutes { get; set; }
        public string Status { get; set; } = "";
        public string? ResultReason { get; set; }
        public int? Round { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public record MoveEventDto(Guid GameId, string From, string To, string? Promotion, string San, string Fen,
        string WhoseTurn, long WhiteMsLeft, long BlackMsLeft, int MoveNumber);

    public record GameEndedEventDto(Guid GameId, string Result, string Reason, long WhiteMsLeft, long BlackMsLeft);

    public record ClockSyncDto(Guid GameId, string WhoseTurn, long WhiteMsLeft, long BlackMsLeft, long NowMs);

    public record ChatMessageDto(Guid Id, string SenderId, string SenderName, string? ReceiverId, Guid? GameId,
        string Content, DateTime SentAt);

    public record TournamentStandingDto(string UserId, string Username, int Elo, double Points, int Wins, int Draws, int Losses)
    {
        public int Rank { get; set; }
    }

    public class TournamentDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string CreatorId { get; set; } = "";
        public int MaxPlayers { get; set; }
        public int Minutes { get; set; }
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public int PlayerCount { get; set; }
        public List<UserDto> Players { get; set; } = new();
        public List<TournamentStandingDto> Standings { get; set; } = new();
        public List<GameListItemDto> Games { get; set; } = new();
    }
}
