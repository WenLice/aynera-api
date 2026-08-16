# ElAris API reference

Contract documentation for **`elaris-api`**. Keep this file updated whenever endpoints, DTOs, or status codes change.

- Base URL (local SDK): typically `http://localhost:5057` (check launchSettings)
- Base URL (Docker Compose `api` service): `http://localhost:8080`
- Interactive (Development only): `/swagger`
- Envelope type: `ApiResponse<T>` (`Elaris.Domain.Common`)
- Content type: `application/json` unless noted

Related: [developer-guide.md](./developer-guide.md), [setup.md](./setup.md).

---

## Conventions

### Response envelope

Every JSON API response uses:

| Field | Type | Meaning |
|-------|------|---------|
| `success` | `boolean` | `true` on happy path |
| `data` | `T` or `null` | Payload when successful |
| `statusCode` | `number` | Same HTTP status as the response |
| `errorCode` | `string` or `null` | Machine-readable error when `success` is false |
| `errors` | `object` or `null` | Field → string[] (validation) |
| `correlationId` | `string` or `null` | Request correlation id |

### Pagination

List endpoints that can grow (waitlist, suggestions, feedback, member applications, admins) return `PagedResult<T>` inside `data`:

| Field | Type | Meaning |
|-------|------|---------|
| `items` | `T[]` | Current page |
| `page` | `int` | 1-based page index |
| `pageSize` | `int` | Page size used |
| `totalCount` | `int` | Matching rows |
| `totalPages` | `int` | Derived from `totalCount` / `pageSize` |
| `hasNextPage` | `bool` | |
| `hasPreviousPage` | `bool` | |

Query: `page` (default `1`) and `pageSize` (default `15`, max `50`). Example: `GET /admin/suggestions?page=2&pageSize=20`. Invalid `page` or `pageSize` returns `validation_failed` (400).

Cities and member photos stay unpaged (small fixed catalogs).

### Headers

| Header | Direction | Required | Notes |
|--------|-----------|----------|-------|
| `Authorization` | request | When endpoint is authorized | `Bearer {accessToken}` |
| `Content-Type` | request | Body endpoints | `application/json` |
| `X-Correlation-Id` | request / response | Optional | Generated if missing; echo for client logs |
| `Accept` | request | Optional | `application/json` |

### Auth policies

| Policy | Used by | Requirement |
|--------|---------|-------------|
| Anonymous | `/auth/*`, `/admin/otp/*`, `POST /admin/password`, and `POST /users/register` | No token |
| `Member` | `GET /users/me`, `POST /users/me/password`, and other `/users/*` | JWT with role `member` and `aud=member` |
| `Admin` | Early-access city writes and `GET /early-access/signups`; `GET /admin/me`; `GET /admin/members`, `GET /admin/members/{id}`, `POST /admin/members/{id}/restrict|unrestrict` (super-admin); `POST /admin/admins`; `GET /admin/suggestions` and `GET /admin/feedback` | JWT with role `admin` and `aud=admin` |

### Common error codes

| `errorCode` | Typical HTTP | When |
|-------------|--------------|------|
| `validation_failed` | 400 | FluentValidation / model binding failed |
| `otp_rate_limited` | 429 | Too many OTP requests (identifier/IP) |
| `otp_expired` | 401 | OTP missing, wrong purpose, or past TTL |
| `otp_invalid` | 401 | Wrong code |
| `otp_locked` | 401 | Too many verify attempts |
| `invalid_identifier` | 400 | Identifier is neither a valid Indian mobile nor email |
| `invalid_audience` | 400 | Audience not allowed for this login door (`member` on app OTP) |
| `invalid_refresh` | 401 | Refresh missing/unknown/user gone |
| `refresh_reuse` | 401 | Refresh already rotated/revoked (family revoked) |
| `refresh_expired` | 401 | Refresh past expiry |
| `password_not_set` | 400 | Password login on an account with no password |
| `password_invalid` | 401 | Wrong password (or current password when changing) |
| `password_too_weak` | 400 | Password failed Identity rules (min 8, lowercase + digit) |
| `password_current_required` | 400 | Changing an existing password without `currentPassword` |
| `account_locked` | 403 | Too many failed password attempts (Identity lockout) |
| `underage` | 400 | Date of birth is under 18 on register |
| `unauthorized` | 401 | Missing/invalid principal for protected route |
| `account_deactivated` | 403/409 | Account deactivated; activate before login/re-register |
| `account_restricted` | 403/409 | Account restricted by super-admin; contact support |
| `account_deleted` | 409 | Tried to activate a soft-deleted account |
| `user_already_exists` | 409 | Phone already registered on create member |
| `email_already_exists` | 409 | Email already registered on create member |
| `email_token_invalid` | 400 | VerifyEmail token missing/invalid/expired |
| `email_missing` | 400 | Account has no email to confirm |
| `photo_required` | 400 | Upload called with no files |
| `photo_limit_exceeded` | 400 | More than max photos (default 6) |
| `photo_too_large` | 400 | File exceeds MaxBytes |
| `photo_unsupported_type` | 400 | Not JPEG/PNG/WebP |
| `photo_invalid` | 400 | Image could not be decoded |
| `photo_face_mismatch` | 400 | Face match rejected the photo |
| `photo_ai_generated` | 400 | Photo failed authenticity check (likely AI/synthetic) |
| `photo_not_found` | 404 | Photo id missing for user |
| `video_required` | 400 | Introduction video upload missing file |
| `video_too_large` | 400 | Video exceeds MaxBytes |
| `video_unsupported_type` | 400 | Not MP4/WebM/QuickTime |
| `video_reference_photo_required` | 400 | No reference photo for face match |
| `video_face_mismatch` | 400 | Face match rejected the video |
| `video_ai_generated` | 400 | Video failed authenticity check (likely AI/synthetic) |
| `video_guideline_failed` | 400 | Banned-words / community guideline check failed |
| `video_not_found` | 404 | Introduction video missing |
| `early_access_adult_required` | 400 | Early access without 18+ confirmation |
| `early_access_consent_required` | 400 | Early access without marketing email consent |
| `early_access_city_not_open` | 400 | City missing, inactive, or soft-deleted |
| `early_access_city_exists` | 409 | Duplicate early-access city name |
| `early_access_city_not_found` | 404 | Early-access city id missing |
| `early_access_rate_limited` | 429 | Too many early-access posts from IP |
| `feedback_message_too_long` | 400 | Feedback message exceeds max length |
| `feedback_rate_limited` | 429 | Too many feedback posts from IP |
| `suggestion_message_too_long` | 400 | Suggestion message exceeds max length |
| `suggestion_rate_limited` | 429 | Too many suggestion posts from IP |
| `user_not_found` | 404 | Account missing (Login/VerifySms/Password without Register, admin door without a seeded admin, or `/me`). The other product’s accounts are hidden behind this code. |
| `super_admin_required` | 403 | Authenticated admin is not a super-admin (admin management writes and list) |
| `cannot_deactivate_self` | 409 | Super-admin tried to deactivate their own account |
| `last_admin` | 409 | The last active admin cannot be deactivated |
| `last_super_admin` | 409 | The last active super-admin cannot be deactivated |
| `internal_error` | 500 | Unhandled exception |

