using Finance.Application.Abstractions;
using Finance.Application.Models;
using Finance.Application.Services.Interfaces;
using Finance.Domain.Entities;
using Finance.Domain.Exceptions;

namespace Finance.Application.Services;

public class TagService(ITagRepository tagRepository) : ITagService
{
    public Task<List<TagSummary>> GetAllAsync(CancellationToken cancellationToken = default) =>
        tagRepository.GetSummariesAsync(cancellationToken);

    public async Task<TagSummary> AddAsync(string name, CancellationToken cancellationToken = default)
    {
        var tag = new Tag { Name = Normalize(name) };
        EnsureUnique(await tagRepository.GetAllAsync(cancellationToken), tag.Name, tag.Id);
        await tagRepository.AddAsync(tag, cancellationToken);
        return new TagSummary(tag, 0);
    }

    public async Task<TagSummary> RenameAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        var tags = await tagRepository.GetAllAsync(cancellationToken);
        var tag = tags.SingleOrDefault(t => t.Id == id) ?? throw new NotFoundException(nameof(Tag), id);
        var normalized = Normalize(name);
        EnsureUnique(tags, normalized, id);
        tag.Name = normalized;
        await tagRepository.SaveChangesAsync(cancellationToken);
        return new TagSummary(tag, await tagRepository.CountTransactionsAsync(id, cancellationToken));
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tag = await tagRepository.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(nameof(Tag), id);
        await tagRepository.RemoveAsync(tag, cancellationToken);
    }

    /// <summary>Trimmed, inner spaces collapsed: "  trip   portugal " is "trip portugal".</summary>
    private static string Normalize(string name) => string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    // The database's unique index is the backstop for concurrent requests; this gives the usual case a clear message.
    private static void EnsureUnique(List<Tag> tags, string name, Guid id)
    {
        if (tags.Any(t => t.Id != id && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException($"A tag named \"{name}\" already exists.");
        }
    }
}
