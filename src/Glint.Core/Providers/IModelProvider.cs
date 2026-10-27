namespace Glint.Core.Providers;

public interface IModelProvider
{
    string Name { get; }

    Task<string> GenerateReplyAsync(
        string prompt,
        ReadOnlyMemory<byte> screenshotPng,
        CancellationToken cancellationToken = default);
}
