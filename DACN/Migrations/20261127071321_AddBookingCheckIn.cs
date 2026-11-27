using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DACN.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingCheckIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckInCode",
                table: "Bookings",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CheckedInAt",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckedInBy",
                table: "Bookings",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            // Cấp mã vé ngẫu nhiên cho các đơn đã có (cùng bảng chữ cái với TicketCode.New)
            var pick = "SUBSTRING(@a, ABS(CHECKSUM(NEWID())) % 31 + 1, 1)";
            migrationBuilder.Sql(
                "DECLARE @a varchar(31) = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789'; " +
                $"UPDATE Bookings SET CheckInCode = {pick}+{pick}+{pick}+{pick}+{pick}+{pick}+{pick}+{pick} WHERE CheckInCode IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_CheckInCode",
                table: "Bookings",
                column: "CheckInCode",
                unique: true,
                filter: "[CheckInCode] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_CheckInCode",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CheckInCode",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CheckedInAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CheckedInBy",
                table: "Bookings");
        }
    }
}
