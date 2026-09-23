using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aynera.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Photos and videos move out of Postgres into object storage (Cloudflare R2): one
    /// <c>MemberMedia</c> table holds the object key and the check results, never the bytes.
    /// <para>
    /// The old tables are dropped, not copied. SQL cannot move bytes into a bucket, and when this
    /// was written the development database held no media and the test database only fixtures.
    /// Check any other database (Neon) for rows in <c>MemberPhotos</c>/<c>MemberIntroductionVideos</c>
    /// before applying it there: whatever they hold is lost. <c>Down</c> recreates empty tables.
    /// </para>
    /// </summary>
    public partial class MoveMediaToObjectStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemberIntroductionVideos");

            migrationBuilder.DropTable(
                name: "MemberPhotos");

            migrationBuilder.CreateTable(
                name: "MemberMedia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Index = table.Column<int>(type: "integer", nullable: true),
                    StorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ByteSize = table.Column<int>(type: "integer", nullable: false),
                    IsReference = table.Column<bool>(type: "boolean", nullable: false),
                    FaceMatchStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FaceMatchScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    GuidelinePassed = table.Column<bool>(type: "boolean", nullable: true),
                    GuidelineDetail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Transcript = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberMedia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemberMedia_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemberMedia_IsDeleted",
                table: "MemberMedia",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_MemberMedia_UserId",
                table: "MemberMedia",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_MemberMedia_UserId_Kind",
                table: "MemberMedia",
                columns: new[] { "UserId", "Kind" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Index\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MemberMedia_UserId_Kind_Index",
                table: "MemberMedia",
                columns: new[] { "UserId", "Kind", "Index" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Index\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemberMedia");

            migrationBuilder.CreateTable(
                name: "MemberIntroductionVideos",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ByteSize = table.Column<int>(type: "integer", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FaceMatchScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    FaceMatchStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GuidelineDetail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    GuidelinePassed = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Transcript = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberIntroductionVideos", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_MemberIntroductionVideos_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MemberPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ByteSize = table.Column<int>(type: "integer", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FaceMatchScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    FaceMatchStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    IsReference = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemberPhotos_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemberIntroductionVideos_IsDeleted",
                table: "MemberIntroductionVideos",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_MemberPhotos_IsDeleted",
                table: "MemberPhotos",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_MemberPhotos_UserId",
                table: "MemberPhotos",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_MemberPhotos_UserId_SortOrder",
                table: "MemberPhotos",
                columns: new[] { "UserId", "SortOrder" });
        }
    }
}
