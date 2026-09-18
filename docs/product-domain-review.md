# Product domain review

Date: 2026-09-07. Status: analysis and proposed direction; no further service extraction implemented by this review.

Implementation update (2026-09-08): the first [registration slice](registration-workflow.md) now provides atomic account/profile creation and durable verification delivery. The remaining recommendations below are a roadmap, not completed work.

## Evidence and scope

Reviewed the product journey and matchmaking rules, backend controllers/services/repositories/entities, current admin routes and API callers, marketing API integration, and mobile navigation, state, and sample-data structure. This is an architecture review of the current implementation and documented roadmap, not browser acceptance testing or a review of every design artifact.

Primary sources:

- [Product specification](../../Admin%20Panel/docs/product/WEB-MVP-PRODUCT-SPEC.md), a working specification for founder review.
- [Matching and relationship rules](../../Admin%20Panel/docs/product/MATCHMAKING-RULES.md), including explicitly open decisions.
- [Architecture](../../Admin%20Panel/docs/engineering/architecture.md) and [build order](../../Admin%20Panel/docs/product/BUILD-ORDER.md).
- [AuthService](../src/Aynera.Application/Features/Auth/Services/Implementations/AuthService.cs), [UserManagementService](../src/Aynera.Application/Features/Users/Services/Implementations/UserManagementService.cs), and [current entities](../src/Aynera.Persistence/Entities).
- [Admin navigation](../../Admin%20Panel/src/App.tsx), [marketing API](../../Marketing%20web/lib/api.ts), and [mobile state](../../Mobile%20App/src/state).

## Main conclusion

Keep the modular monolith. Group operations by the business state they own, with authorization deciding who may perform them. Do not move all non-login methods into Users, and do not create a new module for every page or role.

The previous controller and policy work is useful, but the service extraction is an intermediate step. UserManagementService still combines account administration, profile read composition, and moderation. A new catch-all Users service would reproduce the original problem.

## What exists versus what is planned

| Area | Current implementation | Product direction |
|---|---|---|
| Marketing | City catalog, early-access signup, suggestions and support submissions | Demand collection, trust and public information |
| Admin | Login, waitlist/cities, members/media, restriction, admin accounts, inboxes and audit | Admission review, verification, cohort operations, manual introductions and safety cases |
| Member account | Registration, OTP/password authentication, current account, photo/video, account lifecycle | Approved, verified membership and richer profile editing |
| Mobile | Rich screens, local state modules and mock people; no fetch/axios API callers found in source scan | Backend-authoritative onboarding, introductions, conversations and relationship workflows |
| Core journey backend | Applications, Profiles, Verification, Cohorts, Matching, Introductions, Chat, Dates, Focus, Together and Safety folders are placeholders | Apply → verify → build profile → review/admit → introduce → mutual match → communicate → Focus/Together |

The existing waitlist is not an application-review workflow. Admin `/applicants` currently redirects to members. An early-access city is not a cohort with capacity and reciprocal-preference rules. Public support feedback is not private post-date feedback or a safety-case workflow.

## Proposed ownership

| Domain | Owns | Important boundary |
|---|---|---|
| Identity/Auth | Credentials, identifier verification, OTP, reset, token/session lifecycle, recent-authentication proofs | Does not own member biography, admission decisions or relationship status. Separate admin/member login audiences can remain in this domain. |
| Accounts/Users | Account identity/kind, self-activation/deactivation/deletion, privileged admin-account administration | Account active is distinct from approved for discovery. Account deletion coordinates other domains. |
| Profiles | Biography, prompts, preferences, completion, member-visible profile projections | Keep private account/admin fields out of discovery DTOs. A `/me` composite response can remain stable. |
| Media and Verification | Photos/videos own bytes and metadata; verification owns evidence and review outcome | A photo or heuristic authenticity result is not equivalent to identity/liveness approval. Existing stub providers do not complete verification. |
| EarlyAccess and Applications | EarlyAccess owns leads/city availability; Applications owns submission, review, admission and waitlist decisions | Connect a lead to an account through an explicit conversion workflow; do not treat registration as approval. |
| Cohorts | Admission capacity, city/segment membership and operational availability | Consulted by admissions and introductions; not inferred solely from city text. |
| Safety | Reports, blocks, restrictions, holds and moderation decisions | Safety constrains discovery/chat/relationship actions, while reporting and exit remain available. Existing restriction behavior must remain stable until a deliberate migration. |
| Introductions/Matching | Candidate eligibility, curator proposals, reasons, reciprocal interest and match creation | Implement one coherent pilot workflow first; avoid premature internal splitting. Recheck safety and eligibility when accepting. |
| Conversations and Meetings | Matched conversation access/close; meeting plans and private outcomes | Public feedback submissions must not become a shared storage model for private meeting outcomes. |
| Relationship journey | Focus, Together, mutual confirmations, expiry/exit and shared-space access | Begin with one consistency boundary for mutual state; do not scatter competing booleans across Users, Focus and Together. |
| Suggestions, Support, Audit, Notifications | Product ideas, support intake, traceability and delivery respectively | Audit records outcomes; it must not become the source of business state. Notification delivery should not determine whether a persisted workflow succeeded. |

