using System.ComponentModel.DataAnnotations;

namespace Sloffer.Api.Contracts;

/// <summary>Query parameters for <c>GET /api/jobs</c>. Out-of-range values return 400.</summary>
public sealed record JobListQuery
{
    public const int MaxPageSize = 100;
    public const int MaxPostedWithinHours = 24;

    /// <summary>1-based page number.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "page must be 1 or greater.")]
    public int Page { get; init; } = 1;

    /// <summary>Items per page, 1 to 100.</summary>
    [Range(1, MaxPageSize, ErrorMessage = "pageSize must be between 1 and 100.")]
    public int PageSize { get; init; } = 20;

    /// <summary>Case-insensitive match on title or company.</summary>
    public string? Q { get; init; }

    /// <summary>Case-insensitive "contains" match on location.</summary>
    public string? Location { get; init; }

    /// <summary>Exact source name, e.g. "jobicy".</summary>
    public string? Source { get; init; }

    /// <summary>Only jobs posted within this many hours, 1 to 24.</summary>
    [Range(1, MaxPostedWithinHours, ErrorMessage = "postedWithinHours must be between 1 and 24.")]
    public int PostedWithinHours { get; init; } = MaxPostedWithinHours;
}

/// <summary>One page of jobs, newest first.</summary>
public sealed record JobListResponse(IReadOnlyList<JobListItem> Items, int Page, int PageSize, int TotalCount);

/// <param name="DescriptionExcerpt">First 300 characters of the description as plain text (HTML tags stripped), or null.</param>
public sealed record JobListItem(
    Guid Id,
    string Source,
    string Url,
    string Title,
    string Company,
    string? Location,
    string? EmploymentTypes,
    string? Level,
    DateTimeOffset PostedAt,
    string? DescriptionExcerpt);

/// <param name="Description">Description as returned by the source (HTML or text). Data only.</param>
public sealed record JobDetailResponse(
    Guid Id,
    string Source,
    string ExternalId,
    string Url,
    string Title,
    string Company,
    string? Location,
    string? EmploymentTypes,
    string? Level,
    string? Description,
    DateTimeOffset PostedAt,
    DateTimeOffset FetchedAt);
