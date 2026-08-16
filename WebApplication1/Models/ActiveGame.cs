using ChessDotNet;

namespace WebApplication1.Models
{
    public class ActiveGame
    {
        public Guid Id { get; set; }
        public Guid RecordId { get; set; }
        public string? WhiteUserId { get; set; }
        public string? BlackUserId { get; set; }
        public UserDto? WhiteUser { get; set; }
        public UserDto? BlackUser { get; set; }
        public bool IsVsBot { get; set; }
        public bool IsRanked { get; set; }
        public int Minutes { get; set; }
        public int Increment { get; set; }
        public long WhiteMs { get; set; }
        public long BlackMs { get; set; }
        public DateTimeOffset LastMoveUtc { get; set; }
        public DateTimeOffset StartedAtUtc { get; set; }
        public ChessGame Chess { get; set; } = new();
        public List<string> Fens { get; set; } = new() { "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1" };

        // InProgress | Ended
        public string Status { get; set; } = "InProgress";
        public string? Result { get; set; }
        public string? ResultReason { get; set; }
        public string? DrawOfferedBy { get; set; }
        public DateTimeOffset? DisconnectedSinceUtc { get; set; }
        public Guid? TournamentId { get; set; }
        public int? Round { get; set; }
    }
}
