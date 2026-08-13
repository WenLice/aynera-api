using AutoMapper;
using Elaris.Application.Features.PublicForms.Repositories;
using Elaris.Application.Features.Suggestions.Models;
using Elaris.Application.Features.Suggestions.Repositories;
using Elaris.Application.Features.Suggestions.Services.Interfaces;
using Elaris.Domain.Suggestions.Records;
using Elaris.Domain.Suggestions.Requests;
using Elaris.Domain.Suggestions.Responses;
using Elaris.Domain.Suggestions.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elaris.Application.Features.Suggestions.Services.Implementations;

public sealed class SuggestionService : ISuggestionService
{
    private readonly ISuggestionRepository _suggestions;
    private readonly IPublicFormRateLimiter _rateLimiter;
    private readonly IMapper _mapper;
    private readonly ILogger<SuggestionService> _logger;
    private readonly SuggestionOptions _options;

    public SuggestionService(
        ISuggestionRepository suggestions,
        IPublicFormRateLimiter rateLimiter,
        IMapper mapper,
        ILogger<SuggestionService> logger,
        IOptions<SuggestionOptions> options)
    {
        _suggestions = suggestions;
        _rateLimiter = rateLimiter;
        _mapper = mapper;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<SuggestionDto> SubmitAsync(
        SubmitSuggestionRequest request,
        string? clientIp,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("SubmitSuggestion starting");
        var message = request.Message.Trim();
        if (message.Length > _options.MaxMessageLength)
        {
            throw new SuggestionException(
                "suggestion_message_too_long",
                $"Message must be at most {_options.MaxMessageLength} characters.");
        }

        var fullName = request.FullName.Trim();
        var email = request.Email.Trim().ToLowerInvariant();
        var phone = request.Phone.Trim();

        var (allowed, _) = await _rateLimiter.TryAcquireAsync(
            "suggestions",
            clientIp,
            _options.MaxRequestsPerIpPerHour,
            cancellationToken);
        if (!allowed)
        {
            throw new SuggestionException(
                "suggestion_rate_limited",
                "Too many suggestion submissions from this network. Please try again later.",
                statusCode: 429);
        }

        var created = await _suggestions.AddAsync(
            new SuggestionRecord(
                Id: Guid.NewGuid(),
                FullName: fullName,
                Email: email,
                Phone: phone,
                Message: message,
                ClientIp: Truncate(clientIp, 64),
                UserAgent: Truncate(userAgent, 512),
                CreatedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);

        return _mapper.Map<SuggestionDto>(created);
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= max
                ? value
                : value[..max];
}
