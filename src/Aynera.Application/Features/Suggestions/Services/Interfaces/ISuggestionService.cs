using Aynera.Domain.Common;
using Aynera.Domain.Suggestions.Requests;
using Aynera.Domain.Suggestions.Responses;

namespace Aynera.Application.Features.Suggestions.Services.Interfaces;

public interface ISuggestionService
{
    Task<PagedResult<SuggestionAdminDto>> ListAsync(PagedQuery query, CancellationToken cancellationToken);

    Task<SuggestionDto> SubmitAsync(
        SubmitSuggestionRequest request,
        string? clientIp,
        string? userAgent,
        CancellationToken cancellationToken);
}
