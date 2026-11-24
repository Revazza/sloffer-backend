using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Sloffer.Jobs.Fetching;

namespace Sloffer.Jobs.Matching;

/// <param name="Score">0-100. 0 means a hard rule failed.</param>
/// <param name="Reasons">Plain-language reasons, in a fixed rule order.</param>
/// <param name="Version">The <see cref="JobScorer.Version"/> that produced this result.</param>
public sealed record ScoreResult(int Score, IReadOnlyList<string> Reasons, int Version);

/// <summary>
/// Scores a job 0-100 against the owner's profile with deterministic rules from <see cref="JobScoringOptions"/>.
/// Pure: no I/O, no clock, same input gives the same output. Listing text is only read, never acted on.
/// </summary>
public sealed partial class JobScorer
{
    /// <summary>Bump when the rules change, so <see cref="JobRescoreService"/> rescores stored jobs.</summary>
    public const int Version = 1;

    private readonly JobScoringOptions _options;
    private readonly TermMatcher _dotNet;
    private readonly TermMatcher _emea;
    private readonly TermMatcher _nonEmea;
    private readonly TermMatcher _remote;
    private readonly TermMatcher _excludedSeniority;
    private readonly TermMatcher _senior;
    private readonly TermMatcher _mid;
    private readonly TermMatcher _lead;
    private readonly TermMatcher _primaryDomain;
    private readonly TermMatcher _secondaryDomain;
    private readonly TermMatcher _stack;
    private readonly TermMatcher _gaps;
    private readonly TermMatcher _visaRelocation;

    public JobScorer(IOptions<JobScoringOptions> options)
    {
        var o = options.Value;
        _options = o;
        _dotNet = new TermMatcher(o.DotNetTerms ?? JobScoringDefaults.DotNetTerms);
        _emea = new TermMatcher(o.EmeaLocations ?? JobScoringDefaults.EmeaLocations);
        _nonEmea = new TermMatcher(o.NonEmeaLocations ?? JobScoringDefaults.NonEmeaLocations);
        _remote = new TermMatcher(o.RemoteTerms ?? JobScoringDefaults.RemoteTerms);
        _excludedSeniority = new TermMatcher(o.ExcludedSeniorityTerms ?? JobScoringDefaults.ExcludedSeniorityTerms);
        _senior = new TermMatcher(o.SeniorTerms ?? JobScoringDefaults.SeniorTerms);
        _mid = new TermMatcher(o.MidTerms ?? JobScoringDefaults.MidTerms);
        _lead = new TermMatcher(o.LeadTerms ?? JobScoringDefaults.LeadTerms);
        _primaryDomain = new TermMatcher(o.PrimaryDomainTerms ?? JobScoringDefaults.PrimaryDomainTerms);
        _secondaryDomain = new TermMatcher(o.SecondaryDomainTerms ?? JobScoringDefaults.SecondaryDomainTerms);
        _stack = new TermMatcher(o.StackTerms ?? JobScoringDefaults.StackTerms);
        _gaps = new TermMatcher(o.GapTerms ?? JobScoringDefaults.GapTerms);
        _visaRelocation = new TermMatcher(o.VisaRelocationTerms ?? JobScoringDefaults.VisaRelocationTerms);
    }

    public ScoreResult Score(FetchedJob job)
    {
        var title = job.Title ?? "";
        var description = PlainText(job.Description);
        var location = (job.Location ?? "").Trim();
        var text = $"{title}\n{description}";

        // Hard rules: every failing rule adds its reason; any failure gives 0.
        var failures = new List<string>();

        if (!_dotNet.Any(text))
            failures.Add("not .NET");

        var region = CheckRegion(title, description, location);
        if (region.Failure is not null)
            failures.Add(region.Failure);

        if (_excludedSeniority.FirstMatch(title) is { } excluded)
            failures.Add($"seniority excluded ({excluded})");

        if (failures.Count > 0)
            return new ScoreResult(0, failures, Version);

        // Soft rules: start at BaseScore, each applied rule adds one reason.
        var o = _options;
        var score = o.BaseScore;
        var reasons = new List<string>();

        void Apply(int points, string reason)
        {
            score += points;
            reasons.Add($"{reason} ({Points(points)})");
        }

        if (_dotNet.Any(title))
            Apply(o.TitleDotNetPoints, "title mentions .NET/C#");

        var senior = _senior.Any(title);
        var mid = _mid.Any(title);
        var lead = _lead.Any(title);
        if (senior)
            Apply(o.SeniorPoints, "senior title");
        if (mid)
            Apply(o.MidPoints, "mid-level title");
        if (lead)
            Apply(o.LeadPoints, "lead title");
        if (!senior && !mid && !lead)
            Apply(o.NoLevelPoints, "no level in title");

        // Domain: the larger bonus only, once.
        if (_primaryDomain.FirstMatch(text) is { } primary)
            Apply(o.PrimaryDomainPoints, $"domain: {primary}");
        else if (_secondaryDomain.FirstMatch(text) is { } secondary)
            Apply(o.SecondaryDomainPoints, $"domain: {secondary}");

        var stack = _stack.Matches(text);
        if (stack.Count > 0)
            Apply(Math.Min(stack.Count * o.StackPointsEach, o.StackPointsMax), $"stack: {string.Join(", ", stack)}");

        var gaps = _gaps.Matches(text);
        if (gaps.Count > 0)
            Apply(Math.Max(gaps.Count * o.GapPointsEach, o.GapPointsMin), $"gaps: {string.Join(", ", gaps)}");

        if (_visaRelocation.Any(text))
            Apply(o.VisaRelocationPoints, "visa sponsorship or relocation mentioned");

        if (region.Remote)
            Apply(o.RemotePoints, "remote in EMEA");

        if (MaxYears(text) is { } years)
        {
            var points = years <= o.FewYearsMax ? o.FewYearsPoints : years >= o.ManyYearsMin ? o.ManyYearsPoints : 0;
            Apply(points, $"asks {years}+ years");
        }

        return new ScoreResult(Math.Clamp(score, 0, 100), reasons, Version);
    }

    private (string? Failure, bool Remote) CheckRegion(string title, string description, string location)
    {
        if (location.Length == 0)
        {
            return _remote.Any(title) || _remote.Any(description)
                ? (null, true)
                : ("location unknown", false);
        }

        var remote = _remote.Any(location) || _remote.Any(title);

        if (_emea.Any(location))
            return (null, remote);

        if (_nonEmea.FirstMatch(location) is { } outside)
            return ($"location outside EMEA ({outside})", false);

        if (_remote.Any(location))
            return (null, true);

        return ($"location not in EMEA ({location})", false);
    }

    /// <summary>Largest N in "N+ years" / "N+ yrs", or null.</summary>
    private static int? MaxYears(string text)
    {
        int? max = null;
        foreach (Match match in YearsRegex().Matches(text))
        {
            var years = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            if (max is null || years > max)
                max = years;
        }

        return max;
    }

    private static string PlainText(string? html)
    {
        if (string.IsNullOrEmpty(html))
            return "";

        var text = TagRegex().Replace(html, " ");
        return WebUtility.HtmlDecode(text);
    }

    private static string Points(int points) => points > 0 ? $"+{points}" : points.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"(?<![\p{L}\p{N}_])(\d{1,2})\s*\+\s*(?:years?|yrs?)(?![\p{L}\p{N}_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex YearsRegex();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagRegex();
}
