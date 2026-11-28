using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DACN.Migrations
{
    /// <inheritdoc />
    public partial class AddRecentlyViewedFacilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecentlyViewedFacilities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FacilityId = table.Column<int>(type: "int", nullable: false),
                    ViewedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecentlyViewedFacilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecentlyViewedFacilities_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecentlyViewedFacilities_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecentlyViewedFacilities_FacilityId",
                table: "RecentlyViewedFacilities",
                column: "FacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_RecentlyViewedFacilities_UserId_FacilityId",
                table: "RecentlyViewedFacilities",
                columns: new[] { "UserId", "FacilityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecentlyViewedFacilities_UserId_ViewedAt",
                table: "RecentlyViewedFacilities",
                columns: new[] { "UserId", "ViewedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecentlyViewedFacilities");
        }
    }
}
