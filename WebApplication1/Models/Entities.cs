namespace WebApplication1.Models
{
    public class GameRecord
    {
        public Guid Id { get; set; }
        public string? WhitePlayerId { get; set; }
        public string? BlackPlayerId { get; set; }
        public bool IsVsBot { get; set; }
        public int Minutes { get; set; }
        public int Increment { get; set; } = 5;

        // Waiting | InProgress | WhiteWon | BlackWon | Draw | Abandoned
        public string Status { get; set; } = "Waiting";
        public string? ResultReason { get; set; }

        // JSON array of SAN moves, e.g. ["e4","e5","Nf3"]
        public string? MovesJson { get; set; }

        // JSON array of FEN positions after each move
        public string? FensJson { get; set; }
        public long WhiteClockLeftMs { get; set; }
        public long BlackClockLeftMs { get; set; }
        public bool IsRanked { get; set; }
        public Guid? TournamentId { get; set; }
        public int? Round { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }

        public ApplicationUser? WhitePlayer { get; set; }
        public ApplicationUser? BlackPlayer { get; set; }
        public TournamentRecord? Tournament { get; set; }
    }

    public class TournamentRecord
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string CreatorId { get; set; } = "";
        public int MaxPlayers { get; set; } = 4; // 4 or 6
        public int Minutes { get; set; } = 30;

        // Open | InProgress | Completed
        public string Status { get; set; } = "Open";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public ApplicationUser? Creator { get; set; }
        public List<TournamentPlayerRecord> Players { get; set; } = new();
        public List<GameRecord> Games { get; set; } = new();
    }

    public class TournamentPlayerRecord
    {
        public Guid Id { get; set; }
        public Guid TournamentId { get; set; }
        public string UserId { get; set; } = "";
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        public TournamentRecord? Tournament { get; set; }
        public ApplicationUser? User { get; set; }
    }

    public class FriendRecord
    {
        public Guid Id { get; set; }
        public string RequesterId { get; set; } = "";
        public string AddresseeId { get; set; } = "";

        // Pending | Accepted
        public string Status { get; set; } = "Pending";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ApplicationUser? Requester { get; set; }
        public ApplicationUser? Addressee { get; set; }
    }

    public class ChatMessageRecord
    {
        public Guid Id { get; set; }
        public string SenderId { get; set; } = "";
        public string? ReceiverId { get; set; } // null => game chat
        public Guid? GameId { get; set; }       // null => private chat
        public string Content { get; set; } = "";
        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        public ApplicationUser? Sender { get; set; }
    }
}
