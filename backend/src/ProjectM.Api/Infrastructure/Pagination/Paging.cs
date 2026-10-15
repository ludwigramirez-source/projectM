using Microsoft.EntityFrameworkCore;

namespace ProjectM.Api.Infrastructure.Pagination;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public static class Paging
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int? page, int? pageSize) =>
        (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));

    public static async Task<PagedResult<T>> ToPagedAsync<T>(
        this IQueryable<T> query, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, s) = Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((p - 1) * s).Take(s).ToListAsync(ct);
        return new PagedResult<T>(items, p, s, total);
    }
}