---

## Shared models

### `ApiResponse<T>`

See conventions above.

### `CreateMemberRequest`

| Attribute | Type | Required | Validation / notes |
|-----------|------|----------|--------------------|
| `phone` | `string` | Yes | Required; valid Indian mobile. Normalized to E.164 `+91…` |
| `firstName` | `string` | Yes | Max 100 |
| `lastName` | `string` | Yes | Max 100 |
| `gender` | `string` | Yes | `Male` \| `Female` \| `Other` |
| `dateOfBirth` | `date` | Yes | ISO date; must be 18+ (`underage` if not) |
| `city` | `string` | Yes | Max 100; free text for now |
| `email` | `string` | Yes | Required; valid email |
| `religion` | `string` or `null` | No | Max 100 |
| `password` | `string` or `null` | No | Optional; min 8 chars with a lowercase letter and a number. Enables `POST /auth/password` immediately. |

### `MemberProfileDto`

| Attribute | Type | Notes |
|-----------|------|-------|
| `firstName` | `string` | |
| `lastName` | `string` | |
| `gender` | `string` | |
| `dateOfBirth` | `date` | |
| `city` | `string` | |
| `religion` | `string` or `null` | |

### `RequestMemberOtpRequest`

| Attribute | Type | Required | Validation / notes |
|-----------|------|----------|--------------------|
| `identifier` | `string` | Yes | Indian mobile **or** email. Mobiles are normalized to E.164 `+91…`; emails are trimmed and lowercased |

### `RequestMemberOtpResponse`

| Attribute | Type | Notes |
|-----------|------|-------|
| `expiresInSeconds` | `int` | OTP TTL (from config, default 300) |
| `retryAfterSeconds` | `int` or `null` | Hint when rate-limited path returns timing |

### `VerifyMemberOtpRequest`

| Attribute | Type | Required | Validation / notes |
|-----------|------|----------|--------------------|
| `identifier` | `string` | Yes | Same phone or email used on Login |
| `code` | `string` | Yes | Required; length 4–10 |
| `audience` | `string` or `null` | No | Defaults to `member`. Allowed: `member` |

### `MemberPasswordLoginRequest`

| Attribute | Type | Required | Validation / notes |
|-----------|------|----------|--------------------|
| `identifier` | `string` | Yes | Indian mobile or email |
| `password` | `string` | Yes | Account password |

### `SetMemberPasswordRequest`

| Attribute | Type | Required | Validation / notes |
|-----------|------|----------|--------------------|
| `password` | `string` | Yes | Min 8 chars with a lowercase letter and a number |
| `currentPassword` | `string` or `null` | When changing | Required if the account already has a password |

### `ForgotMemberPasswordRequest`

| Attribute | Type | Required | Validation / notes |
|-----------|------|----------|--------------------|
| `identifier` | `string` | Yes | Indian mobile or email |

### `ResetMemberPasswordRequest`

| Attribute | Type | Required | Validation / notes |
|-----------|------|----------|--------------------|
| `identifier` | `string` | Yes | Same phone or email used on ForgotPassword |
| `code` | `string` | Yes | Password-reset OTP; length 4–10 |
| `newPassword` | `string` | Yes | Min 8 chars with a lowercase letter and a number |

### `RequestAdminOtpRequest`

| Attribute | Type | Required | Validation / notes |
|-----------|------|----------|--------------------|
| `identifier` | `string` | Yes | Indian mobile or email of a **seeded admin** |

### `VerifyAdminOtpRequest`

| Attribute | Type | Required | Validation / notes |
|-----------|------|----------|--------------------|
| `identifier` | `string` | Yes | Same phone or email used on Admin OtpRequest |
| `code` | `string` | Yes | Required; length 4–10 |

### `AdminPasswordLoginRequest`

| Attribute | Type | Required | Validation / notes |
|-----------|------|----------|--------------------|
| `identifier` | `string` | Yes | Indian mobile or email |
| `password` | `string` | Yes | Seeded admin password |

### `CreateAdminRequest`

| Attribute | Type | Required | Validation / notes |
|-----------|------|----------|--------------------|
| `email` | `string` | Yes | Valid email; unique across accounts |
| `password` | `string` | Yes | Min 8 chars with a lowercase letter and a number |
| `phone` | `string` or `null` | No | Indian mobile when present |

### `RefreshTokenRequest` / `LogoutRequest`

| Attribute | Type | Required | Validation / notes |
|-----------|------|----------|--------------------|
| `refreshToken` | `string` | Yes | Required opaque refresh token |

### `TokenResponse`

| Attribute | Type | Notes |
|-----------|------|-------|
| `accessToken` | `string` | JWT |
| `refreshToken` | `string` | Opaque; store securely; rotated on refresh |
| `tokenType` | `string` | Always `Bearer` |
| `expiresInSeconds` | `int` | Access token lifetime |
| `account` | `AuthAccountDto` | Current account snapshot |

### `AuthAccountDto`

| Attribute | Type | Notes |
|-----------|------|-------|
| `id` | `guid` | Account id (`sub`) |
| `phone` | `string` or `null` | E.164 when set |
| `phoneConfirmed` | `bool` | |
| `email` | `string` or `null` | |
| `emailConfirmed` | `bool` | `false` until VerifyEmail succeeds |
| `accountKind` | `string` | `Member` or `Admin` |
| `isActive` | `bool` | `false` = member self-deactivated (must activate; cannot re-register) |
| `isDeleted` | `bool` | Soft-deleted accounts are hidden from normal queries |
| `isSuperAdmin` | `bool` | Super-admin flag (admins only; members are always `false`). Set in the database, not via API |
| `isRestricted` | `bool` | `true` = restricted by super-admin; blocks sign-in until unrestricted |
| `roles` | `string[]` | e.g. `member` or `admin` |
| `profile` | `MemberProfileDto` or `null` | Present after member Register; always `null` for admins |

### `ConfirmEmailRequest`

| Attribute | Type | Required | Validation / notes |
|-----------|------|----------|--------------------|
| `userId` | `guid` | Yes | From verification link query |
| `token` | `string` | Yes | From verification link query |

---

### `MemberPhotoDto`

| Attribute | Type | Notes |
|-----------|------|-------|
| `id` | `guid` | |
| `sortOrder` | `int` | Ascending display order |
| `contentType` | `string` | Stored as `image/jpeg` after processing |
| `byteSize` | `int` | |
| `isReference` | `bool` | Primary selfie for face matching |
| `faceMatchStatus` | `string` | `Pending` \| `Matched` \| `Rejected` \| `Skipped` |
| `faceMatchScore` | `number` or `null` | |
| `createdAtUtc` | `datetime` | |

