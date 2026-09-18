using Aynera.Domain.Auth.Enums;
using Microsoft.AspNetCore.Authorization;

namespace Aynera.Infrastructure.Authorization;

public sealed record AccountStateRequirement(
    AccountKind Kind,
    bool AllowInactive = false,
    bool AllowRestricted = false) : IAuthorizationRequirement;
