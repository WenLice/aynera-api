using AutoMapper;
using Aynera.Domain.Admissions.Records;
using Aynera.Domain.Auth.Records;
using Aynera.Domain.EarlyAccess.Records;
using Aynera.Domain.Feedback.Records;
using Aynera.Domain.Photos.Records;
using Aynera.Domain.Suggestions.Records;
using Aynera.Domain.Venues.Records;
using Aynera.Domain.Videos.Records;
using Aynera.Persistence.Entities;

namespace Aynera.Infrastructure.Mapping;

public sealed class InfrastructureMappingProfile : Profile
{
    public InfrastructureMappingProfile()
    {
        CreateMap<AppUser, UserRecord>()
            .ConstructUsing(src => new UserRecord(
                src.Id,
                src.PhoneNumber,
                src.PhoneNumberConfirmed,
                src.Email,
                src.EmailConfirmed,
                src.AccountKind.ToString(),
                src.IsActive,
                src.IsDeleted,
                src.IsSuperAdmin,
                src.IsRestricted,
                Roles: Array.Empty<string>()))
            .ForAllMembers(o => o.Ignore());

        CreateMap<MemberProfile, MemberProfileRecord>()
            .ConstructUsing(src => new MemberProfileRecord(
                src.UserId,
                src.Name,
                src.Gender.ToString(),
                src.DateOfBirth,
                src.City,
                src.CityId,
                src.Hometown,
                src.Nickname,
                src.HeightCm,
                src.Work,
                src.Religion))
            .ForAllMembers(o => o.Ignore());

        CreateMap<MemberProfileRecord, MemberProfile>()
            .ForMember(d => d.Name, o => o.MapFrom(s => s.Name.Trim()))
            .ForMember(d => d.City, o => o.MapFrom(s => s.City.Trim()))
            .ForMember(d => d.CityId, o => o.MapFrom(s => s.CityId))
            .ForMember(d => d.HeightCm, o => o.MapFrom(s => s.HeightCm))
            .ForMember(
                d => d.Nickname,
                o => o.MapFrom(s => string.IsNullOrWhiteSpace(s.Nickname) ? null : s.Nickname.Trim()))
            .ForMember(d => d.Hometown, o => o.MapFrom(s => s.Hometown.Trim()))
            .ForMember(
                d => d.Work,
                o => o.MapFrom(s => string.IsNullOrWhiteSpace(s.Work) ? null : s.Work.Trim()))
            .ForMember(
                d => d.Religion,
                o => o.MapFrom(s => string.IsNullOrWhiteSpace(s.Religion) ? null : s.Religion.Trim()))
            .ForMember(d => d.Gender, o => o.Ignore())
            .ForMember(d => d.CreatedAtUtc, o => o.MapFrom(_ => DateTimeOffset.UtcNow))
            .ForMember(d => d.IsDeleted, o => o.MapFrom(_ => false))
            .ForMember(d => d.UpdatedAtUtc, o => o.Ignore())
            .ForMember(d => d.DeletedAtUtc, o => o.Ignore())
            .ForMember(d => d.User, o => o.Ignore());

        // Photos and the introduction video are mapped by hand in their repositories: their bytes
        // come from object storage, not from the row.

        CreateMap<EarlyAccessCity, EarlyAccessCityRecord>()
            .ConstructUsing(src => new EarlyAccessCityRecord(
                src.Id,
                src.Name,
                src.Wave,
                src.SortOrder,
                src.IsActive,
                src.CreatedAtUtc,
                src.UpdatedAtUtc))
            .ForAllMembers(o => o.Ignore());

        CreateMap<EarlyAccessCityRecord, EarlyAccessCity>()
            .ForMember(d => d.DeactivatedAtUtc, o => o.Ignore())
            .ForMember(d => d.IsDeleted, o => o.Ignore())
            .ForMember(d => d.DeletedAtUtc, o => o.Ignore());

        CreateMap<MemberAdmission, MemberAdmissionRecord>()
            .ConstructUsing(src => new MemberAdmissionRecord(
                src.UserId,
                src.State,
                src.SubmittedAtUtc,
                src.DecidedAtUtc,
                src.DecidedByUserId,
                src.DecisionReason,
                src.ReviewNote,
                src.CreatedAtUtc,
                src.UpdatedAtUtc))
            .ForAllMembers(o => o.Ignore());

        CreateMap<MemberConsent, MemberConsentRecord>()
            .ConstructUsing(src => new MemberConsentRecord(
                src.Id,
                src.UserId,
                src.PolicyKind,
                src.Version,
                src.AcceptedAtUtc))
            .ForAllMembers(o => o.Ignore());

        CreateMap<Venue, VenueRecord>()
            .ConstructUsing(src => new VenueRecord(
                src.Id,
                src.Name,
                src.Type,
                src.CityId,
                src.Area,
                src.Address,
                src.PhotoUrls,
                src.ContactName,
                src.ContactEmail,
                src.ContactPhoneE164,
                src.Capacity,
                src.Notes,
                src.IsActive,
                src.CreatedAtUtc,
                src.UpdatedAtUtc))
            .ForAllMembers(o => o.Ignore());

        CreateMap<VenueRecord, Venue>()
            .ForMember(d => d.PhotoUrls, o => o.MapFrom(s => s.PhotoUrls.ToList()))
            .ForMember(d => d.DeactivatedAtUtc, o => o.Ignore())
            .ForMember(d => d.IsDeleted, o => o.Ignore())
            .ForMember(d => d.DeletedAtUtc, o => o.Ignore());

        CreateMap<EarlyAccessSignup, EarlyAccessSignupRecord>()
            .ConstructUsing(src => new EarlyAccessSignupRecord(
                src.Id,
                src.FullName,
                src.Email,
                src.Phone,
                src.City,
                src.Interest,
                src.IsAdult,
                src.MarketingConsent,
                src.ClientIp,
                src.UserAgent,
                src.IsActive,
                src.CreatedAtUtc,
                src.UpdatedAtUtc,
                src.Intent,
                src.MeetPreference))
            .ForAllMembers(o => o.Ignore());

        CreateMap<EarlyAccessSignupRecord, EarlyAccessSignup>()
            .ForMember(d => d.DeactivatedAtUtc, o => o.Ignore())
            .ForMember(d => d.IsDeleted, o => o.Ignore())
            .ForMember(d => d.DeletedAtUtc, o => o.Ignore());

        CreateMap<RefreshSession, RefreshSessionRecord>()
            .ConstructUsing(src => new RefreshSessionRecord(
                src.Id,
                src.UserId,
                src.Audience,
                src.TokenHash,
                src.FamilyId,
                src.DeviceLabel,
                src.CreatedAtUtc,
                src.ExpiresAtUtc,
                src.RevokedAtUtc,
                src.ReplacedAtUtc,
                src.ReplacedBySessionId))
            .ForAllMembers(o => o.Ignore());

        CreateMap<RefreshSessionRecord, RefreshSession>()
            .ForMember(d => d.IsDeleted, o => o.Ignore())
            .ForMember(d => d.DeletedAtUtc, o => o.Ignore());

        CreateMap<Suggestion, SuggestionRecord>()
            .ConstructUsing(src => new SuggestionRecord(
                src.Id,
                src.FullName,
                src.Email,
                src.Phone,
                src.Message,
                src.ClientIp,
                src.UserAgent,
                src.CreatedAtUtc))
            .ForAllMembers(o => o.Ignore());

        CreateMap<SuggestionRecord, Suggestion>()
            .ForMember(d => d.IsActive, o => o.MapFrom(_ => true))
            .ForMember(d => d.DeactivatedAtUtc, o => o.Ignore())
            .ForMember(d => d.IsDeleted, o => o.Ignore())
            .ForMember(d => d.DeletedAtUtc, o => o.Ignore());

        CreateMap<FeedbackSubmission, FeedbackSubmissionRecord>()
            .ConstructUsing(src => new FeedbackSubmissionRecord(
                src.Id,
                src.FullName,
                src.Email,
                src.Phone,
                src.Message,
                src.ClientIp,
                src.UserAgent,
                src.IsExistingUser,
                src.MemberId,
                src.CreatedAtUtc))
            .ForAllMembers(o => o.Ignore());

        CreateMap<FeedbackSubmissionRecord, FeedbackSubmission>()
            .ForMember(d => d.IsActive, o => o.MapFrom(_ => true))
            .ForMember(d => d.DeactivatedAtUtc, o => o.Ignore())
            .ForMember(d => d.IsDeleted, o => o.Ignore())
            .ForMember(d => d.DeletedAtUtc, o => o.Ignore());
    }
}
