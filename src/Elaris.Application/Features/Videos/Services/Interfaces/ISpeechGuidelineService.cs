using Elaris.Domain.Videos.Responses;

namespace Elaris.Application.Features.Videos.Services.Interfaces;

public interface ISpeechGuidelineService
{
    SpeechGuidelineResult Evaluate(string transcript);
}
