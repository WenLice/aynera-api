using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aynera.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameStaffIdentityToAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "AspNetUsers"
                SET "AccountKind" = 'Admin'
                WHERE "AccountKind" = 'Staff';

                UPDATE "AspNetRoles"
                SET "Name" = 'admin',
                    "NormalizedName" = 'ADMIN'
                WHERE "NormalizedName" = 'STAFF';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "AspNetUsers"
                SET "AccountKind" = 'Staff'
                WHERE "AccountKind" = 'Admin';

                UPDATE "AspNetRoles"
                SET "Name" = 'staff',
                    "NormalizedName" = 'STAFF'
                WHERE "NormalizedName" = 'ADMIN';
                """);
        }
    }
}