---

## Endpoints

### Users — Register

| | |
|--|--|
| **Name** | Register |
| **Purpose** | Register a member account **and** profile. Blocked if a non-deleted account exists for the phone (`isActive=true` → use existing; `isActive=false` → activate first). Soft-deleted rows (`isDeleted=true`) do **not** block; a **new** account id is created. Requires firstName, lastName, gender, dateOfBirth (18+), city, email; optional religion. Automatically emails a verification link; `emailConfirmed` stays false until VerifyEmail |
| **Method / path** | `POST /users/register` |
| **Auth** | Anonymous |
| **Tags** | Users |

**Request**

| Source | Model |
|--------|-------|
| Body | `CreateMemberRequest` |
| Query | none |
| Route | none |

**Response `data` model:** `AuthAccountDto`

**Response types**

| HTTP | Body | When |
|------|------|------|
| 200 | `ApiResponse<AuthAccountDto>` | Member created |
| 400 | `ApiResponse<object?>` | Validation failed / invalid phone |
| 409 | `ApiResponse<object?>` | Phone/email in use (`user_already_exists` / `email_already_exists`) or deactivated (`account_deactivated`) |
| 500 | `ApiResponse<object?>` | Unexpected |

---

### Auth — VerifyEmail

| | |
|--|--|
| **Name** | VerifyEmail |
| **Purpose** | Confirm email via token from the Register email link. Sets `emailConfirmed=true`. No separate “send verification” endpoint. Member-web opens the link, then POSTs `userId` + `token` here |
| **Method / path** | `POST /auth/verifyemail` |
| **Auth** | Anonymous |
| **Tags** | Auth |

**Request**

| Source | Model |
|--------|-------|
| Body | `ConfirmEmailRequest` |
| Query | none |
| Route | none |

**Response `data` model:** `AuthAccountDto`

**Response types**

| HTTP | Body | When |
|------|------|------|
| 200 | `ApiResponse<AuthAccountDto>` | Email confirmed (also if already confirmed) |
| 400 | `ApiResponse<object?>` | Invalid/expired token (`email_token_invalid`) |
| 404 | `ApiResponse<object?>` | User missing (`user_not_found`) |
| 500 | `ApiResponse<object?>` | Unexpected |

---

### Users — DeleteAccount

| | |
|--|--|
| **Name** | DeleteAccount |
| **Purpose** | Soft-delete the authenticated member and related rows (e.g. refresh sessions). Phone is freed for a brand-new registration; old account data is not restored |
| **Method / path** | `DELETE /users/account` |
| **Auth** | Bearer JWT + policy **`Member`** |
| **Tags** | Users |

**Request**

| Source | Model |
|--------|-------|
| Body | none |
| Headers | `Authorization: Bearer {accessToken}` |

**Response `data` model:** `null`

**Response types**

| HTTP | Body | When |
|------|------|------|
| 200 | `ApiResponse<object?>` | Soft-deleted |
| 401 | | Missing/invalid token |
| 404 | `ApiResponse<object?>` | User not found |
| 500 | `ApiResponse<object?>` | Unexpected |

---

### Users — Deactivate

| | |
|--|--|
| **Name** | Deactivate |
| **Purpose** | Mark account deactivated. Re-registration blocked until activated; refresh sessions revoked |
| **Method / path** | `POST /users/deactivate` |
| **Auth** | Bearer JWT + policy **`Member`** |
| **Tags** | Users |

**Response types:** `200` success; `401` unauthorized; `403` if already in a blocked state as applicable

---

### Users — Reactivate

| | |
|--|--|
| **Name** | Reactivate |
| **Purpose** | Reactivate a deactivated account (not available for soft-deleted accounts) |
| **Method / path** | `POST /users/reactivate` |
| **Auth** | Bearer JWT + policy **`Member`** |
| **Tags** | Users |

**Response types:** `200` success; `409` `account_deleted` if soft-deleted

---

### Auth — Login

| | |
|--|--|
| **Name** | Login |
| **Purpose** | Start member login by sending an OTP to a **registered** phone or email: rate-limit check, store hashed OTP in Redis (`otp:phone:{e164}` or `otp:email:{normalized}`), send code (dev stub does not deliver). Fails if the identifier is not an active member. Admin accounts return `user_not_found`. |
| **Method / path** | `POST /auth/login` |
| **Auth** | Anonymous |
| **Tags** | Auth |

**Request**

| Source | Model |
|--------|-------|
| Body | `RequestMemberOtpRequest` |
| Query | none |
| Route | none |

**Response `data` model:** `RequestMemberOtpResponse`

**Response types**

| HTTP | Body | When |
|------|------|------|
| 200 | `ApiResponse<RequestMemberOtpResponse>` | OTP stored and “sent” |
| 400 | `ApiResponse<object?>` | Validation failed |
| 403 | `ApiResponse<object?>` | Deactivated account (`account_deactivated`) |
| 404 | `ApiResponse<object?>` | Identifier not a member (`user_not_found`) |
| 429 | `ApiResponse<object?>` | Rate limited (`otp_rate_limited`) |
| 500 | `ApiResponse<object?>` | Unexpected |

---

### Auth — VerifySms

| | |
|--|--|
| **Name** | VerifySms |
| **Purpose** | Validate the login OTP for an **existing** registered member; mark that phone or email confirmed; issue access + refresh tokens. Path name is historical — it verifies SMS **or** email OTP. Does **not** create accounts |
| **Method / path** | `POST /auth/verifysms` |
| **Auth** | Anonymous |
| **Tags** | Auth |

**Request**

| Source | Model |
|--------|-------|
| Body | `VerifyMemberOtpRequest` |
| Query | none |
| Route | none |

**Response `data` model:** `TokenResponse`

**Response types**

| HTTP | Body | When |
|------|------|------|
| 200 | `ApiResponse<TokenResponse>` | Tokens issued |
| 400 | `ApiResponse<object?>` | Validation / invalid audience |
| 401 | `ApiResponse<object?>` | Invalid/expired/locked OTP |
| 403 | `ApiResponse<object?>` | Deactivated account (`account_deactivated`) |
| 404 | `ApiResponse<object?>` | Identifier not a member (`user_not_found`) |
| 500 | `ApiResponse<object?>` | Unexpected |

---

### Auth — PasswordLogin

| | |
|--|--|
| **Name** | PasswordLogin |
| **Purpose** | Sign in a registered member with phone or email plus password. Issues member tokens (`aud=member`, `amr=pwd`). Rejects admin accounts as `user_not_found`. |
| **Method / path** | `POST /auth/password` |
| **Auth** | Anonymous |
| **Tags** | Auth |

**Request**

