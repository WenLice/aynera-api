using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aynera.Persistence.Migrations
{
    /// <summary>
    /// Conversation prompts and their recordings, the profile-editor extras, and one settings table
    /// for every "how Aynera treats me" choice — notifications, pause, and which fields show.
    /// <para>
    /// Hand-ordered, because the scaffold dropped <c>MemberProfiles.GenderIsPublic</c> before anything
    /// could copy it, which would have quietly un-hidden every hidden gender, and its <c>Down</c>
    /// re-added the column as <c>false</c>, which would have hidden everyone's. Visibility is copied
    /// into <c>MemberSettings.Visibility</c> first — the gender and each everyday/belief answer's
    /// <c>public</c> flag — and only then are the old places cleared.
    /// </para>
    /// </summary>
    public partial class AddVoiceAnswersAndMemberSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MemberMedia_UserId_Kind",
                table: "MemberMedia");

            migrationBuilder.AddColumn<string>(
                name: "Dealbreaker",
                table: "MemberProfileAnswers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Prompts",
                table: "MemberProfileAnswers",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "Rhythm",
                table: "MemberProfileAnswers",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "PromptId",
                table: "MemberMedia",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MemberSettings",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotifyIntroductions = table.Column<bool>(type: "boolean", nullable: true),
                    NotifyReplies = table.Column<bool>(type: "boolean", nullable: true),
                    NotifyWeekendSurprise = table.Column<bool>(type: "boolean", nullable: true),
                    IntroductionsPaused = table.Column<bool>(type: "boolean", nullable: false),
                    PausedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Visibility = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberSettings", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_MemberSettings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // One settings row per member who has a profile or answers, carrying every visibility
            // choice they have made so far. A deleted account's row starts deleted too. A "Prefer not
            // to say" answer is never published, so it has no visibility entry.
            migrationBuilder.Sql("""
                INSERT INTO "MemberSettings" ("UserId", "IntroductionsPaused", "Visibility", "CreatedAtUtc", "IsDeleted", "DeletedAtUtc")
                SELECT m."UserId",
                       false,
                       COALESCE(jsonb_object_agg(v.key, v.shown) FILTER (WHERE v.key IS NOT NULL), '{}'::jsonb),
                       now(),
                       u."IsDeleted",
                       u."DeletedAtUtc"
                FROM (SELECT "UserId" FROM "MemberProfiles"
                      UNION
                      SELECT "UserId" FROM "MemberProfileAnswers") m
                JOIN "AspNetUsers" u ON u."Id" = m."UserId"
                LEFT JOIN (
                    SELECT "UserId", 'gender' AS key, "GenderIsPublic" AS shown
                    FROM "MemberProfiles"
                    UNION ALL
                    SELECT a."UserId", 'lifestyle.' || e.key, COALESCE((e.value->>'public')::boolean, true)
                    FROM "MemberProfileAnswers" a, jsonb_each(a."Lifestyle") e
                    WHERE lower(e.value->>'option') <> 'prefer not to say'
                    UNION ALL
                    SELECT a."UserId", 'beliefs.' || e.key, COALESCE((e.value->>'public')::boolean, true)
                    FROM "MemberProfileAnswers" a, jsonb_each(a."Beliefs") e
                    WHERE lower(e.value->>'option') <> 'prefer not to say'
                ) v ON v."UserId" = m."UserId"
                GROUP BY m."UserId", u."IsDeleted", u."DeletedAtUtc";
                """);

            // The answers now keep only the choice; visibility lives in the settings row.
            migrationBuilder.Sql("""
                UPDATE "MemberProfileAnswers" SET
                    "Lifestyle" = COALESCE((SELECT jsonb_object_agg(e.key, e.value - 'public') FROM jsonb_each("Lifestyle") e), '{}'::jsonb),
                    "Beliefs"   = COALESCE((SELECT jsonb_object_agg(e.key, e.value - 'public') FROM jsonb_each("Beliefs") e), '{}'::jsonb);
                """);

            migrationBuilder.DropColumn(
                name: "GenderIsPublic",
                table: "MemberProfiles");

            migrationBuilder.CreateIndex(
                name: "IX_MemberMedia_UserId_Kind",
                table: "MemberMedia",
                columns: new[] { "UserId", "Kind" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Index\" IS NULL AND \"PromptId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MemberMedia_UserId_Kind_PromptId",
                table: "MemberMedia",
                columns: new[] { "UserId", "Kind", "PromptId" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"PromptId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MemberSettings_IntroductionsPaused",
                table: "MemberSettings",
                column: "IntroductionsPaused");

            migrationBuilder.CreateIndex(
                name: "IX_MemberSettings_IsDeleted",
                table: "MemberSettings",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Visibility goes back to where it lived before the settings table is dropped. Shown is
            // the default, exactly as a missing entry means today.
            migrationBuilder.AddColumn<bool>(
                name: "GenderIsPublic",
                table: "MemberProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql("""
                UPDATE "MemberProfiles" p
                SET "GenderIsPublic" = COALESCE((s."Visibility"->>'gender')::boolean, true)
                FROM "MemberSettings" s
                WHERE s."UserId" = p."UserId";

                UPDATE "MemberProfileAnswers" a SET
                    "Lifestyle" = COALESCE((
                        SELECT jsonb_object_agg(e.key, e.value || jsonb_build_object(
                            'public', COALESCE((s."Visibility"->>('lifestyle.' || e.key))::boolean, true)))
                        FROM jsonb_each(a."Lifestyle") e), '{}'::jsonb),
                    "Beliefs" = COALESCE((
                        SELECT jsonb_object_agg(e.key, e.value || jsonb_build_object(
                            'public', COALESCE((s."Visibility"->>('beliefs.' || e.key))::boolean, true)))
                        FROM jsonb_each(a."Beliefs") e), '{}'::jsonb)
                FROM "MemberSettings" s
                WHERE s."UserId" = a."UserId";
                """);

            migrationBuilder.DropTable(
                name: "MemberSettings");

            migrationBuilder.DropIndex(
                name: "IX_MemberMedia_UserId_Kind",
                table: "MemberMedia");

            migrationBuilder.DropIndex(
                name: "IX_MemberMedia_UserId_Kind_PromptId",
                table: "MemberMedia");

            migrationBuilder.DropColumn(
                name: "Dealbreaker",
                table: "MemberProfileAnswers");

            migrationBuilder.DropColumn(
                name: "Prompts",
                table: "MemberProfileAnswers");

            migrationBuilder.DropColumn(
                name: "Rhythm",
                table: "MemberProfileAnswers");

            migrationBuilder.DropColumn(
                name: "PromptId",
                table: "MemberMedia");

            migrationBuilder.CreateIndex(
                name: "IX_MemberMedia_UserId_Kind",
                table: "MemberMedia",
                columns: new[] { "UserId", "Kind" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Index\" IS NULL");
        }
    }
}