These are ownership boundaries, not instructions to create all these services now. Existing Photos/Videos services can remain separate implementations within the media area.

## Workflows determine extraction order

1. **Registration:** today AuthService creates the account, creates a profile, sends verification email and writes audit. Extract a registration use case that coordinates Accounts, Profiles and Auth; preserve the public request/response. Define atomic account/profile persistence and what happens when email delivery fails before moving code. Current orchestration has no explicit encompassing transaction, while repositories persist independently.
2. **Account deletion:** today it soft-deletes sessions, profile, photos/video and account sequentially. Give this a named account-lifecycle use case. Define retry/partial-failure behavior now; add conversations/shared-content retention only when those features exist. Do not replace this with a generic repository delete.
3. **Profile reads:** separate self/admin/discovery projections. The existing admin detail is legitimately a composed read across profile and media; composition itself is not an architectural defect. Do not change URLs again just to mirror class names.
4. **Moderation:** preserve current restrict/unrestrict effects and session revocation. Introduce Safety ownership when implementing reports/holds, with tests for which actions are blocked. Keep authentication, account availability and discovery eligibility distinct.
5. **Focus/Together:** the specification requires atomic mutual transitions and rechecking eligibility. Together also affects introductions, chat and shared space. Establish the transition/transaction contract before implementing these modules; OTP/password login remains Auth's responsibility.

## Authorization implications

Member/Admin/SuperAdmin is adequate for the implemented account-management slice, but not the complete product permission model. Reviewers, curators, safety moderators and cohort operators are planned responsibilities, not currently implemented policies.

Future authorization needs both operation permission and resource context: self ownership, pair membership, assigned review, mutual confirmation and recent authentication. Avoid turning applicant, approved member, Focus or Together into Identity roles. They are business states with different transition rules.

Current Admin and Member policies check authentication, role and audience; SuperAdmin additionally checks live account state. Decide explicitly how existing access tokens behave after account deactivation/restriction. Do not accidentally block reactivation or safety exit paths by applying a universal active-account rule everywhere.

## Product alignment decisions

- The older build-order/spec describes web-first delivery and Delhi/Bangalore. The mobile prototype already exists and its city catalog also includes Mumbai. Treat these as differing stages of the product, not instructions to delete or revert working screens. Confirm current launch channels/cities before admission implementation.
- The spec describes Core and Professionals. Current marketing signup submits interest `Aynera`, while the backend retains additional interest support. Confirm the admission proposition before creating a membership schema.
- The specification leaves prior Focus before Together, unrelated-chat handling, cooling-off duration, shared-content ownership and cross-matching between propositions open. Preserve those as unresolved product decisions; do not invent defaults during a code move.
- Public consent intake, private evidence retention, and account/shared-content deletion need distinct product policies. The deferred-media document is an implementation proposal, not a complete retention specification.
- Backend API/developer documentation still contains descriptions from before the controller/service refactor. Reconcile those with the current routes as part of the next bounded implementation.

## Recommended next work

The [operation-level ownership map](workflow-ownership-map.md) now documents current dependencies, observed partial-failure cases, and acceptance checks for the first registration slice.

First produce an operation-level ownership and consistency map for the implemented registration, account lifecycle, profile reads and moderation workflows. Then implement **registration orchestration and profile ownership as one bounded slice**, retaining contracts, followed by account lifecycle and its failure handling. Avoid more generic Users extraction until that map is agreed with the actual workflow.

Validation for each slice should cover existing client contracts, failed/retried operations, session revocation, authorization, audit outcomes and migration upgrades. Passing the current suite does not validate future admission, matching or relationship rules. Browser verification remains deferred as requested; legacy aliases were removed on 2026-09-13.