| Source | Model |
|--------|-------|
| Body | `MemberPasswordLoginRequest` |
| Query | none |
| Route | none |

**Response `data` model:** `TokenResponse`

**Response types**

| HTTP | Body | When |
|------|------|------|
| 200 | `ApiResponse<TokenResponse>` | Tokens issued |
| 400 | `ApiResponse<object?>` | Validation / `password_not_set` / `password_too_weak` |
| 401 | `ApiResponse<object?>` | Wrong password (`password_invalid`) |
| 403 | `ApiResponse<object?>` | Deactivated (`account_deactivated`) or locked (`account_locked`) |
| 404 | `ApiResponse<object?>` | Identifier not a member (`user_not_found`) |
| 500 | `ApiResponse<object?>` | Unexpected |

---

### Auth — ForgotPassword

| | |
|--|--|
| **Name** | ForgotPassword |
| **Purpose** | Send a password-reset OTP to phone or email when an active member matches. Always returns 200 with the Login OTP response shape so callers cannot probe whether an account exists. |
| **Method / path** | `POST /auth/password/forgot` |
| **Auth** | Anonymous |
| **Tags** | Auth |

**Request**

| Source | Model |
|--------|-------|
| Body | `ForgotMemberPasswordRequest` |
| Query | none |
| Route | none |

**Response `data` model:** `RequestMemberOtpResponse`

**Response types**

| HTTP | Body | When |
|------|------|------|
| 200 | `ApiResponse<RequestMemberOtpResponse>` | Accepted (code sent only if an active member exists) |
| 400 | `ApiResponse<object?>` | Validation failed |
| 429 | `ApiResponse<object?>` | Rate limited (`otp_rate_limited`) |
| 500 | `ApiResponse<object?>` | Unexpected |

---

### Auth — ResetPassword

| | |
|--|--|
| **Name** | ResetPassword |
| **Purpose** | Verify the password-reset OTP, set a new password, confirm the identifier, and issue member tokens |
| **Method / path** | `POST /auth/password/reset` |
| **Auth** | Anonymous |
| **Tags** | Auth |

**Request**

| Source | Model |
|--------|-------|
| Body | `ResetMemberPasswordRequest` |
| Query | none |
| Route | none |

**Response `data` model:** `TokenResponse`

**Response types**

| HTTP | Body | When |
|------|------|------|
| 200 | `ApiResponse<TokenResponse>` | Password set; tokens issued |
| 400 | `ApiResponse<object?>` | Validation / weak password |
| 401 | `ApiResponse<object?>` | Invalid/expired/locked OTP |
| 403 | `ApiResponse<object?>` | Deactivated account (`account_deactivated`) |
| 404 | `ApiResponse<object?>` | Identifier not a member (`user_not_found`) |
| 500 | `ApiResponse<object?>` | Unexpected |

---

### Auth — Refresh

| | |
|--|--|
| **Name** | Refresh |
| **Purpose** | Rotate refresh session; issue new access + refresh pair |
| **Method / path** | `POST /auth/refresh` |
| **Auth** | Anonymous (refresh token in body) |
| **Tags** | Auth |

**Request**

| Source | Model |
|--------|-------|
| Body | `RefreshTokenRequest` |
| Query | none |
| Route | none |

**Response `data` model:** `TokenResponse`

**Response types**

| HTTP | Body | When |
|------|------|------|
| 200 | `ApiResponse<TokenResponse>` | New tokens issued |
| 400 | `ApiResponse<object?>` | Validation failed |
| 401 | `ApiResponse<object?>` | Invalid / reused / expired refresh |
| 500 | `ApiResponse<object?>` | Unexpected |

---

### Auth — logout

| | |
|--|--|
| **Name** | Logout |
| **Purpose** | Revoke the refresh session for the given refresh token (idempotent if unknown) |
| **Method / path** | `POST /auth/logout` |
| **Auth** | Anonymous (refresh token in body) |
| **Tags** | Auth |

**Request**

| Source | Model |
|--------|-------|
| Body | `LogoutRequest` |
| Query | none |
| Route | none |

**Response `data` model:** `null`

**Response types**

| HTTP | Body | When |
|------|------|------|
| 200 | `ApiResponse<object?>` | Always succeeds for valid shape; missing session is no-op |
| 400 | `ApiResponse<object?>` | Validation failed |
| 500 | `ApiResponse<object?>` | Unexpected |

---

### Early Access — list cities

| | |
|--|--|
| **Name** | ListEarlyAccessCities |
| **Purpose** | Active cities for marketing Early Register (`/apply`); Admin receive full catalog |
| **Method / path** | `GET /early-access/cities` |
| **Auth** | Anonymous (Admin role → all cities including inactive) |
| **Tags** | EarlyAccess |

**Response `data`:** `EarlyAccessCityDto[]` (`id`, `name`, `wave`, `sortOrder`, `isActive`)

---

### Early Access — register

| | |
|--|--|
| **Name** | RegisterEarlyAccess |
| **Purpose** | Join or update the marketing early-access waitlist (upsert by email) |
| **Method / path** | `POST /early-access/register` |
| **Auth** | Anonymous |
| **Tags** | EarlyAccess |

**Request body:** `JoinEarlyAccessRequest`

| Attribute | Type | Required | Validation / notes |
|-----------|------|----------|--------------------|
| `fullName` | `string` | Yes | Max 200 |
| `email` | `string` | Yes | Normalized lower-case; unique upsert key |
| `city` | `string` | Yes | Must match an active early-access city |
| `interest` | `string` | Yes | `Elaris` or `Elaris Professionals` / `ElarisProfessionals` |
| `isAdult` | `bool` | Yes | Must be `true` |
| `marketingConsent` | `bool` | Yes | Must be `true` |

**Response `data` model:** `EarlyAccessSignupDto` (`id`, `email`, `city`, `interest`, `created`)

---

### Early-access cities (Admin writes)

`GET /early-access/cities` is anonymous (active cities for `/apply`). Callers with the `admin` role and `aud=admin` receive the full catalog (including inactive).  
`POST` / `PATCH` / `DELETE` require Admin JWT (`Admin` policy). Soft-delete + `isActive` supported.

| Name | Method / path |
|------|----------------|
| ListEarlyAccessCities | `GET /early-access/cities` |
| ListEarlyAccessSignups | `GET /early-access/signups` (Admin) |
| CreateEarlyAccessCity | `POST /early-access/cities` |
| UpdateEarlyAccessCity | `PATCH /early-access/cities/{id}` |
| DeleteEarlyAccessCity | `DELETE /early-access/cities/{id}` (soft delete) |

`CreateEarlyAccessCityRequest`: `name`, `wave`, `sortOrder`, `isActive`.  
Wave 2+ = add cities here; anonymous `GET /early-access/cities` picks them up when active.

---

### Public — submit suggestion

