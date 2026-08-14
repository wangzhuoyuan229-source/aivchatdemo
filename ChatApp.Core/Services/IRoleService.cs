using ChatApp.Core.Models;

namespace ChatApp.Core.Services;

public interface IRoleService
{
    Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken ct = default);

    Task<Role?> GetAsync(int id, CancellationToken ct = default);

    Task<Role> CreateAsync(Role role, CancellationToken ct = default);

    Task UpdateAsync(Role role, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Ensures the preset roles exist in the database (idempotent).</summary>
    Task EnsurePresetsSeededAsync(CancellationToken ct = default);
}
