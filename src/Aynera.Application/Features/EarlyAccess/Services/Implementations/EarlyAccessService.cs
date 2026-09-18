using AutoMapper;
using Aynera.Application.Features.Audit.Services.Interfaces;
using Aynera.Application.Features.EarlyAccess.Models;
using Aynera.Application.Features.EarlyAccess.Repositories;
using Aynera.Application.Features.EarlyAccess.Services.Interfaces;
using Aynera.Application.Features.PublicForms.Repositories;
using Aynera.Domain.Audit.Records;
using Aynera.Domain.Audit.Statics;
using Aynera.Domain.Common;
using Aynera.Domain.EarlyAccess.Records;
using Aynera.Domain.EarlyAccess.Requests;
using Aynera.Domain.EarlyAccess.Responses;
using Aynera.Domain.EarlyAccess.Enums;
using Aynera.Domain.EarlyAccess.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aynera.Application.Features.EarlyAccess.Services.Implementations;

public sealed class EarlyAccessService : IEarlyAccessService
{
    private readonly IEarlyAccessSignupRepository _signups;
    private readonly IEarlyAccessCityRepository _cities;
    private readonly IPublicFormRateLimiter _rateLimiter;
    private readonly IMapper _mapper;
    private readonly IAuditWriter _audit;
    private readonly ILogger<EarlyAccessService> _logger;
    private readonly EarlyAccessOptions _options;

    public EarlyAccessService(
        IEarlyAccessSignupRepository signups,
        IEarlyAccessCityRepository cities,
        IPublicFormRateLimiter rateLimiter,
        IMapper mapper,
        IAuditWriter audit,
        ILogger<EarlyAccessService> logger,
        IOptions<EarlyAccessOptions> options)
    {
        _signups = signups;
        _cities = cities;
        _rateLimiter = rateLimiter;
        _mapper = mapper;
        _audit = audit;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<EarlyAccessCityDto>> ListOpenCitiesAsync(CancellationToken cancellationToken)
    {
        var cities = await _cities.ListOpenAsync(cancellationToken);
        return _mapper.Map<List<EarlyAccessCityDto>>(cities);
    }

    public async Task<PagedResult<EarlyAccessSignupAdminDto>> ListSignupsAsync(
        PagedQuery query,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("ListEarlyAccessSignups page {Page} size {PageSize}", query.Page, query.PageSize);
        var (signups, totalCount) = await _signups.ListPageAsync(query.Skip, query.PageSize, cancellationToken);
        return new PagedResult<EarlyAccessSignupAdminDto>(
            _mapper.Map<List<EarlyAccessSignupAdminDto>>(signups),
            query.Page,
            query.PageSize,
            totalCount);
    }

    public async Task<EarlyAccessSignupDto> RegisterAsync(
        JoinEarlyAccessRequest request,
        string? clientIp,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("EarlyAccessRegister starting city {City}", request.City);
        if (!request.IsAdult)
        {
            throw new EarlyAccessException(
                "early_access_adult_required",
                "You must confirm that you are 18 years or older.");
        }

        if (!request.MarketingConsent)
        {
            throw new EarlyAccessException(
                "early_access_consent_required",
                "Marketing email consent is required to join early access.");
        }

        var cityName = request.City.Trim();
        var openCity = await _cities.FindOpenByNameAsync(cityName, cancellationToken)
            ?? throw new EarlyAccessException(
                "early_access_city_not_open",
                $"Early access is not open for {cityName} yet.");

        var interest = ParseInterest(request.Interest);
        var fullName = request.FullName.Trim();
        var email = NormalizeEmail(request.Email);
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

        var (allowed, _) = await _rateLimiter.TryAcquireAsync(
            "early-access",
            clientIp,
            _options.MaxRequestsPerIpPerHour,
            cancellationToken);
        if (!allowed)
        {
            throw new EarlyAccessException(
                "early_access_rate_limited",
                "Too many early-access requests from this network. Please try again later.",
                statusCode: 429);
        }

        var existing = await _signups.FindByEmailAsync(email, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var ua = Truncate(userAgent, 512);
        var ip = Truncate(clientIp, 64);

        if (existing is null)
        {
            var created = await _signups.AddAsync(
                new EarlyAccessSignupRecord(
                    Id: Guid.NewGuid(),
                    FullName: fullName,
                    Email: email,
                    Phone: phone,
                    City: openCity.Name,
                    Interest: interest.ToString(),
                    IsAdult: true,
                    MarketingConsent: true,
                    ClientIp: ip,
                    UserAgent: ua,
                    IsActive: true,
                    CreatedAtUtc: now,
                    UpdatedAtUtc: null,
                    Intent: NormalizeOptional(request.Intent),
                    MeetPreference: NormalizeOptional(request.MeetPreference)),
                cancellationToken);

            await _audit.WriteAsync(
                new AuditEventWriteModel(
                    Action: AuditActions.EarlyAccessJoined,
                    Outcome: AuditOutcomes.Success,
                    Message: "Early access signup created.",
                    ClientIp: ip,
                    SubjectType: AuditSubjectTypes.EarlyAccessSignup,
                    SubjectId: created.Id.ToString("D"),
                    Changes: AuditChanges.Create(
                    [
                        ("email", null, AuditRedaction.MaskEmail(created.Email)),
                        ("phone", null, AuditRedaction.MaskPhone(created.Phone)),
                        ("city", null, created.City),
                        ("interest", null, created.Interest)
                    ]),
                    Metadata: new { created = true }),
                cancellationToken);

            return _mapper.Map<EarlyAccessSignupDto>(created) with { Created = true };
        }

        var updated = await _signups.UpdateAsync(
            existing with
            {
                FullName = fullName,
                Phone = phone,
                City = openCity.Name,
                Interest = interest.ToString(),
                IsAdult = true,
                MarketingConsent = true,
                ClientIp = ip,
                UserAgent = ua,
                IsActive = true,
                Intent = NormalizeOptional(request.Intent),
                MeetPreference = NormalizeOptional(request.MeetPreference),
                UpdatedAtUtc = now
            },
            cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.EarlyAccessJoined,
                Outcome: AuditOutcomes.Success,
                Message: "Early access signup updated.",
                ClientIp: ip,
                SubjectType: AuditSubjectTypes.EarlyAccessSignup,
                SubjectId: updated.Id.ToString("D"),
                Changes: AuditChanges.Create(
                [
                    ("phone", AuditRedaction.MaskPhone(existing.Phone), AuditRedaction.MaskPhone(updated.Phone)),
                    ("city", existing.City, updated.City),
                    ("interest", existing.Interest, updated.Interest)
                ]),
                Metadata: new { created = false }),
            cancellationToken);

        return _mapper.Map<EarlyAccessSignupDto>(updated) with { Created = false };
    }

    private static EarlyAccessInterest ParseInterest(string raw)
    {
        var normalized = raw.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (Enum.TryParse<EarlyAccessInterest>(normalized, ignoreCase: true, out var interest))
        {
            return interest;
        }

        throw new EarlyAccessException(
            "validation_failed",
            "Interest must be Aynera or Aynera Professionals.");
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= max
                ? value
                : value[..max];
}
