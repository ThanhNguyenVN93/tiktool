namespace TikTool.Services;

/// <summary>
/// Tiny bilingual helper. Call Initialize() once at startup (before any UI),
/// then T("tiếng Việt", "English") everywhere in the codebase.
/// Language change saves to settings then calls Application.Restart().
/// </summary>
public static class LanguageService
{
    private static bool _en;

    public static void Initialize(string language) => _en = language == "en";

    public static string T(string vi, string en) => _en ? en : vi;
}
