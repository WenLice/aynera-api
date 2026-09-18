# Implemented workflow ownership and consistency map

Date: 2026-09-08. This refines the [product domain review](product-domain-review.md). The baseline observations below describe the implementation before extraction. The first [registration slice](registration-workflow.md) is now implemented; registration, lifecycle, recovery and moderation consistency are implemented; the baseline sections describe the original extraction analysis.

Lifecycle update: activation, deactivation and deletion now live in `IAccountLifecycleService` / `AccountLifecycleService` under Features/Users. MembersController delegates the same endpoints to that service. This first step preserves behavior; lifecycle transactions, pending-email cleanup and expired-token reactivation recovery remain outstanding.

Deletion update: member deletion now wraps session/profile/media soft deletion, pending verification-email cancellation and Identity account soft deletion in one WorkflowTransaction. Failure or cancellation rolls back those stores together. An account-scoped advisory lock serializes deletion requests. Audit remains best effort after commit. Deactivation atomicity and expired-token reactivation recovery are still outstanding.

Deactivation update: account deactivation and refresh-session revocation now commit in one WorkflowTransaction, using the same account lock as deletion. Failure/cancellation rolls back both writes, and a subsequent retry can complete. Existing validation and reactivation behavior are preserved; already-inactive accounts still follow the repository's existing rejection rules. Access-token invalidation, expired-token recovery and moderation transactions are not changed by this step.

Reactivation recovery update: authenticated `POST /members/me/reactivate` is unchanged for a still-valid member access token. Expired-token recovery is `POST /members/reactivate/request` then `POST /members/reactivate/recover` with a reactivation-purpose OTP. Identifier or user id alone cannot reactivate. Login, OTP login and password reset still reject inactive accounts. Recovery does not restore revoked refresh sessions or issue tokens. Restricted, deleted and non-member accounts are not eligible. Activation uses the same `account:{userId:D}` lock as deactivation and deletion. Access-JWT invalidation remains separate work. Moderation transactions are not changed by this step.

## Decision

Use business workflows as the extraction unit. Keep the HTTP contracts stable (the `admin/*` and `public/*` compatibility aliases were removed on 2026-09-13; only controller-prefixed routes remain). Keep the modular monolith and shared PostgreSQL database; introduce an application-level transaction abstraction implemented in Infrastructure when a workflow needs atomic persistence. Do not expose EF transactions to controllers or Domain.

The first implementation slice should be **member registration**, including profile ownership and a recoverable verification-delivery path. Moving methods alone would preserve partial-registration failure modes. Accounts, Profiles and Auth are collaborators in that workflow, not three independently committed steps.

## Operation map

