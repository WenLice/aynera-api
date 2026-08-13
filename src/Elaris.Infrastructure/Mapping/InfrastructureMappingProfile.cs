using AutoMapper;
using Elaris.Domain.Auth.Records;
using Elaris.Domain.EarlyAccess.Records;
using Elaris.Domain.Feedback.Records;
using Elaris.Domain.Photos.Records;
using Elaris.Domain.Suggestions.Records;
using Elaris.Domain.Videos.Records;
using Elaris.Persistence.Entities;

namespace Elaris.Infrastructure.Mapping;

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
                Roles: Array.Empty<string>()))
            .ForAllMembers(o => o.Ignore());

        CreateMap<MemberProfile, MemberProfileRecord>()
            .ConstructUsing(src => new MemberProfileRecord(
                src.UserId,
                src.FirstName,
                src.LastName,
                src.Gender.ToString(),
                src.DateOfBirth,
                src.City,
                src.Religion))
            .ForAllMembers(o => o.Ignore());

        CreateMap<MemberProfileRecord, MemberProfile>()
            .ForMember(d => d.FirstName, o => o.MapFrom(s => s.FirstName.Trim()))
            .ForMember(d => d.LastName, o => o.MapFrom(s => s.LastName.Trim()))
            .ForMember(d => d.City, o => o.MapFrom(s => s.City.Trim()))
            .ForMember(
                d => d.Religion,
                o => o.MapFrom(s => string.IsNullOrWhiteSpace(s.Religion) ? null : s.Religion.Trim()))
            .ForMember(d => d.Gender, o => o.Ignore())
            .ForMember(d => d.CreatedAtUtc, o => o.MapFrom(_ => DateTimeOffset.UtcNow))
            .ForMember(d => d.IsDeleted, o => o.MapFrom(_ => false))
            .ForMember(d => d.UpdatedAtUtc, o => o.Ignore())
            .ForMember(d => d.DeletedAtUtc, o => o.Ignore())
            .ForMember(d => d.User, o => o.Ignore());

        CreateMap<MemberPhoto, MemberPhotoRecord>()
            .ConstructUsing(src => new MemberPhotoRecord(
                src.Id,
                src.UserId,
                src.SortOrder,
                src.ContentType,
                src.ByteSize,
                src.Data,
                src.IsReference,
                src.FaceMatchStatus.ToString(),
                src.FaceMatchScore,
                src.CreatedAtUtc))
            .ForAllMembers(o => o.Ignore());

        CreateMap<MemberPhotoRecord, MemberPhoto>()
            .ForMember(
                d => d.Id,
                o => o.MapFrom(s => s.Id == Guid.Empty ? Guid.NewGuid() : s.Id))
            .ForMember(d => d.FaceMatchStatus, o => o.Ignore())
            .ForMember(
                d => d.CreatedAtUtc,
                o => o.MapFrom(s => s.CreatedAtUtc == default ? DateTimeOffset.UtcNow : s.CreatedAtUtc))
            .ForMember(d => d.IsDeleted, o => o.MapFrom(_ => false))
            .ForMember(d => d.UpdatedAtUtc, o => o.Ignore())
            .ForMember(d => d.DeletedAtUtc, o => o.Ignore())
            .ForMember(d => d.User, o => o.Ignore());

        CreateMap<MemberIntroductionVideo, IntroductionVideoRecord>()
            .ConstructUsing(src => new IntroductionVideoRecord(
                src.UserId,
                src.ContentType,
                src.ByteSize,
                src.Data,
                src.FaceMatchStatus.ToString(),
                src.FaceMatchScore,
                src.GuidelinePassed,
                src.GuidelineDetail,
                src.Transcript,
                src.CreatedAtUtc,
                src.UpdatedAtUtc))
            .ForAllMembers(o => o.Ignore());

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
                src.UpdatedAtUtc))
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
                src.CreatedAtUtc))
            .ForAllMembers(o => o.Ignore());

        CreateMap<FeedbackSubmissionRecord, FeedbackSubmission>()
            .ForMember(d => d.IsActive, o => o.MapFrom(_ => true))
            .ForMember(d => d.DeactivatedAtUtc, o => o.Ignore())
            .ForMember(d => d.IsDeleted, o => o.Ignore())
            .ForMember(d => d.DeletedAtUtc, o => o.Ignore());
    }
}
