using Finance.Application.Models;

namespace Finance.Application.Services.Interfaces;

public interface ITagService
{
    Task<List<TagSummary>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<TagSummary> AddAsync(string name, CancellationToken cancellationToken = default);
    Task<TagSummary> RenameAsync(Guid id, string name, CancellationToken cancellationToken = default);

    /// <summary>Deletes the tag; its transactions just lose it.</summary>
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}
