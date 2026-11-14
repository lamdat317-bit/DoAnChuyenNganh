using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DACN.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchmakingTeamwork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CityId",
                table: "Facilities",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DistrictName",
                table: "Facilities",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CityProvinces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CityProvinces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TotalBookingsCount = table.Column<int>(type: "int", nullable: false),
                    TotalSpentAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    VipTier = table.Column<int>(type: "int", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerProfiles_AspNetUsers_ApplicationUserId",
                        column: x => x.ApplicationUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FacilityServices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FacilityId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacilityServices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FacilityServices_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchPosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatorId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SportTypeId = table.Column<int>(type: "int", nullable: false),
                    FacilityId = table.Column<int>(type: "int", nullable: true),
                    BookingId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    VenueName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    VenueAddress = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PostType = table.Column<int>(type: "int", nullable: false),
                    MatchDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    SkillLevel = table.Column<int>(type: "int", nullable: false),
                    TotalSlots = table.Column<int>(type: "int", nullable: false),
                    CurrentSlots = table.Column<int>(type: "int", nullable: false),
                    CostPerPerson = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CostSharingNote = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ContactPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchPosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchPosts_AspNetUsers_CreatorId",
                        column: x => x.CreatorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchPosts_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MatchPosts_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MatchPosts_SportTypes_SportTypeId",
                        column: x => x.SportTypeId,
                        principalTable: "SportTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FacilityId = table.Column<int>(type: "int", nullable: false),
                    TelegramChatId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TelegramBotToken = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ZaloOaToken = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NotifyOnNewBooking = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationConfigs_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookingServiceDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    FacilityServiceId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    SubTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingServiceDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingServiceDetails_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookingServiceDetails_FacilityServices_FacilityServiceId",
                        column: x => x.FacilityServiceId,
                        principalTable: "FacilityServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatchJoinRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MatchPostId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PreferredPosition = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ContactPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchJoinRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchJoinRequests_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchJoinRequests_MatchPosts_MatchPostId",
                        column: x => x.MatchPostId,
                        principalTable: "MatchPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Facilities_CityId",
                table: "Facilities",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingServiceDetails_BookingId",
                table: "BookingServiceDetails",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingServiceDetails_FacilityServiceId",
                table: "BookingServiceDetails",
                column: "FacilityServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerProfiles_ApplicationUserId",
                table: "CustomerProfiles",
                column: "ApplicationUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacilityServices_FacilityId",
                table: "FacilityServices",
                column: "FacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchJoinRequests_MatchPostId_UserId",
                table: "MatchJoinRequests",
                columns: new[] { "MatchPostId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchJoinRequests_UserId",
                table: "MatchJoinRequests",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPosts_BookingId",
                table: "MatchPosts",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPosts_CreatorId",
                table: "MatchPosts",
                column: "CreatorId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPosts_FacilityId",
                table: "MatchPosts",
                column: "FacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPosts_MatchDate",
                table: "MatchPosts",
                column: "MatchDate");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPosts_PostType",
                table: "MatchPosts",
                column: "PostType");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPosts_SportTypeId",
                table: "MatchPosts",
                column: "SportTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPosts_Status",
                table: "MatchPosts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationConfigs_FacilityId",
                table: "NotificationConfigs",
                column: "FacilityId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Facilities_CityProvinces_CityId",
                table: "Facilities",
                column: "CityId",
                principalTable: "CityProvinces",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Facilities_CityProvinces_CityId",
                table: "Facilities");

            migrationBuilder.DropTable(
                name: "BookingServiceDetails");

            migrationBuilder.DropTable(
                name: "CityProvinces");

            migrationBuilder.DropTable(
                name: "CustomerProfiles");

            migrationBuilder.DropTable(
                name: "MatchJoinRequests");

            migrationBuilder.DropTable(
                name: "NotificationConfigs");

            migrationBuilder.DropTable(
                name: "FacilityServices");

            migrationBuilder.DropTable(
                name: "MatchPosts");

            migrationBuilder.DropIndex(
                name: "IX_Facilities_CityId",
                table: "Facilities");

            migrationBuilder.DropColumn(
                name: "CityId",
                table: "Facilities");

            migrationBuilder.DropColumn(
                name: "DistrictName",
                table: "Facilities");
        }
    }
}
