namespace BigDamnAssistant.Core.Configuration;

public class AssistantOptions
{
    public const string SectionName = "Assistant";

    public string Name { get; set; } = "Big Damn Assistant";
    public string TriggerKeyword { get; set; } = "BDA";
}
