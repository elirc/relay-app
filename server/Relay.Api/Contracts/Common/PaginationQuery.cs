using Microsoft.AspNetCore.Mvc;

namespace Relay.Api.Contracts.Common;

/// <summary>Bindable page/pageSize query parameters, clamped to safe bounds.</summary>
public sealed class PaginationQuery
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    [FromQuery(Name = "page")]
    public int Page { get; set; } = 1;

    [FromQuery(Name = "pageSize")]
    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>Page number clamped to >= 1.</summary>
    public int NormalizedPage => Page < 1 ? 1 : Page;

    /// <summary>Page size clamped to [1, MaxPageSize].</summary>
    public int NormalizedPageSize => Math.Clamp(PageSize < 1 ? DefaultPageSize : PageSize, 1, MaxPageSize);

    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;
}
