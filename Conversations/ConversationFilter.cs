using System.Text.RegularExpressions;

namespace SS14LocalMods.Conversations;

public sealed record Speaker(string? Name, string? RadioChannel)
{
    // Radio messages usually have no entity ID. Match the displayed voice, as in Crew Monitor.
    public string? Key => Name?.Trim().ToUpperInvariant();
}

public sealed class ConversationFilter
{
    public string Query { get; set; } = "";
    public string MessageQuery { get; set; } = "";
    public string? RadioChannel { get; set; }
    public bool SelectedOnly { get; set; }
    public HashSet<string> Selected { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Active => Query.Trim().Length > 0 || MessageQuery.Trim().Length > 0 || RadioChannel != null || SelectedOnly;

    public bool Matches(Speaker speaker, string message = "") =>
        (Query.Trim().Length == 0 || speaker.Name?.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase) == true) &&
        (MessageQuery.Trim().Length == 0 || message.Contains(MessageQuery.Trim(), StringComparison.OrdinalIgnoreCase)) &&
        (RadioChannel == null || string.Equals(RadioChannel, speaker.RadioChannel, StringComparison.OrdinalIgnoreCase)) &&
        (!SelectedOnly || speaker.Key != null && Selected.Contains(speaker.Key));

    public void Reset() { Query = ""; MessageQuery = ""; RadioChannel = null; SelectedOnly = false; Selected.Clear(); }

    // Adapted from ss14-crew-monitor's ConversationMonitor.ParseMetadata.
    public static Speaker Parse(string channel, string display, string text)
    {
        if (channel == "Radio")
        {
            var plain = Plain(display);
            var radio = RadioPattern.Match(plain);
            var voice = RadioVoicePattern.Match(display);
            var name = voice.Success ? RolePattern.Replace(Plain(voice.Groups["name"].Value), "").Trim() : "";
            if (name.Length == 0 && !radio.Success)
            {
                var announcement = AnnouncementPattern.Match(plain);
                if (announcement.Success) name = announcement.Groups["name"].Value.Trim();
            }
            return new(Clean(name), radio.Success ? Clean(radio.Groups["channel"].Value) : null);
        }
        var tagged = NamePattern.Match(display);
        if (tagged.Success) return new(Clean(Plain(tagged.Groups["name"].Value)), null);
        if (channel is "OOC" or "LOOC" or "Dead" or "Admin")
        {
            // Native OOC/LOOC/dead chat use a bold name followed by a colon, without [Name].
            var bold = BoldSpeakerPattern.Match(display);
            if (bold.Success) return new(Clean(Plain(bold.Groups["name"].Value)), null);
        }
        if (channel == "Emote")
        {
            var plain = Plain(display);
            var action = Plain(text);
            if (action.Length > 0 && plain.EndsWith(action, StringComparison.OrdinalIgnoreCase))
                return new(Clean(plain[..^action.Length]), null);
        }
        return new(null, null);
    }

    // History stores the client's receive tick, not a server utterance timestamp.
    public static TimeSpan? GameTime(uint tick, uint baseTick, TimeSpan baseTime, TimeSpan period)
    {
        if (period.Ticks <= 0) return null;
        try
        {
            var ticks = checked(baseTime.Ticks + checked(((long)tick - baseTick) * period.Ticks));
            return ticks >= 0 ? TimeSpan.FromTicks(ticks) : null;
        }
        catch (OverflowException) { return null; }
    }

    public static string FormatTime(TimeSpan? time) => time is { } t
        ? $"{(long)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}" : "—";

    private static string Plain(string text) => MarkupPattern.Replace(text, "").Replace("\\[", "[").Replace("\\]", "]").Trim();
    private static string? Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline;
    private static readonly Regex MarkupPattern = new(@"(?<!\\)\[[A-Za-z/][^\]\r\n]{0,160}\]", RegexOptions.Compiled);
    private static readonly Regex NamePattern = new(@"\[Name\](?<name>.*?)\[/Name\]", Options);
    private static readonly Regex RadioPattern = new(@"^\s*\[(?<channel>[^\]\r\n]{1,80})\]", RegexOptions.Compiled);
    private static readonly Regex RadioVoicePattern = new(@"\\\[[^\]\r\n]+\\\]\s*\[bold\](?<name>.*?)\[/bold\]", Options);
    private static readonly Regex RolePattern = new(@"^(?:\[[^\]\r\n]+\]\s*)+", RegexOptions.Compiled);
    private static readonly Regex AnnouncementPattern = new(@"^(?<name>[^:\r\n]{1,100}):", RegexOptions.Compiled);
    private static readonly Regex BoldSpeakerPattern = new(@"^[^:\r\n]{1,160}:\s*\[bold\](?<name>.*?):\[/bold\]", Options);
}
