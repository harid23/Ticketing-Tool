using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ReleaseTicketing.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateThreeRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM user_roles
                WHERE RoleId IN (3, 4, 5, 6)
                  AND EXISTS (
                      SELECT 1
                      FROM user_roles AS existing
                      WHERE existing.UserId = user_roles.UserId
                        AND existing.RoleId = CASE user_roles.RoleId WHEN 4 THEN 2 ELSE 7 END
                  );

                UPDATE user_roles
                SET RoleId = CASE RoleId WHEN 4 THEN 2 ELSE 7 END
                WHERE RoleId IN (3, 4, 5, 6);
                """);

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.UpdateData(
                table: "roles",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Description", "Name" },
                values: new object[] { "ASDM", "ASDM" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "Id", "Description", "Name" },
                values: new object[,]
                {
                    { 3, "PMO", "PMO" },
                    { 4, "ClusterManager", "ClusterManager" },
                    { 5, "Head", "Head" },
                    { 6, "CompetencyPmo", "CompetencyPmo" }
                });

            migrationBuilder.UpdateData(
                table: "roles",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Description", "Name" },
                values: new object[] { "SDM", "SDM" });
        }
    }
}
