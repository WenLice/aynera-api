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
| Anonymous | `/auth/*` and `POST /users/register` | No token |
| `Member` | `GET /users/me` and other `/users/*` | Valid JWT with member audience / role |

### Common error codes

| `errorCode` | Typical HTTP | When |
|-------------|--------------|------|
| `validation_failed` | 400 | FluentValidation / model binding failed |
| `otp_rate_limited` | 429 | Too many OTP requests (phone/IP) |
| `otp_expired` | 401 | OTP missing or past TTL |
| `otp_invalid` | 401 | Wrong code |
| `otp_locked` | 401 | Too many verify attempts |
| `invalid_audience` | 400 | Audience not allowed for member OTP |
| `invalid_refresh` | 401 | Refresh missing/unknown/user gone |
| `refresh_reuse` | 401 | Refresh already rotated/revoked (family revoked) |
| `refresh_expired` | 401 | Refresh past expiry |
| `underage` | 400 | Date of birth is under 18 on register |
| `unauthorized` | 401 | Missing/invalid principal for protected route |
| `account_deactivated` | 403/409 | Account deactivated; activate before login/re-register |
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
| `user_not_found` | 404 | Account missing (Login/VerifySms without Register, or `/me`) |
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
| `phone` | `string` | Yes | Required; valid Indian mobile; normalized to E.164 `+91…` server-side |

### `RequestMemberOtpResponse`

| Attribute | Type | Notes |
|-----------|------|-------|
| `expiresInSeconds` | `int` | OTP TTL (from config, default 300) |
| `retryAfterSeconds` | `int` or `null` | Hint when rate-limited path returns timing |

### `VerifyMemberOtpRequest`

| Attribute | Type | Required | Validation / notes |
|-----------|------|----------|--------------------|
| `phone` | `string` | Yes | Required; valid Indian mobile |
| `code` | `string` | Yes | Required; length 4–10 |
| `audience` | `string` or `null` | No | Defaults to `member-web`. Allowed: `member-web`, `member-mobile` |

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
| `accountKind` | `string` | e.g. `Member` |
| `isActive` | `bool` | `false` = deactivated (must activate; cannot re-register) |
| `isDeleted` | `bool` | Soft-deleted accounts are hidden from normal queries |
| `roles` | `string[]` | e.g. `member` |
| `profile` | `MemberProfileDto` or `null` | Present after Register |

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
| **Purpose** | Start member login / SMS verification for a **registered** phone: rate-limit check, store hashed OTP in Redis, send code (dev stub does not deliver SMS). Fails if the phone is not registered |
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
| 404 | `ApiResponse<object?>` | Phone not registered (`user_not_found`) |
| 429 | `ApiResponse<object?>` | Rate limited (`otp_rate_limited`) |
| 500 | `ApiResponse<object?>` | Unexpected |

---

### Auth — VerifySms

| | |
|--|--|
| **Name** | VerifySms |
| **Purpose** | Validate OTP for an **existing** registered member; mark phone confirmed; issue access + refresh tokens. Does **not** create accounts |
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
| 404 | `ApiResponse<object?>` | Phone not registered (`user_not_found`) |
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
| **Purpose** | Active cities for marketing Early Register (`/apply`); Staff receive full catalog |
| **Method / path** | `GET /early-access/cities` |
| **Auth** | Anonymous (Staff role → all cities including inactive) |
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

### Early-access cities (Staff writes)

`GET /early-access/cities` is anonymous (active cities for `/apply`). Callers with the `staff` role receive the full catalog (including inactive).  
`POST` / `PATCH` / `DELETE` require Staff JWT (`Staff` policy). Soft-delete + `isActive` supported.

| Name | Method / path |
|------|----------------|
| ListEarlyAccessCities | `GET /early-access/cities` |
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

**Response `data`:** `FeedbackSubmissionDto` (`id`, `isExistingUser`, `createdAtUtc`) — `isExistingUser` is `true` when the email matches an existing member account.

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

## Endpoint index

