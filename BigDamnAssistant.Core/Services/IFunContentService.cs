namespace BigDamnAssistant.Core.Services;

public interface IFunContentService
{
    Task<string> GenerateJokeAsync(CancellationToken cancellationToken = default);
    Task<string> GenerateFactAsync(string? topic = null, CancellationToken cancellationToken = default);
}