| | |
|--|--|
| **Name** | SubmitSuggestion |
| **Purpose** | Product idea submissions from `/suggest` |
| **Method / path** | `POST /public/suggestions` |
| **Auth** | Anonymous |
| **Tags** | Public |

**Request:** `fullName`, `email`, `message` (max 2000)

---

### Public — submit feedback (grievance)

| | |
|--|--|
| **Name** | SubmitFeedback |
| **Purpose** | Grievance / support channel from `/grievance` (not product ideas) |
| **Method / path** | `POST /public/feedback` |
| **Auth** | Anonymous |
| **Tags** | Public |

**Request:** `fullName`, `email`, `message`, optional `phone`

**Response `data`:** `FeedbackSubmissionDto` (`id`, `isExistingUser`, `createdAtUtc`) — `isExistingUser` is `true` when the email matches an existing member account (`memberId` is stored for admin list linking).

---

### Early Access — ListSignups

| | |
|--|--|
| **Name** | ListEarlyAccessSignups |
| **Purpose** | Waitlist inbox. Newest first. Includes name, email, phone, city, and interest. Soft-deleted rows are omitted. |
| **Method / path** | `GET /early-access/signups` |
| **Auth** | `Admin` |
| **Tags** | EarlyAccess |

**Query:** `PagedQuery` — `page` (default 1), `pageSize` (default 15, max 50)

**Response `data` model:** `PagedResult<EarlyAccessSignupAdminDto>`

`EarlyAccessSignupAdminDto`

| Attribute | Type | Notes |
|-----------|------|-------|
| `id` | `guid` | |
| `fullName` | `string` | |
| `email` | `string` | |
| `phone` | `string` or `null` | |
| `city` | `string` | |
| `interest` | `string` | `Elaris` or `Elaris Professionals` |
| `isAdult` | `bool` | |
| `marketingConsent` | `bool` | |
| `isActive` | `bool` | |
| `createdAtUtc` | `datetime` | |
| `updatedAtUtc` | `datetime` or `null` | Set when the same email upserts |

---

### Admin — ListSuggestions

| | |
|--|--|
| **Name** | ListSuggestions |
| **Purpose** | Product-idea inbox. Newest first. Includes name, email, phone, and message. |
| **Method / path** | `GET /admin/suggestions` |
| **Auth** | `Admin` |
| **Tags** | Admin |

**Query:** `PagedQuery` — `page` (default 1), `pageSize` (default 15, max 50)

**Response `data` model:** `PagedResult<SuggestionAdminDto>` (`items[].id`, `fullName`, `email`, `phone`, `message`, `createdAtUtc`)

---

### Admin — ListFeedback

| | |
|--|--|
| **Name** | ListFeedback |
| **Purpose** | Grievance / support inbox. Newest first. Includes name, email, phone, message, and whether the email matched a member. |
| **Method / path** | `GET /admin/feedback` |
| **Auth** | `Admin` |
| **Tags** | Admin |

**Query:** `PagedQuery` — `page` (default 1), `pageSize` (default 15, max 50)

**Response `data` model:** `PagedResult<FeedbackSubmissionAdminDto>` (`items[].id`, `fullName`, `email`, `phone`, `message`, `isExistingUser`, `memberId`, `createdAtUtc`)

---

### Users — current account

| | |
|--|--|
| **Name** | Me |
| **Purpose** | Return the authenticated member account |
| **Method / path** | `GET /users/me` |
| **Auth** | Bearer JWT + policy **`Member`** |
| **Tags** | Users |

**Request**

| Source | Model |
|--------|-------|
| Body | none |
| Query | none |
| Route | none |
| Headers | `Authorization: Bearer {accessToken}` |

**Response `data` model:** `AuthAccountDto`

**Response types**

| HTTP | Body | When |
|------|------|------|
| 200 | `ApiResponse<AuthAccountDto>` | Account loaded |
| 401 | `ApiResponse` or empty challenge | Missing/invalid token or principal |
| 404 | `ApiResponse<object?>` | User id in token not found (`user_not_found`) |
| 500 | `ApiResponse<object?>` | Unexpected |

---

### Users — SetPassword

| | |
|--|--|
| **Name** | SetPassword |
| **Purpose** | Set a password if none exists, or change it when `currentPassword` is supplied |
| **Method / path** | `POST /users/me/password` |
| **Auth** | Bearer JWT + policy **`Member`** |
| **Tags** | Users |

**Request**

| Source | Model |
|--------|-------|
| Body | `SetMemberPasswordRequest` |
| Headers | `Authorization: Bearer {accessToken}` |

**Response `data` model:** `null`

**Response types**

| HTTP | Body | When |
|------|------|------|
| 200 | `ApiResponse<object?>` | Password set or changed |
| 400 | `ApiResponse<object?>` | Validation / `password_too_weak` / `password_current_required` |
| 401 | `ApiResponse<object?>` | Missing token or wrong `currentPassword` (`password_invalid`) |
| 500 | `ApiResponse<object?>` | Unexpected |

---

### Admin — OtpRequest

| | |
|--|--|
| **Name** | OtpRequest |
| **Purpose** | Start admin login by sending an OTP to a registered admin phone or email. Member accounts return `user_not_found`. No public admin register. |
| **Method / path** | `POST /admin/otp/request` |
| **Auth** | Anonymous |
| **Tags** | Admin |

**Request body:** `RequestAdminOtpRequest`

**Response `data` model:** `RequestMemberOtpResponse`

**Response types**

| HTTP | Body | When |
|------|------|------|
| 200 | `ApiResponse<RequestMemberOtpResponse>` | OTP stored and “sent” |
| 400 | `ApiResponse<object?>` | Validation failed |
| 403 | `ApiResponse<object?>` | Deactivated account (`account_deactivated`) |
| 404 | `ApiResponse<object?>` | Identifier not an admin (`user_not_found`) |
| 429 | `ApiResponse<object?>` | Rate limited (`otp_rate_limited`) |
| 500 | `ApiResponse<object?>` | Unexpected |

---

### Admin — OtpVerify

| | |
|--|--|
| **Name** | OtpVerify |
| **Purpose** | Verify admin OTP; issue access (1 hour) + refresh (24 hours) with `aud=admin`, `amr=otp` |
| **Method / path** | `POST /admin/otp/verify` |
| **Auth** | Anonymous |
| **Tags** | Admin |

**Request body:** `VerifyAdminOtpRequest`

**Response `data` model:** `TokenResponse`

**Response types**

| HTTP | Body | When |
|------|------|------|
| 200 | `ApiResponse<TokenResponse>` | Tokens issued |
| 400 | `ApiResponse<object?>` | Validation failed |
| 401 | `ApiResponse<object?>` | Invalid/expired/locked OTP |
| 403 | `ApiResponse<object?>` | Deactivated account (`account_deactivated`) |
| 404 | `ApiResponse<object?>` | Identifier not an admin (`user_not_found`) |
| 500 | `ApiResponse<object?>` | Unexpected |