| Name | Method | Path | Auth | Request body | Response `data` |
|------|--------|------|------|--------------|-----------------|
| Register | `POST` | `/users/register` | Anonymous | `CreateMemberRequest` | `AuthAccountDto` |
| VerifyEmail | `POST` | `/auth/verifyemail` | Anonymous | `ConfirmEmailRequest` | `AuthAccountDto` |
| Login | `POST` | `/auth/login` | Anonymous | `RequestMemberOtpRequest` | `RequestMemberOtpResponse` |
| VerifySms | `POST` | `/auth/verifysms` | Anonymous | `VerifyMemberOtpRequest` | `TokenResponse` |
| Refresh | `POST` | `/auth/refresh` | Anonymous | `RefreshTokenRequest` | `TokenResponse` |
| Logout | `POST` | `/auth/logout` | Anonymous | `LogoutRequest` | `null` |
| Me | `GET` | `/users/me` | `Member` | — | `AuthAccountDto` |
| UploadPhotos | `POST` | `/users/me/photos` | `Member` | multipart `photos` | `MemberPhotoDto[]` |
| ListPhotos | `GET` | `/users/me/photos` | `Member` | — | `MemberPhotoDto[]` |
| GetPhoto | `GET` | `/users/me/photos/{id}` | `Member` | — | image bytes |
| DeletePhoto | `DELETE` | `/users/me/photos/{id}` | `Member` | — | `null` |
| UploadIntroductionVideo | `POST` | `/users/me/introduction-video` | `Member` | multipart `video` | `IntroductionVideoDto` |
| GetIntroductionVideo | `GET` | `/users/me/introduction-video` | `Member` | — | `IntroductionVideoDto` |
| GetIntroductionVideoContent | `GET` | `/users/me/introduction-video/content` | `Member` | — | video bytes |
| DeleteIntroductionVideo | `DELETE` | `/users/me/introduction-video` | `Member` | — | `null` |
| JoinEarlyAccess | `POST` | `/early-access/register` | Anonymous | `JoinEarlyAccessRequest` | `EarlyAccessSignupDto` |
| ListEarlyAccessCities | `GET` | `/early-access/cities` | Anonymous (Staff sees all) | — | `EarlyAccessCityDto[]` |
| CreateEarlyAccessCity | `POST` | `/early-access/cities` | `Staff` | `CreateEarlyAccessCityRequest` | `EarlyAccessCityDto` |
| UpdateEarlyAccessCity | `PATCH` | `/early-access/cities/{id}` | `Staff` | `UpdateEarlyAccessCityRequest` | `EarlyAccessCityDto` |
| DeleteEarlyAccessCity | `DELETE` | `/early-access/cities/{id}` | `Staff` | — | `null` |
| SubmitSuggestion | `POST` | `/public/suggestions` | Anonymous | `SubmitSuggestionRequest` | `SuggestionDto` |
| SubmitFeedback | `POST` | `/public/feedback` | Anonymous | `SubmitFeedbackRequest` | `FeedbackSubmissionDto` |
| Deactivate | `POST` | `/users/deactivate` | `Member` | — | `null` |
| Reactivate | `POST` | `/users/reactivate` | `Member` | — | `null` |
| DeleteAccount | `DELETE` | `/users/account` | `Member` | — | `null` |

---

## Client flow (member-web)

```text
0. POST /users/register                { phone, email, firstName, lastName, gender, dateOfBirth, city, religion? }
   — emails verification link (dev stub does not deliver mail); emailConfirmed=false until step 0b
0b. Open email link → member-web reads ?userId=&token=
    POST /auth/verifyemail             { userId, token }
1. POST /auth/login                    { phone }
2. User enters OTP (dev/tests: capture via test SMS double; prod: SMS)3. POST /auth/verifysms                { phone, code, audience? }
   — confirms phone; does not create the account
4. Store accessToken + refreshToken securely
5. GET  /users/me                      Authorization: Bearer accessToken
5b. POST /users/me/photos              multipart photos[] (first = reference selfie)
6. Before access expiry: POST /auth/refresh { refreshToken }
7. Sign out: POST /auth/logout         { refreshToken }
```

Config: `Elaris:Email:VerifyLinkBaseUrl` (or `ELARIS_EMAIL_VERIFY_LINK_BASE_URL`) is the **member-web** page that receives `userId` + `token` in the query string and POSTs them to `/auth/verifyemail`.

Photos are stored as compressed JPEG `BYTEA` in Postgres (`Elaris:Photos`). Face matching uses `IFaceMatchService` (stub in dev; `StubFaceMatchStatus` defaults to `Matched`).

Media authenticity (`Elaris:MediaAuthenticity`) runs on **original** photo/video bytes before processing. The default `HeuristicMediaAuthenticityService` rejects known AI-tool markers in EXIF/XMP/IPTC (and sampled video metadata). Undetermined media is allowed when `AllowWhenUndetermined` is true. Set `StubForceAiDetected` only in tests. Swap the service later for a dedicated AI-detection provider without changing controllers.

Early access cities live in Postgres (`EarlyAccessCities`) with soft-delete and `isActive`. `GET /early-access/cities` returns open cities for anonymous callers (full catalog for Staff). Staff manage Wave 2+ via `POST`/`PATCH`/`DELETE` on `/early-access/cities`. Marketing `/apply` registers with `POST /early-access/register`. Suggestions (`/public/suggestions`) and grievance feedback (`/public/feedback`) are separate.

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
