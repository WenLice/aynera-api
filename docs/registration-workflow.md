# Registration implementation

Implemented 2026-09-08 as the first slice of the [workflow ownership map](workflow-ownership-map.md).

`POST /members/register` keeps its request, response and authorization contract. MembersController now calls IRegistrationService instead of IAuthService. AuthService retains credentials, OTP, identifier confirmation and sessions; the profile repository contract now lives under Features/Profiles/Repositories.

## App registration (step-wise, 2026-09-18)

The mobile app verifies the phone before it asks for anything else, so `RegistrationService` also owns a
four-step flow: `StartPhoneRegistrationAsync` (SMS code, purpose `registration`; refuses numbers that already
have a live account with the same `user_already_exists` / `account_deactivated` / `account_restricted` rule the
repository applies), `VerifyPhoneRegistrationAsync` (consumes the code, then under the `registration:phone:*`
lock calls `CreateMemberAsync(phone, email: null, password: null)` + `MarkPhoneConfirmedAsync`, audits
`member_registered`, and issues member tokens via `IAuthService.IssueMemberSessionAsync`), then for the signed-in
member `StartEmailVerificationAsync` (email code, purpose `email_verification`, `email_already_exists` if another
live account owns the address) and `VerifyEmailCodeAsync` (consumes the code, `SetConfirmedEmailAsync` under the
account lock, audits `email_confirmed`). No verification-email link is queued on this path — the code *is* the
proof. The one-shot `RegisterAsync` is unchanged for the website and tests.

A fifth step, `SaveProfileAsync` (`PUT /members/me/profile`), writes the member's basic details. The app keeps
its own draft on the device while the member walks the "you", "basics" and "life" steps and sends it once, after
"life" — `city` is required and that is the first step at which it is known. The method resolves the city the same
way `RegisterAsync` does, then upserts the profile under the `account:{userId}` lock and audits
`member_profile_saved` with the before/after of each field. It is a full replace, not a patch: a second save
overwrites every field, so an optional field left out is cleared. Interests, taste and intent are not part of it —
those are a later, separate model.

## Persistence and delivery

RegistrationService validates adulthood/contact information and the verification-link URL, resolves `city` against the active city catalog (`IEarlyAccessCityRepository.FindOpenByNameAsync`, case-insensitive; unknown or closed cities fail with `city_not_supported` before any write, and the canonical catalog name plus `CityId` are stored so members and venues share one city key), then coordinates account creation (including the member role), initial profile creation and a VerificationEmailDeliveries row. WorkflowTransaction uses one scoped AyneraDbContext transaction across Identity and repositories. Sorted PostgreSQL advisory locks serialize member registrations that share a normalized phone or email. A failed identity/profile/queue write or cancellation rolls back all those writes.

The success response means the account/profile and verification delivery intent were committed. It does not imply provider delivery. Existing duplicate-contact responses remain unchanged; registering again does not resend email or return somebody else's account.

VerificationEmailHostedService polls every five seconds, processing up to 25 due rows per pass. VerificationEmailDispatcher claims rows with five-minute leases so multiple API instances do not normally send the same delivery concurrently. It calls the email provider outside a database transaction, with a one-minute cancellation timeout. Provider adapters must honor cancellation.

On failure, the row remains with an incremented Attempts count and a retry time: 30 seconds initially, doubling to a maximum of one hour. Retries continue while the API worker runs. Logs include the user ID, attempt, exception type and next-attempt time; they do not include the token, recipient or provider exception message. Operators can inspect Attempts, NextAttemptAtUtc and LeaseUntilUtc for delayed work.

Successful rows are deleted. Missing/deleted, inactive, restricted, already-confirmed or email-less accounts have obsolete deliveries discarded. Reactivation does not automatically schedule another verification-email delivery. A future authenticated verification-email resend requires a separate rate-limited contract; it is not introduced by this slice. Account reactivation after access-token expiry is a separate lifecycle OTP flow (`POST /members/reactivate/request` and `POST /members/reactivate/recover`).

Only UserId and scheduling/lease metadata are stored in the queue. Email addresses and tokens are resolved at send time, so no passwords or bearer verification links are persisted in the queue. Delivery is at least once: a crash after sending but before deleting the row may cause a duplicate email after lease expiry. Exactly-once provider delivery is not claimed.

Audit retains its existing best-effort behavior after commit. Account deletion now cancels pending verification deliveries in the same transaction as account/profile/media/session soft deletion. A delivery already handed to an email provider cannot be recalled; queue cancellation prevents later retries. Account deactivation and refresh-session revocation are also transactional. Expired-token reactivation recovery is implemented on AccountLifecycleService.

## Deployment and tests

- Apply `20260908052824_AddVerificationEmailDeliveries` through the normal EF migration/startup path. It adds one table and index; existing member rows are unchanged and no historical email jobs are backfilled.
- `20260913091510_AddMemberProfileCityId` adds the nullable `MemberProfiles.CityId` column + index. `20260916081640_RequireMemberProfileCity` then backfills legacy rows by case-insensitive, trimmed name match against `EarlyAccessCities` (normalising `City` to the catalog spelling), **raises an exception listing the unmatched city names if any profile cannot be linked**, and finally makes the column `NOT NULL`. Fix or remove unmatched rows and restart; the migration never invents a placeholder city. `MemberProfileCityMigrationTests` replays this against legacy-shaped rows.
- `20260918092953_MemberProfileBasicDetails` replaces `FirstName`/`LastName` with one required `Name` (max 150 — a first name or a full name, the member's choice) and adds the nullable `Nickname`, `HeightCm`, `Hometown` and `Work`. `Name` is added nullable, backfilled as `btrim(FirstName || ' ' || LastName)`, then **raises if any row is still without a name** before being set `NOT NULL`; only then are the old columns dropped. `Down` splits `Name` back at the first space, which is lossy by nature. `MemberProfileNameMigrationTests` replays it.
- Keep the API process running for automatic retries. Configure a real email adapter for delivery; the Console provider remains a development no-send adapter.
- All worker instances need compatible ASP.NET Data Protection keys/configuration so a link created by one instance can be confirmed by another. Use the existing deployment key-management setup; this slice does not configure shared production keys.
- Integration tests disable only the background worker and invoke the production dispatcher explicitly for deterministic assertions. PostgreSQL tests cover rollback after persisted writes, cancellation, concurrency, provider retry, lease recovery, invalid URL rejection and obsolete work cleanup.
- Fresh-database migration and upgrade of the existing test database were both exercised. Browser workflows remain deferred; legacy aliases were removed on 2026-09-13.
