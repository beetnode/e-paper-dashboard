namespace EPaperDashboard.Models
{
    public class User
    {
        public UserId Id { get; set; } = UserId.Empty;
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public bool IsSuperUser { get; set; }
        // Null preserves rendering access for accounts created before user-level settings.
        public bool? RenderingEnabled { get; set; }
        public string? Nickname { get; set; }
        public AiConfig? AiConfig { get; set; }
    }
}