| Operation and current endpoint | Current implementation | Proposed owner/coordinator | Required collaborators and invariants |
|---|---|---|---|
| Register: `POST /members/register` | `AuthService.CreateMemberAsync` | Registration use case under Accounts/Users | Auth creates identity/credentials and member role; Profiles creates initial profile; identity, role and profile must persist together. No login tokens or admission approval implied. |
| Verify email: `POST /auth/verifyemail` | `AuthService.ConfirmEmailAsync` | Auth | Owns identifier proof, token validation and confirmation. Must remain independent of profile review and liveness verification. |
| OTP/password login, password reset/set, refresh/logout | AuthService and auth interfaces | Auth | Account eligibility read; credential/session writes; shared profile projection may enrich token response without transferring profile ownership to Auth. |
| Self account: `GET /members/me` | `AuthService.GetMeAsync` | Account query composition | Account plus private self-profile DTO. Preserve current response; no need for a client fan-out to multiple endpoints. |
| Admin self: `GET /admins/me` | UserManagementService | Account query composition | Live admin account and super-admin flag; no member profile. |
| List/detail members: `GET /members/GetAll`, `GET /members/{id}` | UserManagementService + user repository projection | Member queries, composing Accounts/Profiles/Media | Admin-only private projection, search/filter/page semantics, omission of deleted members. Do not reuse it for future discovery. |
| Self photo/video CRUD: `/photos/*`, `/introduction-video/*` | PhotosController → Photos service; IntroductionVideoController → Videos service | Media | Ownership, count/type/size rules, reference photo and video dependencies. Future verification consumes outcomes separately. |
| Admin media reads: `/photos/{id}/{photoId}`, `/introduction-video/{id}/content` | UserManagementService checks actor/target then delegates media read | Authorized member query composition + Media | Check actor and member visibility before bytes are read; Media retains storage/missing-content behavior. |
| Self deactivate/reactivate: `POST /members/me/deactivate`, `/members/me/reactivate`, `/members/reactivate/request`, `/members/reactivate/recover` | AccountLifecycleService | Account lifecycle | Deactivation plus session revocation must be consistent. Authenticated reactivation is preserved. Expired-token recovery proves possession of a reactivation OTP; it never clears a moderation restriction or restores revoked refresh sessions. |
| Delete: `DELETE /members/me` | AuthService | Account lifecycle coordinator | Account identifiers, profile, media and refresh-session cleanup form the currently implemented deletion boundary. Future shared-space retention remains an explicit later policy. |
| Restrict/unrestrict: `POST /members/{id}/restrict`, `/unrestrict` | UserManagementService | Safety moderation use case, using Accounts/Auth | Super-admin permission retained. Restriction must remain distinct from account deactivation. Restriction and session revocation now share a transaction and account locks. Unrestriction must not reactivate a self-deactivated account or restore revoked tokens. |
| Create/list/activate/deactivate admins: `/admins/GetAll`, `/admins/Create` and nested actions | UserManagementService | Account administration | Keep super-admin checks, no self-deactivation, last-admin protections and session revocation. Last-admin counts and transitions share the admins:lifecycle lock and transaction. The account kind is a property, not a service-layer domain. |
| Early access/cities, suggestions, public feedback | Existing domain services | Existing owners | Leave these outside registration. A lead is not an account, and public support feedback is not a safety case or post-date outcome. |

## Observed failure boundaries

### Registration

Current sequence:

1. Validate adulthood and normalize contact fields.
2. UserRepository creates the Identity user and adds its member role.
3. MemberProfileRepository creates and saves the profile.
4. Generate verification token/link and send email inline.
5. Write audit and return the account/profile DTO.

Evidence: [AuthService](../src/Aynera.Application/Features/Auth/Services/Implementations/AuthService.cs), [UserRepository](../src/Aynera.Infrastructure/Repositories/UserRepository.cs), [MemberProfileRepository](../src/Aynera.Infrastructure/Repositories/MemberProfileRepository.cs).

There is no explicit transaction spanning these writes in the current use case. A failed role/profile write can leave earlier persistence behind. Email token generation, invalid email-link configuration or delivery failure happens after account/profile persistence; the request can fail although the account exists. A registration retry can then produce a duplicate conflict. Existing successful round-trip tests do not prove rollback/recovery behavior.

Proposed contract:

- Commit account, member role, profile and durable verification-delivery intent atomically.
- Keep token generation/confirmation semantics in Auth. Keep initial-profile construction and persistence in Profiles.
- Deliver email after commit; never hold the database transaction open while calling a provider. A small durable delivery queue/outbox supports retry. Do not silently swallow delivery errors or use an untracked background task.
- Registration success means the account exists and delivery is scheduled, not that a provider has delivered the message. Preserve the existing response shape, while documenting this timing change.
- Configure and validate the verification-link base URL before accepting registrations. Do not queue plaintext passwords. Minimize sensitive delivery payload and define queue cleanup.
- Keep existing duplicate-contact rules. Do not make registration idempotent by returning an existing account to an unauthenticated caller.
- Expose a safe resend/recovery path only with explicit anti-enumeration and rate-limit behavior; do not repurpose registration retries as resends.

This is a behavioral improvement plus extraction. It needs a migration for durable delivery work, not merely moved files. An outbox without a working consumer, retry behavior and observable failures is not a completed slice.

### Account lifecycle

Deletion currently soft-deletes sessions, profile, photos and video before deleting the account and freeing its identifiers. A failure midway leaves a partially deleted account. The existing shared database allows an atomic transaction across these currently implemented stores. Future external media cleanup can be queued after commit; do not introduce external storage solely for this extraction.

Deactivation writes account state and then revokes refresh sessions. These belong in the same transaction. Reactivation recovery is implemented: a leftover member access token can still call `POST /members/me/reactivate`. After that token expires, `POST /members/reactivate/request` may send a reactivation OTP (same anti-enumeration shape as password reset), and `POST /members/reactivate/recover` consumes that proof then activates under the account lock. Login remains blocked until the account is active again.

