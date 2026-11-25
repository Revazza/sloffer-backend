using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sloffer.Api.Contracts;
using Sloffer.Api.Jobs;
using Sloffer.Data;

namespace Sloffer.Api.Controllers;

[ApiController]
[Route("api/jobs")]
public sealed class JobsController(SlofferDbContext db, TimeProvider time, IOptions<JobListOptions> options) : ControllerBase
{
    /// <summary>
    /// Recent jobs, filtered, sorted and paged in the database. By default sorted by score (highest first, unscored last,
    /// then newest, then Id); <c>sort=posted</c> sorts newest first. By default lists the whole job age window
    /// (<c>JobFetch:MaxAgeDays</c>, 7 days by default), hides scored jobs below <c>minScore</c> (1, so score-0 jobs are
    /// hidden) and hides duplicates (<c>includeDuplicates=true</c> shows them, with <c>duplicateOfId</c> pointing at the original).
    /// </summary>
    [HttpGet]
    [ProducesResponseType<JobListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<JobListResponse>> List([FromQuery] JobListQuery query, CancellationToken cancellationToken)
    {
        var postedWithinHours = query.PostedWithinHours ?? options.Value.DefaultPostedWithinHours;
        var postedSince = time.GetUtcNow().AddHours(-postedWithinHours);

        var jobs = db.Jobs.AsNoTracking().Where(j => j.PostedAt >= postedSince);

        if (!query.IncludeDuplicates)
            jobs = jobs.Where(j => j.DuplicateOfId == null);

        if (!string.IsNullOrWhiteSpace(query.Source))
            jobs = jobs.Where(j => j.Source == query.Source);

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var q = query.Q.Trim().ToLower();
            jobs = jobs.Where(j => j.Title.ToLower().Contains(q) || j.Company.ToLower().Contains(q));
        }

        if (!string.IsNullOrWhiteSpace(query.Location))
        {
            var location = query.Location.Trim().ToLower();
            jobs = jobs.Where(j => j.Location != null && j.Location.ToLower().Contains(location));
        }

        if (query.MinScore > 0)
            jobs = jobs.Where(j => j.Score == null || j.Score >= query.MinScore);

        var totalCount = await jobs.CountAsync(cancellationToken);

        var ordered = query.Sort == JobListQuery.SortByPosted
            ? jobs.OrderByDescending(j => j.PostedAt).ThenBy(j => j.Id)
            : jobs.OrderBy(j => j.Score == null).ThenByDescending(j => j.Score).ThenByDescending(j => j.PostedAt).ThenBy(j => j.Id);

        var page = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(j => new
            {
                j.Id, j.Source, j.Url, j.Title, j.Company, j.Location, j.EmploymentTypes, j.Level, j.PostedAt, j.Description,
                j.DuplicateOfId, j.Score, j.ScoreReasons,
            })
            .ToListAsync(cancellationToken);

        var items = page
            .Select(j => new JobListItem(
                j.Id, j.Source, j.Url, j.Title, j.Company, j.Location, j.EmploymentTypes, j.Level, j.PostedAt,
                HtmlText.Excerpt(j.Description), j.DuplicateOfId, j.Score, j.ScoreReasons ?? []))
            .ToList();

        return Ok(new JobListResponse(items, query.Page, query.PageSize, totalCount));
    }

    /// <summary>One job with its full description.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<JobDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobDetailResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var job = await db.Jobs
            .AsNoTracking()
            .Where(j => j.Id == id)
            .Select(j => new
            {
                j.Id, j.Source, j.ExternalId, j.Url, j.Title, j.Company, j.Location, j.EmploymentTypes, j.Level,
                j.Description, j.PostedAt, j.FetchedAt, j.Score, j.ScoreReasons,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
            return NotFound();

        return Ok(new JobDetailResponse(
            job.Id, job.Source, job.ExternalId, job.Url, job.Title, job.Company, job.Location, job.EmploymentTypes,
            job.Level, job.Description, job.PostedAt, job.FetchedAt, job.Score, job.ScoreReasons ?? []));
    }
}
