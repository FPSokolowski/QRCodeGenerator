namespace QRCodeGenerator.Models;

public class UserPreferences
{
    public string Language { get; set; } = "pl";

    public string Theme { get; set; } = "light";

    public List<RecentPreferenceSnapshot> Recent { get; set; } = [];
}

public class RecentPreferenceSnapshot
{
    public string Action { get; set; } = "generate";

    public string ContentType { get; set; } = "general";

    public string ErrorCorrectionLevel { get; set; } = "Q";

    public string CameraProfile { get; set; } = "standard";

    public string PrintFormat { get; set; } = "a4";

    public string PrintGradient { get; set; } = "aurora";

    public string DesignTemplate { get; set; } = "clean";

    public bool DesignEnabled { get; set; }

    public bool DarkModePreference { get; set; }
}
