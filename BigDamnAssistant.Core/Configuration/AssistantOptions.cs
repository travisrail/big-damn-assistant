namespace BigDamnAssistant.Core.Configuration;

public class AssistantOptions
{
    public const string SectionName = "Assistant";

    public string Name { get; set; } = "Big Damn Assistant";
    public string TriggerKeyword { get; set; } = "BDA";

    // Session management
    public int SessionBoundaryHours { get; set; } = 4;
    public int MaxCurrentSessionMessages { get; set; } = 10;
    public int MaxSessionSummaries { get; set; } = 5;
}
