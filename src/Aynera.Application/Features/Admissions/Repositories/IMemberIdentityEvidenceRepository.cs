namespace Aynera.Application.Features.Admissions.Repositories;

/// <summary>
/// Cheap identity-evidence probes for eligibility. Deliberately separate from the photo and video
/// repositories: those return full records including the stored bytes, which must never be loaded
/// merely to test a face-match verdict.
/// </summary>
public interface IMemberIdentityEvidenceRepository
{
    /// <summary>True when any of the member's photos or their introduction video was face-match rejected.</summary>
    Task<bool> HasRejectedFaceMatchAsync(Guid userId, CancellationToken cancellationToken);
}