---

### Admin — PasswordLogin

| | |
|--|--|
| **Name** | AdminPasswordLogin |
| **Purpose** | Sign in a seeded admin with phone or email plus password. Issues `aud=admin`, `amr=pwd`. Member accounts return `user_not_found`. |
| **Method / path** | `POST /admin/password` |
| **Auth** | Anonymous |
| **Tags** | Admin |

**Request body:** `AdminPasswordLoginRequest`

**Response `data` model:** `TokenResponse`

**Response types**

| HTTP | Body | When |
|------|------|------|
| 200 | `ApiResponse<TokenResponse>` | Tokens issued |
| 400 | `ApiResponse<object?>` | Validation / `password_not_set` |
| 401 | `ApiResponse<object?>` | Wrong password (`password_invalid`) |
| 403 | `ApiResponse<object?>` | Deactivated (`account_deactivated`) or locked (`account_locked`) |
| 404 | `ApiResponse<object?>` | Identifier not an admin (`user_not_found`) |
| 500 | `ApiResponse<object?>` | Unexpected |

---

### Admin — Me

| | |
|--|--|
| **Name** | AdminMe |
| **Purpose** | Return the authenticated admin account. `isSuperAdmin` is the live database flag (not the JWT claim). `profile` is null. |
| **Method / path** | `GET /admin/me` |
| **Auth** | Bearer JWT + policy **`Admin`** |
| **Tags** | Admin |

**Request**

| Source | Model |
|--------|-------|
| Body | none |
| Query | none |
| Route | none |
| Headers | `Authorization: Bearer {accessToken}` |

**Response `data` model:** `AuthAccountDto`

**Response types**

| HTTP | Body | When |
|------|------|------|
| 200 | `ApiResponse<AuthAccountDto>` | Account loaded |
| 401 | `ApiResponse` or empty challenge | Missing/invalid token or principal |
| 404 | `ApiResponse<object?>` | User id in token is not an admin (`user_not_found`) |
| 500 | `ApiResponse<object?>` | Unexpected |

---

### Admin — CreateAdmin

| | |
|--|--|
| **Name** | CreateAdmin |
| **Purpose** | Create another admin with a password (`isSuperAdmin` is always false). Caller must already be a super-admin (database flag, not the JWT claim). Set `AspNetUsers.IsSuperAdmin` in the database when you need a super-admin. There is still no public admin register. |
| **Method / path** | `POST /admin/admins` |
| **Auth** | `Admin` + live `IsSuperAdmin` |
| **Tags** | Admin |

**Request body:** `CreateAdminRequest`

**Response `data` model:** `AuthAccountDto`

**Response types**

| HTTP | Body | When |
|------|------|------|
| 200 | `ApiResponse<AuthAccountDto>` | Admin created |
| 400 | `ApiResponse<object?>` | Validation failed |
| 401 | `ApiResponse<object?>` | Missing/invalid admin token |
| 403 | `ApiResponse<object?>` | Caller is not a super-admin (`super_admin_required`) |
| 409 | `ApiResponse<object?>` | Email or phone already used (`email_already_exists` / `user_already_exists`) |
| 500 | `ApiResponse<object?>` | Unexpected |

---

### Admin — ListAdmins

| | |
|--|--|
| **Name** | ListAdmins |
| **Purpose** | Page admin accounts, including inactive operators. Super-admin only. |
| **Method / path** | `GET /admin/admins` |
| **Auth** | `Admin` + live `IsSuperAdmin` |
| **Tags** | Admin |

**Query:** `PagedQuery` — `page` (default 1), `pageSize` (default 15, max 50)

**Response `data` model:** `PagedResult<AuthAccountDto>`

---

### Admin — DeactivateAdmin

| | |
|--|--|
| **Name** | DeactivateAdmin |
| **Purpose** | Deactivate another admin and revoke their refresh sessions. Super-admin only. |
| **Method / path** | `POST /admin/admins/{id}/deactivate` |
| **Auth** | `Admin` + live `IsSuperAdmin` |
| **Tags** | Admin |

**Response `data` model:** `AuthAccountDto`

| HTTP | Body | When |
|------|------|------|
| 200 | `ApiResponse<AuthAccountDto>` | Deactivated (or already inactive) |
| 403 | `ApiResponse<object?>` | Caller is not a super-admin (`super_admin_required`) |
| 404 | `ApiResponse<object?>` | Target is not an admin (`user_not_found`) |
| 409 | `ApiResponse<object?>` | Self, last admin, or last super-admin |

---

### Admin — ActivateAdmin

| | |
|--|--|
| **Name** | ActivateAdmin |
| **Purpose** | Reactivate a deactivated admin. Super-admin only. |
| **Method / path** | `POST /admin/admins/{id}/activate` |
| **Auth** | `Admin` + live `IsSuperAdmin` |
| **Tags** | Admin |

**Response `data` model:** `AuthAccountDto`

---

### Admin — ListMembers

| | |
|--|--|
| **Name** | ListMembers |
| **Purpose** | Page members, newest first. Any admin. Soft-deleted members are omitted. Optional `search`, `isActive`, `isRestricted`. |
| **Method / path** | `GET /admin/members` |
| **Auth** | `Admin` |
| **Tags** | Admin |

**Query:** `page` (default 1), `pageSize` (default 15, max 50), optional `search` (email, phone, first/last name), optional `isActive`, optional `isRestricted`

**Response `data` model:** `PagedResult<MemberAdminDto>`

`MemberAdminDto`

| Field | Type | Notes |
|-------|------|-------|
| `id` | `guid` | Member user id |
| `phone` | `string` or `null` | E.164 when present |
| `phoneConfirmed` | `boolean` | |
| `email` | `string` or `null` | |
| `emailConfirmed` | `boolean` | |
| `isActive` | `boolean` | Member self-deactivate flag |
| `isRestricted` | `boolean` | Super-admin restriction; blocks sign-in |
| `createdAtUtc` | `datetime` | Account created |
| `firstName` | `string` or `null` | From member profile when present |
| `lastName` | `string` or `null` | |
| `gender` | `string` or `null` | |
| `dateOfBirth` | `date` or `null` | |
| `city` | `string` or `null` | |
| `religion` | `string` or `null` | |

Open one member with `GET /admin/members/{id}`. Super-admins can restrict or unrestrict with `POST /admin/members/{id}/restrict` and `POST /admin/members/{id}/unrestrict` (independent of member self-deactivate).

---

### Admin — GetMember

| | |
|--|--|
| **Name** | GetMember |
| **Purpose** | One member: profile fields plus photo and introduction-video metadata. Any admin. Soft-deleted members are omitted. |
| **Method / path** | `GET /admin/members/{id}` |
| **Auth** | `Admin` |
| **Tags** | Admin |

