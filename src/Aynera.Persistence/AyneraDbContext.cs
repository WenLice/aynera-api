using Aynera.Domain.Photos.Enums;
using Aynera.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Aynera.Persistence;

public sealed class AyneraDbContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>
{
    public AyneraDbContext(DbContextOptions<AyneraDbContext> options)
        : base(options)
    {
    }

    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<MemberProfile> MemberProfiles => Set<MemberProfile>();
    public DbSet<MemberPreferences> MemberPreferences => Set<MemberPreferences>();
    public DbSet<MemberAdmission> MemberAdmissions => Set<MemberAdmission>();
    public DbSet<MemberConsent> MemberConsents => Set<MemberConsent>();
    public DbSet<MemberPhoto> MemberPhotos => Set<MemberPhoto>();
    public DbSet<MemberIntroductionVideo> MemberIntroductionVideos => Set<MemberIntroductionVideo>();
    public DbSet<EarlyAccessSignup> EarlyAccessSignups => Set<EarlyAccessSignup>();
    public DbSet<EarlyAccessCity> EarlyAccessCities => Set<EarlyAccessCity>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<VenueNotification> VenueNotifications => Set<VenueNotification>();
    public DbSet<Suggestion> Suggestions => Set<Suggestion>();
    public DbSet<FeedbackSubmission> FeedbackSubmissions => Set<FeedbackSubmission>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<VerificationEmailDelivery> VerificationEmailDeliveries => Set<VerificationEmailDelivery>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<VerificationEmailDelivery>(entity =>
        {
            entity.HasKey(x => x.UserId);
            entity.HasIndex(x => x.NextAttemptAtUtc);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AppUser>(entity =>
        {
            entity.Property(x => x.AccountKind)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.IsSuperAdmin).IsRequired();
            entity.Property(x => x.IsRestricted).IsRequired();
            entity.Property(x => x.DeletedPhoneE164).HasMaxLength(32);
            entity.HasIndex(x => x.PhoneNumber);
            entity.HasIndex(x => x.IsDeleted);
            entity.HasIndex(x => x.IsActive);
            entity.HasIndex(x => x.IsRestricted);
            entity.HasIndex(x => x.IsSuperAdmin);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        builder.Entity<RefreshSession>(entity =>
        {
            entity.ToTable("RefreshSessions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Audience).HasMaxLength(64).IsRequired();
            entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.DeviceLabel).HasMaxLength(256);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => x.FamilyId);
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.IsDeleted);
            entity.HasQueryFilter(x => !x.IsDeleted);

            entity.HasOne<AppUser>()
                .WithMany(x => x.RefreshSessions)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MemberProfile>(entity =>
        {
            entity.ToTable("MemberProfiles");
            entity.HasKey(x => x.UserId);
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Nickname).HasMaxLength(100);
            entity.Property(x => x.Gender)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(x => x.GenderIsPublic).IsRequired();
            entity.Property(x => x.DateOfBirth).IsRequired();
            entity.Property(x => x.City).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Hometown).HasMaxLength(100);
            entity.Property(x => x.Work).HasMaxLength(200);
            entity.Property(x => x.Religion).HasMaxLength(100);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.CityId).IsRequired();
            entity.HasIndex(x => x.IsDeleted);
            entity.HasIndex(x => x.CityId);
            entity.HasQueryFilter(x => !x.IsDeleted);

            entity.HasOne(x => x.User)
                .WithOne(x => x.Profile)
                .HasForeignKey<MemberProfile>(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MemberPreferences>(entity =>
        {
            entity.ToTable("MemberPreferences");
            entity.HasKey(x => x.UserId);
            entity.Property(x => x.InterestedIn)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(x => x.Track)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(x => x.Outcome)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(x => x.MinAge).IsRequired();
            // Nullable on purpose: null is an open upper end, not a missing answer.
            entity.Property(x => x.MaxAge);
            entity.Property(x => x.AgeIsFlexible).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasIndex(x => x.IsDeleted);
            entity.HasQueryFilter(x => !x.IsDeleted);

            entity.HasOne(x => x.User)
                .WithOne()
                .HasForeignKey<MemberPreferences>(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MemberAdmission>(entity =>
        {
            entity.ToTable("MemberAdmissions");
            entity.HasKey(x => x.UserId);
            entity.Property(x => x.State)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(x => x.DecisionReason).HasMaxLength(500);
            entity.Property(x => x.ReviewNote).HasMaxLength(2000);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasIndex(x => x.State);
            // Mirrors the AppUser filter: a soft-deleted member's admission is invisible too. Without
            // a matching filter EF logs a model warning, which the audit sink would try to persist
            // through this same context while its model is still being validated.
            entity.HasQueryFilter(x => !x.User.IsDeleted);

            entity.HasOne(x => x.User)
                .WithOne()
                .HasForeignKey<MemberAdmission>(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MemberConsent>(entity =>
        {
            entity.ToTable("MemberConsents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PolicyKind)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(x => x.Version).HasMaxLength(32).IsRequired();
            entity.Property(x => x.AcceptedAtUtc).IsRequired();
            entity.HasIndex(x => x.UserId);
            // Re-accepting the same document version is idempotent rather than a duplicate row.
            entity.HasIndex(x => new { x.UserId, x.PolicyKind, x.Version }).IsUnique();
            entity.HasQueryFilter(x => !x.User.IsDeleted);

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MemberPhoto>(entity =>
        {
            entity.ToTable("MemberPhotos");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Data).IsRequired();
            entity.Property(x => x.FaceMatchStatus)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(x => x.FaceMatchScore).HasPrecision(5, 2);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => new { x.UserId, x.SortOrder });
            entity.HasIndex(x => x.IsDeleted);
            entity.HasQueryFilter(x => !x.IsDeleted);

            entity.HasOne(x => x.User)
                .WithMany(x => x.Photos)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MemberIntroductionVideo>(entity =>
        {
            entity.ToTable("MemberIntroductionVideos");
            entity.HasKey(x => x.UserId);
            entity.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Data).IsRequired();
            entity.Property(x => x.FaceMatchStatus)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(x => x.FaceMatchScore).HasPrecision(5, 2);
            entity.Property(x => x.GuidelineDetail).HasMaxLength(500);
            entity.Property(x => x.Transcript).HasMaxLength(4000);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasIndex(x => x.IsDeleted);
            entity.HasQueryFilter(x => !x.IsDeleted);

            entity.HasOne(x => x.User)
                .WithOne(x => x.IntroductionVideo)
                .HasForeignKey<MemberIntroductionVideo>(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<EarlyAccessSignup>(entity =>
        {
            entity.ToTable("EarlyAccessSignups");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(32);
            entity.Property(x => x.City).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Interest).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Intent).HasMaxLength(64);
            entity.Property(x => x.MeetPreference).HasMaxLength(32);
            entity.Property(x => x.ClientIp).HasMaxLength(64);
            entity.Property(x => x.UserAgent).HasMaxLength(512);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasIndex(x => x.Email).IsUnique();
            entity.HasIndex(x => x.City);
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasIndex(x => x.IsDeleted);
            entity.HasIndex(x => x.IsActive);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        builder.Entity<EarlyAccessCity>(entity =>
        {
            entity.ToTable("EarlyAccessCities");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
            entity.HasIndex(x => x.Wave);
            entity.HasIndex(x => x.SortOrder);
            entity.HasIndex(x => x.IsDeleted);
            entity.HasIndex(x => x.IsActive);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        builder.Entity<Venue>(entity =>
        {
            entity.ToTable("Venues");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.Area).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Address).HasMaxLength(500).IsRequired();
            entity.Property(x => x.ContactName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ContactEmail).HasMaxLength(256).IsRequired();
            entity.Property(x => x.ContactPhoneE164).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasIndex(x => x.Name);
            entity.HasIndex(x => x.CityId);
            entity.HasIndex(x => x.Type);
            entity.HasIndex(x => x.IsDeleted);
            entity.HasIndex(x => x.IsActive);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        builder.Entity<VenueNotification>(entity =>
        {
            entity.ToTable("VenueNotifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Channel).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(x => x.Note).HasMaxLength(500);
            entity.Property(x => x.LastError).HasMaxLength(128);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasIndex(x => x.VenueId);
            entity.HasIndex(x => x.NextAttemptAtUtc);
            entity.HasIndex(x => new { x.SentAtUtc, x.AbandonedAtUtc });
        });

        builder.Entity<Suggestion>(entity =>
        {
            entity.ToTable("Suggestions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.ClientIp).HasMaxLength(64);
            entity.Property(x => x.UserAgent).HasMaxLength(512);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasIndex(x => x.Email);
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasIndex(x => x.IsDeleted);
            entity.HasIndex(x => x.IsActive);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        builder.Entity<FeedbackSubmission>(entity =>
        {
            entity.ToTable("FeedbackSubmissions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(32);
            entity.Property(x => x.Message).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.ClientIp).HasMaxLength(64);
            entity.Property(x => x.UserAgent).HasMaxLength(512);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasIndex(x => x.Email);
            entity.HasIndex(x => x.IsExistingUser);
            entity.HasIndex(x => x.MemberId);
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasIndex(x => x.IsDeleted);
            entity.HasIndex(x => x.IsActive);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        builder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Level).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(256);
            entity.Property(x => x.Client).HasMaxLength(32).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(64);
            entity.Property(x => x.ClientIp).HasMaxLength(64);
            entity.Property(x => x.OccurredAtUtc).IsRequired();
            entity.HasIndex(x => x.OccurredAtUtc);
            entity.HasIndex(x => x.CorrelationId);
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.Level);
        });

        builder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("AuditEvents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Action).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Outcome).HasMaxLength(32).IsRequired();
            entity.Property(x => x.SubjectType).HasMaxLength(64);
            entity.Property(x => x.SubjectId).HasMaxLength(64);
            entity.Property(x => x.Audience).HasMaxLength(64);
            entity.Property(x => x.Changes).HasColumnType("jsonb");
            entity.Property(x => x.MetadataJson).HasColumnType("jsonb");
            entity.HasIndex(x => x.AuditLogId).IsUnique();
            entity.HasIndex(x => x.Action);
            entity.HasIndex(x => x.SubjectUserId);
            entity.HasIndex(x => x.Outcome);

            entity.HasOne(x => x.AuditLog)
                .WithOne(x => x.AuditEvent)
                .HasForeignKey<AuditEvent>(x => x.AuditLogId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
