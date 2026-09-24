using Aynera.Application.Features.Voice.Models;
using Aynera.Domain.Voice.Responses;

namespace Aynera.Application.Features.Voice.Services.Interfaces;

public interface IVoiceAnswerService
{
    /// <summary>Stores (or replaces) the spoken answer to one of the member's chosen prompts.</summary>
    Task<VoiceAnswerDto> UploadAsync(Guid userId, VoiceUploadInput file, CancellationToken cancellationToken);

    Task<IReadOnlyList<VoiceAnswerDto>> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task<VoiceAnswerBytes> GetBytesAsync(Guid userId, string promptId, CancellationToken cancellationToken);

    Task DeleteAsync(Guid userId, string promptId, CancellationToken cancellationToken);
}