**Response `data` model:** `MemberAdminDetailDto`

Same fields as `MemberAdminDto`, plus:

| Field | Type | Notes |
|-------|------|-------|
| `photos` | `MemberPhotoDto[]` | Metadata only; bytes on GetMemberPhoto |
| `introductionVideo` | `IntroductionVideoDto` or `null` | Metadata only; bytes on GetMemberVideo |

---

### Admin — GetMemberPhoto

| | |
|--|--|
| **Name** | GetMemberPhoto |
| **Purpose** | Image bytes for one photo on a member. Any admin. |
| **Method / path** | `GET /admin/members/{id}/photos/{photoId}` |
| **Auth** | `Admin` |
| **Tags** | Admin |

**Response:** raw image bytes (`Content-Type` from the stored photo), not `ApiResponse<T>`.

---

### Admin — GetMemberVideo

| | |
|--|--|
| **Name** | GetMemberVideo |
| **Purpose** | Introduction-video bytes for a member. Any admin. |
| **Method / path** | `GET /admin/members/{id}/introduction-video/content` |
| **Auth** | `Admin` |
| **Tags** | Admin |

**Response:** raw video bytes (`Content-Type` from the stored video), not `ApiResponse<T>`.

---

### Admin — RestrictMember

| | |
|--|--|
| **Name** | RestrictMember |
| **Purpose** | Restrict a member (blocks sign-in, revokes refresh sessions). Super-admin only. Independent of member self-deactivate. Soft-deleted members are omitted (404). |
| **Method / path** | `POST /admin/members/{id}/restrict` |
| **Auth** | `Admin` + super-admin |
| **Tags** | Admin |

**Response `data` model:** `MemberAdminDto`

---

### Admin — UnrestrictMember

| | |
|--|--|
| **Name** | UnrestrictMember |
| **Purpose** | Remove a super-admin restriction from a member. Super-admin only. Soft-deleted members are omitted (404). |
| **Method / path** | `POST /admin/members/{id}/unrestrict` |
| **Auth** | `Admin` + super-admin |
| **Tags** | Admin |

**Response `data` model:** `MemberAdminDto`

---

### Admin — ListAuditEvents

| | |
|--|--|
| **Name** | ListAuditEvents |
| **Purpose** | Page audit events, newest first. Any admin. With `memberId` or `subjectId`, returns all actions for that subject unless `action` is set. Without a subject, defaults to member restrict/unrestrict. |
| **Method / path** | `GET /admin/audit/events` |
| **Auth** | `Admin` |
| **Tags** | Admin |

**Query:** `page`, `pageSize`, optional `action`, optional `memberId` (subject user), optional `subjectType`, optional `subjectId`

**Response `data` model:** `PagedResult<AuditEventAdminDto>` (`id`, `occurredAtUtc`, `action`, `outcome`, `message`, `actorUserId`, `subjectUserId`, `changesJson`, `correlationId`)

---

## Endpoint index

| Name | Method | Path | Auth | Request body | Response `data` |
|------|--------|------|------|--------------|-----------------|
| Register | `POST` | `/users/register` | Anonymous | `CreateMemberRequest` | `AuthAccountDto` |
| VerifyEmail | `POST` | `/auth/verifyemail` | Anonymous | `ConfirmEmailRequest` | `AuthAccountDto` |
| Login | `POST` | `/auth/login` | Anonymous | `RequestMemberOtpRequest` | `RequestMemberOtpResponse` |
| VerifySms | `POST` | `/auth/verifysms` | Anonymous | `VerifyMemberOtpRequest` | `TokenResponse` |
| PasswordLogin | `POST` | `/auth/password` | Anonymous | `MemberPasswordLoginRequest` | `TokenResponse` |
| ForgotPassword | `POST` | `/auth/password/forgot` | Anonymous | `ForgotMemberPasswordRequest` | `RequestMemberOtpResponse` |
| ResetPassword | `POST` | `/auth/password/reset` | Anonymous | `ResetMemberPasswordRequest` | `TokenResponse` |
| Refresh | `POST` | `/auth/refresh` | Anonymous | `RefreshTokenRequest` | `TokenResponse` |
| Logout | `POST` | `/auth/logout` | Anonymous | `LogoutRequest` | `null` |
| AdminOtpRequest | `POST` | `/admin/otp/request` | Anonymous | `RequestAdminOtpRequest` | `RequestMemberOtpResponse` |
| AdminOtpVerify | `POST` | `/admin/otp/verify` | Anonymous | `VerifyAdminOtpRequest` | `TokenResponse` |
| AdminPasswordLogin | `POST` | `/admin/password` | Anonymous | `AdminPasswordLoginRequest` | `TokenResponse` |
| AdminMe | `GET` | `/admin/me` | `Admin` | — | `AuthAccountDto` |
| CreateAdmin | `POST` | `/admin/admins` | `Admin` + super-admin | `CreateAdminRequest` | `AuthAccountDto` |
| ListAdmins | `GET` | `/admin/admins` | `Admin` + super-admin | `page`, `pageSize` | `PagedResult<AuthAccountDto>` |
| DeactivateAdmin | `POST` | `/admin/admins/{id}/deactivate` | `Admin` + super-admin | — | `AuthAccountDto` |
| ActivateAdmin | `POST` | `/admin/admins/{id}/activate` | `Admin` + super-admin | — | `AuthAccountDto` |
| ListMembers | `GET` | `/admin/members` | `Admin` | `page`, `pageSize`, `search?`, `isActive?`, `isRestricted?` | `PagedResult<MemberAdminDto>` |
| GetMember | `GET` | `/admin/members/{id}` | `Admin` | — | `MemberAdminDetailDto` |
| GetMemberPhoto | `GET` | `/admin/members/{id}/photos/{photoId}` | `Admin` | — | image bytes |
| GetMemberVideo | `GET` | `/admin/members/{id}/introduction-video/content` | `Admin` | — | video bytes |
| RestrictMember | `POST` | `/admin/members/{id}/restrict` | `Admin` + super-admin | — | `MemberAdminDto` |
| UnrestrictMember | `POST` | `/admin/members/{id}/unrestrict` | `Admin` + super-admin | — | `MemberAdminDto` |
| ListEarlyAccessSignups | `GET` | `/early-access/signups` | `Admin` | `page`, `pageSize` | `PagedResult<EarlyAccessSignupAdminDto>` |
| ListSuggestions | `GET` | `/admin/suggestions` | `Admin` | `page`, `pageSize` | `PagedResult<SuggestionAdminDto>` |
| ListFeedback | `GET` | `/admin/feedback` | `Admin` | `page`, `pageSize` | `PagedResult<FeedbackSubmissionAdminDto>` |
| ListAuditEvents | `GET` | `/admin/audit/events` | `Admin` | `page`, `pageSize`, `action?`, `memberId?`, `subjectType?`, `subjectId?` | `PagedResult<AuditEventAdminDto>` |
| Me | `GET` | `/users/me` | `Member` | — | `AuthAccountDto` |
| SetPassword | `POST` | `/users/me/password` | `Member` | `SetMemberPasswordRequest` | `null` |
| UploadPhotos | `POST` | `/users/me/photos` | `Member` | multipart `photos` | `MemberPhotoDto[]` |
| ListPhotos | `GET` | `/users/me/photos` | `Member` | — | `MemberPhotoDto[]` |
| GetPhoto | `GET` | `/users/me/photos/{id}` | `Member` | — | image bytes |
| DeletePhoto | `DELETE` | `/users/me/photos/{id}` | `Member` | — | `null` |
| UploadIntroductionVideo | `POST` | `/users/me/introduction-video` | `Member` | multipart `video` | `IntroductionVideoDto` |
| GetIntroductionVideo | `GET` | `/users/me/introduction-video` | `Member` | — | `IntroductionVideoDto` |
| GetIntroductionVideoContent | `GET` | `/users/me/introduction-video/content` | `Member` | — | video bytes |
| DeleteIntroductionVideo | `DELETE` | `/users/me/introduction-video` | `Member` | — | `null` |
| JoinEarlyAccess | `POST` | `/early-access/register` | Anonymous | `JoinEarlyAccessRequest` | `EarlyAccessSignupDto` |
| ListEarlyAccessCities | `GET` | `/early-access/cities` | Anonymous (Admin sees all) | — | `EarlyAccessCityDto[]` |
| CreateEarlyAccessCity | `POST` | `/early-access/cities` | `Admin` | `CreateEarlyAccessCityRequest` | `EarlyAccessCityDto` |
| UpdateEarlyAccessCity | `PATCH` | `/early-access/cities/{id}` | `Admin` | `UpdateEarlyAccessCityRequest` | `EarlyAccessCityDto` |
| DeleteEarlyAccessCity | `DELETE` | `/early-access/cities/{id}` | `Admin` | — | `null` |
| SubmitSuggestion | `POST` | `/public/suggestions` | Anonymous | `SubmitSuggestionRequest` | `SuggestionDto` |
| SubmitFeedback | `POST` | `/public/feedback` | Anonymous | `SubmitFeedbackRequest` | `FeedbackSubmissionDto` |
| Deactivate | `POST` | `/users/deactivate` | `Member` | — | `null` |
| Reactivate | `POST` | `/users/reactivate` | `Member` | — | `null` |
| DeleteAccount | `DELETE` | `/users/account` | `Member` | — | `null` |

