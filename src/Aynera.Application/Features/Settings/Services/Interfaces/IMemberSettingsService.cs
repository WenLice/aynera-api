using Aynera.Domain.Settings.Requests;
using Aynera.Domain.Settings.Responses;

namespace Aynera.Application.Features.Settings.Services.Interfaces;

public interface IMemberSettingsService
{
    Task<MemberSettingsDto> GetAsync(Guid userId, CancellationToken cancellationToken);

    Task<MemberSettingsDto> UpdateAsync(Guid userId, UpdateMemberSettingsRequest request, CancellationToken cancellationToken);
}
