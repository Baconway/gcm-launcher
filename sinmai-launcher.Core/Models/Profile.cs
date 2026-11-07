using System;

namespace sinmai_launcher.Core.Models
{
    public class Profile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Player";
        public GameType Game { get; set; } = GameType.Maimai;
        public string GameDirectory { get; set; } = string.Empty;
        public string? ProfilePicturePath { get; set; }
        public bool StoreReminderShown { get; set; } = false;
        public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.Now;
        public DateTimeOffset? LastPlayedDate { get; set; }
        public LaunchSettings LaunchSettings { get; set; } = new();
    }
}