Account deactivation, moderation restriction and future discovery/admission state remain separate. Do not collapse them into a single status enum during a refactor.

### Moderation and admin administration

Restriction and refresh-session revocation now commit together. Restrict/unrestrict and admin activate/deactivate lock both actor and target accounts, recheck current super-admin eligibility inside the transaction, and serialize admin population checks with admins:lifecycle. Repeated restriction/admin deactivation repairs unrevoked sessions without duplicating state-change audit. Unrestriction must not restore revoked tokens or override independent self-deactivation. Keep the live service-level super-admin check even when the endpoint has SuperAdmin policy.

### Audit and access tokens

[AuditWriter](../src/Aynera.Application/Features/Audit/Services/Implementations/AuditWriter.cs) catches persistence exceptions and logs them. Audit is currently best effort, not an atomic guarantee of the business operation. Capturing-writer unit tests show intent to audit, not durable audit persistence. Decide which security-sensitive outcomes require transactional records; do not change all logging to fail closed as part of registration extraction.

Member/Admin policies now validate token identity, role and audience plus current account kind, existence, active status and restriction. SuperAdmin also checks live privileges. MemberReactivation allows inactive members through to lifecycle validation (which rejects restrictions); MemberAccountDeletion retains deletion access for inactive/restricted members. Every policy rejects missing/deleted/wrong-kind accounts. Anonymous verified recovery and logout are unchanged. These are per-request state checks, not permanent JWT revocation: an unexpired token may work again after state restoration, and already-authorized requests may finish. Login/refresh session issuance now shares account locks with disable operations. Refresh rereads state under lock, atomically creates one replacement and marks its parent, and commits family revocation before returning a reuse error. Credential/OTP verification precedes this transaction. OTP challenges are consumed atomically (purpose, audience, expiry, hashed code, attempt limits); concurrent valid proofs succeed once.

## Planned code boundaries for registration

| Component | Responsibility |
|---|---|
| Registration service/use case | Validate registration inputs; coordinate atomic account/profile/delivery-intent creation; return existing DTO |
| Auth account creation contract | Normalize/validate identity credentials and persist Identity user/role within the shared transaction |
| Profiles contract/repository | Own MemberProfileRecord construction/mapping and persistence; move its contract out of Auth without moving unrelated DTOs wholesale |
| Transaction abstraction | Commit/rollback the workflow's shared DbContext operations, including Identity store writes; implementation stays in Infrastructure |
| Verification delivery queue/worker | Durable post-commit sending, retries and delivery failure visibility; reuse notification adapters |
| AuthService | Login, credentials, OTP/session flow and identifier confirmation; consume profile reads only for current response composition |

Avoid an AuthService ↔ RegistrationService cycle. Registration should call a narrow Auth contract, not the complete AuthService that also routes registration back to it. Preserve the existing public endpoint and DTO while moving the controller dependency directly to the use case.

## Acceptance checks before the first slice is complete

1. Existing member registration, password/OTP login, email verification and duplicate-contact tests still pass.
2. Injected role/profile failure leaves no account, role membership, profile or queued email; a subsequent valid attempt succeeds.
3. Delivery-intent persistence failure rolls back account/profile persistence.
4. Email-provider failure after commit preserves the account and queued work; retries recover without another account/profile.
5. Concurrent duplicate contact submissions cannot produce two accounts or profiles; callers retain the documented conflict response.
6. Cancellation and transaction failure do not produce an orphan account/profile or send email for a rolled-back registration.
7. Migrations work for both an empty test database and the existing schema; client routes/response shape remain unchanged.
8. Audit durability is asserted only to the guarantee implemented, with failures observable.

## Sequencing

1. Registration consistency and profile ownership, including delivery recovery.
2. Account lifecycle consistency and a usable reactivation contract.
3. Moderation consistency and concurrent administrator invariants.
4. Additional admission/verification/cohort behavior after outstanding product decisions are resolved.

No future matching or relationship-state tables are needed to implement the first slice. Browser acceptance remains deferred; compatibility aliases were removed on 2026-09-13. This document does not imply those future workflows have been implemented or tested.


