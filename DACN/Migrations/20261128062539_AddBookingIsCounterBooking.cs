using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DACN.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingIsCounterBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCounterBooking",
                table: "Bookings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Đơn cũ do chủ sân / Admin đặt hộ tại quầy: người đặt chính là chủ cụm sân, hoặc là Admin (Role = 3)
            migrationBuilder.Sql(@"
UPDATE b SET b.IsCounterBooking = 1
FROM Bookings b
JOIN Courts c ON c.Id = b.CourtId
JOIN Facilities f ON f.Id = c.FacilityId
LEFT JOIN AspNetUsers u ON u.Id = b.CustomerId
WHERE b.CustomerId = f.OwnerId OR u.Role = 3;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsCounterBooking",
                table: "Bookings");
        }
    }
}
