namespace sinmai_launcher.Core.Models
{
    public enum GameType
    {
        Maimai,
        Ongeki,
        Chunithm
    }

    public static class GameTypeExtensions
    {
        public static string GetDisplayName(this GameType gameType) => gameType switch
        {
            GameType.Maimai => "maimai DX",
            GameType.Ongeki => "O.N.G.E.K.I.",
            GameType.Chunithm => "CHUNITHM",
            _ => gameType.ToString()
        };

        public static string GetDefaultExecutableName(this GameType gameType) => gameType switch
        {
            GameType.Maimai => "Sinmai.exe",
            GameType.Ongeki => "ongeki.exe",
            GameType.Chunithm => "chuniApp.exe",
            _ => "game.exe"
        };
    }
}
