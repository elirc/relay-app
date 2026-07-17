using Microsoft.EntityFrameworkCore;

namespace Relay.Api.Contracts.Common;

public static class QueryableExtensions
{
    /// <summary>
    /// Materializes a page of a (already-ordered) query into a <see cref="PagedResult{T}"/>,
    /// counting the total before applying Skip/Take.
    /// </summary>
    public static async Task<PagedResult<TResult>> ToPagedResultAsync<TSource, TResult>(
        this IQueryable<TSource> source,
        PaginationQuery pagination,
        Func<TSource, TResult> map,
        CancellationToken cancellationToken = default)
    {
        var total = await source.CountAsync(cancellationToken);
        var items = await source
            .Skip(pagination.Skip)
            .Take(pagination.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TResult>(
            items.Select(map).ToList(),
            pagination.NormalizedPage,
            pagination.NormalizedPageSize,
            total);
    }
}