---

## Client flow (member app)

```text
0. POST /users/register                { phone, email, firstName, lastName, gender, dateOfBirth, city, religion?, password? }
   — emails verification link (dev stub does not deliver mail); emailConfirmed=false until step 0b
   — optional password enables PasswordLogin immediately
0b. Open email link → client reads ?userId=&token=
    POST /auth/verifyemail             { userId, token }
1. OTP login: POST /auth/login         { identifier }   // phone or email
2. User enters OTP (dev/tests: capture via SMS or email test double)
3. POST /auth/verifysms                { identifier, code, audience? }
   — audience defaults to member; confirms that phone or email; does not create the account
   — access JWT 1 hour, refresh 90 days, aud=member, amr=otp
1b. Or password login: POST /auth/password { identifier, password }  → amr=pwd
1c. After OTP login, optional: POST /users/me/password { password, currentPassword? }
1d. Forgot: POST /auth/password/forgot { identifier } then POST /auth/password/reset { identifier, code, newPassword }
4. Store accessToken + refreshToken securely
5. GET  /users/me                      Authorization: Bearer accessToken
5b. POST /users/me/photos              multipart photos[] (first = reference selfie)
6. Before access expiry: POST /auth/refresh { refreshToken }
7. Sign out: POST /auth/logout         { refreshToken }
```

## Client flow (admin site)

Seed the first admin with `ELARIS_ADMIN_EMAIL` + `ELARIS_ADMIN_PASSWORD` (optional `ELARIS_ADMIN_PHONE`). That first seeded account is the super-admin. Admins created later via `POST /admin/admins` have `IsSuperAdmin` false. There is no public admin register and no API to grant super-admin.

```text
POST /admin/password               { identifier, password }
  or
POST /admin/otp/request            { identifier }
POST /admin/otp/verify             { identifier, code }
— access JWT 1 hour, refresh 24 hours, aud=admin (optional claim is_super_admin; authorization still uses the database)
Store tokens; call Admin-policy routes with Authorization: Bearer …
GET  /admin/me
GET  /admin/admins                 ?page=1&pageSize=15   // super-admin
POST /admin/admins                 { email, password, phone? }
POST /admin/admins/{id}/deactivate
POST /admin/admins/{id}/activate
GET  /early-access/signups         ?page=1&pageSize=15
GET  /admin/suggestions            ?page=1&pageSize=15
GET  /admin/feedback               ?page=1&pageSize=15
POST /auth/refresh                 { refreshToken }
POST /auth/logout                  { refreshToken }
```

Config: `Elaris:Email:VerifyLinkBaseUrl` (or `ELARIS_EMAIL_VERIFY_LINK_BASE_URL`) is the page or app deep link that receives `userId` + `token` in the query string and POSTs them to `/auth/verifyemail`.

Photos are stored as compressed JPEG `BYTEA` in Postgres (`Elaris:Photos`). Face matching uses `IFaceMatchService` (stub in dev; `StubFaceMatchStatus` defaults to `Matched`).

Media authenticity (`Elaris:MediaAuthenticity`) runs on **original** photo/video bytes before processing. The default `HeuristicMediaAuthenticityService` rejects known AI-tool markers in EXIF/XMP/IPTC (and sampled video metadata). Undetermined media is allowed when `AllowWhenUndetermined` is true. Set `StubForceAiDetected` only in tests. Swap the service later for a dedicated AI-detection provider without changing controllers.

Early access cities live in Postgres (`EarlyAccessCities`) with soft-delete and `isActive`. `GET /early-access/cities` returns open cities for anonymous callers (full catalog for Admin). Admins manage Wave 2+ via `POST`/`PATCH`/`DELETE` on `/early-access/cities`. Marketing `/apply` registers with `POST /early-access/register`. Admins list waitlist, suggestions, and grievances at `GET /early-access/signups`, `GET /admin/suggestions`, and `GET /admin/feedback` (`page` / `pageSize`).

---

## Maintenance

When adding an endpoint, document at least:

1. Name, purpose, method/path, auth
2. Request: body / query / route / headers
3. Response `data` model and field table (or link to Shared models)
4. Response types (HTTP × body × when)
5. New or changed `errorCode` values
6. Row in the Endpoint index table

Source of truth for types: `Elaris.Domain` DTOs and `Elaris.Api` controllers. Swagger is a convenience mirror in Development.
