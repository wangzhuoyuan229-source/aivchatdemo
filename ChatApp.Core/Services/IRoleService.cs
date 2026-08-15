using ChatApp.Core.Models;

namespace ChatApp.Core.Services;

public interface IRoleService
{
    Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken ct = default);

    Task<Role?> GetAsync(int id, CancellationToken ct = default);

    Task<Role> CreateAsync(Role role, CancellationToken ct = default);

    Task UpdateAsync(Role role, CancellationToken ct = default);

    /// <summary>Returns the knowledge groups explicitly bound to a role.</summary>
    Task<IReadOnlyList<int>> GetKnowledgeGroupIdsAsync(int roleId, CancellationToken ct = default);

    /// <summary>Replaces all knowledge-group bindings for a role.</summary>
    Task SetKnowledgeGroupIdsAsync(int roleId, IReadOnlyCollection<int> groupIds, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Ensures the preset roles exist in the database (idempotent).</summary>
    Task EnsurePresetsSeededAsync(CancellationToken ct = default);
}
