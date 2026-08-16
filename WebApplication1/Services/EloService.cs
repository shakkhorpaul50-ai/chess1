using WebApplication1.Data;

namespace WebApplication1.Services
{
    public enum GameResult { WhiteWin, BlackWin, Draw }

    public class EloService
    {
        private const int K = 32;
        private const int WinPoints = 3;
        private const int DrawPoints = 1;

        public async Task ApplyAsync(ApplicationDbContext db, string whiteId, string blackId, GameResult result)
        {
            var white = await db.Users.FindAsync(whiteId);
            var black = await db.Users.FindAsync(blackId);
            if (white == null || black == null) return;

            var expectedWhite = 1d / (1d + Math.Pow(10, (black.Elo - white.Elo) / 400d));

            (double scoreWhite, double scoreBlack) = result switch
            {
                GameResult.WhiteWin => (1d, 0d),
                GameResult.BlackWin => (0d, 1d),
                _ => (0.5d, 0.5d)
            };

            white.Elo = Clamp((int)Math.Round(white.Elo + K * (scoreWhite - expectedWhite)));
            black.Elo = Clamp((int)Math.Round(black.Elo + K * (scoreBlack - (1 - expectedWhite))));

            switch (result)
            {
                case GameResult.WhiteWin:
                    white.Wins++;
                    black.Losses++;
                    white.Points += WinPoints;
                    break;
                case GameResult.BlackWin:
                    black.Wins++;
                    white.Losses++;
                    black.Points += WinPoints;
                    break;
                default:
                    white.Draws++;
                    black.Draws++;
                    white.Points += DrawPoints;
                    black.Points += DrawPoints;
                    break;
            }

            await db.SaveChangesAsync();
        }

        private static int Clamp(int elo) => Math.Clamp(elo, 100, 3000);
    }
}
