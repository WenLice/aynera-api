using System.Text.RegularExpressions;
using Elaris.Application.Features.Videos.Models;
using Elaris.Application.Features.Videos.Services.Interfaces;
using Elaris.Domain.Videos.Responses;
using Microsoft.Extensions.Options;

namespace Elaris.Application.Features.Videos.Services.Implementations;

public sealed class BannedWordsGuidelineService : ISpeechGuidelineService
{
    private readonly IntroductionVideoOptions _options;

    public BannedWordsGuidelineService(IOptions<IntroductionVideoOptions> options)
    {
        _options = options.Value;
    }

    public SpeechGuidelineResult Evaluate(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return new SpeechGuidelineResult(true, null, []);
        }

        var banned = (_options.BannedWords ?? [])
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Select(w => w.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (banned.Count == 0)
        {
            return new SpeechGuidelineResult(true, null, []);
        }

        var hits = new List<string>();
        foreach (var word in banned)
        {
            // Word-boundary-ish match for single tokens; phrase match for multi-word entries.
            var pattern = word.Contains(' ', StringComparison.Ordinal)
                ? Regex.Escape(word)
                : $@"\b{Regex.Escape(word)}\b";

            if (Regex.IsMatch(transcript, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                hits.Add(word);
            }
        }

        if (hits.Count == 0)
        {
            return new SpeechGuidelineResult(true, null, []);
        }

        return new SpeechGuidelineResult(
            false,
            "Introduction video contains language that violates community guidelines.",
            hits);
    }
}
