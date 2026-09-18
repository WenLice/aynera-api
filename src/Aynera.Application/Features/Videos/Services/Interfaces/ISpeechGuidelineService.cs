using Aynera.Domain.Videos.Responses;

namespace Aynera.Application.Features.Videos.Services.Interfaces;

public interface ISpeechGuidelineService
{
    SpeechGuidelineResult Evaluate(string transcript);
}
