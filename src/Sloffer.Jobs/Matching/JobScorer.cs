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
    public const int Version = 5;

    private readonly JobScoringOptions _options;
    private readonly TermMatcher _roleReject;
    private readonly TermMatcher _roleRejectUnlessEngineering;
    private readonly TermMatcher _engineering;
    private readonly TermMatcher _dotNet;
    private readonly TermMatcher _otherLanguage;
    private readonly TermMatcher _alternativeList;
    private readonly TermMatcher _weakMarker;
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
        _roleReject = new TermMatcher(o.RoleRejectTitleTerms ?? JobScoringDefaults.RoleRejectTitleTerms);
        _roleRejectUnlessEngineering = new TermMatcher(
            o.RoleRejectUnlessEngineeringTitleTerms ?? JobScoringDefaults.RoleRejectUnlessEngineeringTitleTerms);
        _engineering = new TermMatcher(o.EngineeringTitleTerms ?? JobScoringDefaults.EngineeringTitleTerms);
        _dotNet = new TermMatcher(o.DotNetTerms ?? JobScoringDefaults.DotNetTerms);
        _otherLanguage = new TermMatcher(o.OtherLanguageTerms ?? JobScoringDefaults.OtherLanguageTerms);
        _alternativeList = new TermMatcher(o.AlternativeListTerms ?? JobScoringDefaults.AlternativeListTerms);
        _weakMarker = new TermMatcher(o.WeakMarkerTerms ?? JobScoringDefaults.WeakMarkerTerms);
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

        // The title decides what the job is: the description never turns a non-engineering role into a match.
        if (CheckRole(title) is { } role)
            failures.Add(role);

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

        // .NET centrality: core earns points; only mentioned caps the score below the list threshold.
        var centrality = DotNetCentrality(title, job.Description);
        if (centrality.Core)
            Apply(o.DotNetCorePoints, $".NET core: {centrality.Why}");
        else
            reasons.Add($".NET only mentioned: {centrality.Why}; score capped at {o.DotNetMentionedMaxScore}");

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

        if (!centrality.Core)
            score = Math.Min(score, o.DotNetMentionedMaxScore);

        return new ScoreResult(Math.Clamp(score, 0, 100), reasons, Version);
    }

    /// <summary>
    /// Is .NET core to the job, or only mentioned? Core: a strong .NET term in the title, or at least
    /// <c>DotNetCoreMinMentions</c> strong mentions in the description. A mention is weak when its clause offers another
    /// language as an alternative ("Java, Python or C#", "e.g. Java, C#", "Swift/Kotlin/C#"), when its clause carries a
    /// "nice to have" marker ("C# is a plus"), or when it comes after a "nice to have / bonus / preferred" heading.
    /// </summary>
    private (bool Core, string Why) DotNetCentrality(string title, string? descriptionHtml)
    {
        var titleStrong = CountStrong(title, weakFrom: int.MaxValue);
        if (titleStrong > 0)
            return (true, "in title");

        var description = StructuredText(descriptionHtml);
        var strong = CountStrong(description, WeakSectionStart(description));
        if (strong >= _options.DotNetCoreMinMentions)
            return (true, $"{strong} mentions in the requirements");

        if (_dotNet.Any(title))
            return (false, "as an alternative in the title");
        return (false, strong == 1 ? "1 strong mention in the description" : "as an alternative or nice to have");
    }

    /// <summary>.NET-family matches that are neither in an alternatives list, nor marked nice-to-have, nor after <paramref name="weakFrom"/>.</summary>
    private int CountStrong(string text, int weakFrom)
    {
        var count = 0;
        foreach (var position in _dotNet.Positions(text))
        {
            if (position >= weakFrom)
                continue;
            var clause = ClauseAround(text, position);
            if (_weakMarker.Any(clause))
                continue;
            if (_otherLanguage.Any(clause) && (_alternativeList.Any(clause) || clause.Contains('/')))
                continue;
            count++;
        }

        return count;
    }

    /// <summary>
    /// Index of the first short heading line that marks the rest as optional ("Nice to have", "Bonus points:",
    /// "Preferred qualifications"), or int.MaxValue. A line with a language name or a verb ("Docker is a plus") is a
    /// requirement, not a heading.
    /// </summary>
    private int WeakSectionStart(string text)
    {
        var index = 0;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length is > 0 and <= 50
                && _weakMarker.Any(trimmed)
                && !_dotNet.Any(trimmed)
                && !_otherLanguage.Any(trimmed)
                && !SentenceVerbRegex().IsMatch(trimmed))
                return index;
            index += line.Length + 1;
        }

        return int.MaxValue;
    }

    /// <summary>The line around <paramref name="position"/>, cut at sentence ends (". ", "; ", but not after "e.g.").</summary>
    private static string ClauseAround(string text, int position)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, position - 1)) + 1;
        var lineEnd = text.IndexOf('\n', position);
        var line = text[lineStart..(lineEnd < 0 ? text.Length : lineEnd)];
        var offset = position - lineStart;

        var start = 0;
        foreach (Match end in SentenceEndRegex().Matches(line))
        {
            if (end.Index + end.Length <= offset)
                start = end.Index + end.Length;
            else if (end.Index >= offset)
                return line[start..end.Index];
        }

        return line[start..];
    }

    /// <summary>Plain text that keeps block structure: paragraphs, list items, headings and breaks become lines.</summary>
    private static string StructuredText(string? html)
    {
        if (string.IsNullOrEmpty(html))
            return "";

        var text = BlockTagRegex().Replace(html, "\n");
        text = TagRegex().Replace(text, " ");
        return WebUtility.HtmlDecode(text);
    }

    /// <summary>"role: term" when the title names a non-engineering (or too junior) role, otherwise null.</summary>
    private string? CheckRole(string title)
    {
        if (_roleReject.FirstMatch(title) is { } rejected)
            return $"role: {rejected}";

        if (_engineering.Any(title))
            return null;

        return _roleRejectUnlessEngineering.FirstMatch(title) is { } term
            ? $"role: {term}"
            : "role: title is not an engineering role";
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

    [GeneratedRegex(@"</?(?:p|li|ul|ol|div|h[1-6]|tr|br)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTagRegex();

    [GeneratedRegex(@"(?<!\b(?:e\.g|i\.e|etc))[.;!?](?=\s)", RegexOptions.IgnoreCase)]
    private static partial Regex SentenceEndRegex();

    [GeneratedRegex(@"\b(?:is|are|would be|will be|ist|sind)\s+(?:a|an|ein|eine)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex SentenceVerbRegex();
}
