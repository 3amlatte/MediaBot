namespace MediaRenamer.Services;

public record LanguageOption(string Code, string Name)
{
    public string Display => $"{Name} ({Code})";
    public override string ToString() => Display;
}

public static class Languages
{
    public static readonly IReadOnlyList<LanguageOption> All = new LanguageOption[]
    {
        new("en", "English"),
        new("fr", "French"),
        new("de", "German"),
        new("es", "Spanish"),
        new("it", "Italian"),
        new("pt-br", "Portuguese (Brazil)"),
        new("pt-pt", "Portuguese (Portugal)"),
        new("nl", "Dutch"),
        new("sv", "Swedish"),
        new("no", "Norwegian"),
        new("da", "Danish"),
        new("fi", "Finnish"),
        new("pl", "Polish"),
        new("ru", "Russian"),
        new("uk", "Ukrainian"),
        new("cs", "Czech"),
        new("sk", "Slovak"),
        new("hu", "Hungarian"),
        new("ro", "Romanian"),
        new("bg", "Bulgarian"),
        new("el", "Greek"),
        new("tr", "Turkish"),
        new("ar", "Arabic"),
        new("he", "Hebrew"),
        new("hi", "Hindi"),
        new("th", "Thai"),
        new("vi", "Vietnamese"),
        new("id", "Indonesian"),
        new("ja", "Japanese"),
        new("ko", "Korean"),
        new("zh-cn", "Chinese (Simplified)"),
        new("zh-tw", "Chinese (Traditional)")
    };
}
