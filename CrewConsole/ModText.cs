namespace SS14LocalMods.CrewConsole;

internal static class ModText
{
    public static string T(string ru, string en) => Environment.GetEnvironmentVariable("SS14_MOD_LANGUAGE") switch
    {
        "en" => en,
        "ru" => ru,
        _ => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? ru : en
    };
}
