using Elaris.Domain.Common;
using Elaris.Domain.Suggestions.Requests;
using Elaris.Domain.Suggestions.Responses;

namespace Elaris.Application.Features.Suggestions.Services.Interfaces;

public interface ISuggestionService
{
    Task<PagedResult<SuggestionAdminDto>> ListAsync(PagedQuery query, CancellationToken cancellationToken);

    Task<SuggestionDto> SubmitAsync(
        SubmitSuggestionRequest request,
        string? clientIp,
        string? userAgent,
        CancellationToken cancellationToken);
}
