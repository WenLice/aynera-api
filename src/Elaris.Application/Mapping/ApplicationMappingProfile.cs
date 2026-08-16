using AutoMapper;
using Elaris.Domain.Auth.Records;
using Elaris.Domain.Auth.Responses;
using Elaris.Domain.EarlyAccess.Enums;
using Elaris.Domain.EarlyAccess.Records;
using Elaris.Domain.EarlyAccess.Responses;
using Elaris.Domain.Feedback.Records;
using Elaris.Domain.Feedback.Responses;
using Elaris.Domain.Photos.Records;
using Elaris.Domain.Photos.Responses;
using Elaris.Domain.Suggestions.Records;
using Elaris.Domain.Suggestions.Responses;
using Elaris.Domain.Videos.Records;
using Elaris.Domain.Videos.Responses;

namespace Elaris.Application.Mapping;

public sealed class ApplicationMappingProfile : Profile
{
    public ApplicationMappingProfile()
    {
        CreateMap<MemberProfileRecord, MemberProfileDto>()
            .ConstructUsing(src => new MemberProfileDto(
                src.FirstName,
                src.LastName,
                src.Gender,
                src.DateOfBirth,
                src.City,
                src.Religion))
            .ForAllMembers(o => o.Ignore());

        CreateMap<MemberAdminRecord, MemberAdminDto>()
            .ConstructUsing(src => new MemberAdminDto(
                src.Id,
                src.Phone,
                src.PhoneConfirmed,
                src.Email,
                src.EmailConfirmed,
                src.IsActive,
                src.IsRestricted,
                src.CreatedAtUtc,
                src.FirstName,
                src.LastName,
                src.Gender,
                src.DateOfBirth,
                src.City,
                src.Religion))
            .ForAllMembers(o => o.Ignore());

        CreateMap<UserRecord, AuthAccountDto>()
            .ConstructUsing(src => new AuthAccountDto(
                src.Id,
                src.Phone,
                src.PhoneConfirmed,
                src.Email,
                src.EmailConfirmed,
                src.AccountKind,
                src.IsActive,
                src.IsDeleted,
                src.IsSuperAdmin,
                src.IsRestricted,
                src.Roles,
                Profile: null))
            .ForAllMembers(o => o.Ignore());

        CreateMap<MemberPhotoRecord, MemberPhotoDto>()
            .ConstructUsing(src => new MemberPhotoDto(
                src.Id,
                src.SortOrder,
                src.ContentType,
                src.ByteSize,
                src.IsReference,
                src.FaceMatchStatus,
                src.FaceMatchScore,
                src.CreatedAtUtc))
            .ForAllMembers(o => o.Ignore());

        CreateMap<IntroductionVideoRecord, IntroductionVideoDto>()
            .ConstructUsing(src => new IntroductionVideoDto(
                src.UserId,
                src.ContentType,
                src.ByteSize,
                src.FaceMatchStatus,
                src.FaceMatchScore,
                src.GuidelinePassed,
                src.GuidelineDetail,
                src.CreatedAtUtc,
                src.UpdatedAtUtc))
            .ForAllMembers(o => o.Ignore());

        CreateMap<EarlyAccessCityRecord, EarlyAccessCityDto>()
            .ConstructUsing(src => new EarlyAccessCityDto(
                src.Id,
                src.Name,
                src.Wave,
                src.SortOrder,
                src.IsActive))
            .ForAllMembers(o => o.Ignore());

        CreateMap<EarlyAccessSignupRecord, EarlyAccessSignupDto>()
            .ConstructUsing(src => new EarlyAccessSignupDto(
                src.Id,
                src.Email,
                src.City,
                FormatInterest(src.Interest),
                Created: false))
            .ForAllMembers(o => o.Ignore());

        CreateMap<EarlyAccessSignupRecord, EarlyAccessSignupAdminDto>()
            .ConstructUsing(src => new EarlyAccessSignupAdminDto(
                src.Id,
                src.FullName,
                src.Email,
                src.Phone,
                src.City,
                FormatInterest(src.Interest),
                src.IsAdult,
                src.MarketingConsent,
                src.IsActive,
                src.CreatedAtUtc,
                src.UpdatedAtUtc))
            .ForAllMembers(o => o.Ignore());

        CreateMap<SuggestionRecord, SuggestionDto>()
            .ConstructUsing(src => new SuggestionDto(src.Id, src.CreatedAtUtc))
            .ForAllMembers(o => o.Ignore());

        CreateMap<SuggestionRecord, SuggestionAdminDto>()
            .ConstructUsing(src => new SuggestionAdminDto(
                src.Id,
                src.FullName,
                src.Email,
                src.Phone,
                src.Message,
                src.CreatedAtUtc))
            .ForAllMembers(o => o.Ignore());

        CreateMap<FeedbackSubmissionRecord, FeedbackSubmissionDto>()
            .ConstructUsing(src => new FeedbackSubmissionDto(
                src.Id,
                src.IsExistingUser,
                src.CreatedAtUtc))
            .ForAllMembers(o => o.Ignore());

        CreateMap<FeedbackSubmissionRecord, FeedbackSubmissionAdminDto>()
            .ConstructUsing(src => new FeedbackSubmissionAdminDto(
                src.Id,
                src.FullName,
                src.Email,
                src.Phone,
                src.Message,
                src.IsExistingUser,
                src.MemberId,
                src.CreatedAtUtc))
            .ForAllMembers(o => o.Ignore());
    }

    private static string FormatInterest(string stored) =>
        string.Equals(stored, nameof(EarlyAccessInterest.ElarisProfessionals), StringComparison.OrdinalIgnoreCase)
            ? "Elaris Professionals"
            : "Elaris";
}
