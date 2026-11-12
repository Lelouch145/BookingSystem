using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingSystem.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeCheckoutSessionId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentModell_BookingId",
                table: "PaymentModell");

            migrationBuilder.AlterColumn<string>(
                name: "StripePaymentIntentId",
                table: "PaymentModell",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "StripeCheckOutSessionId",
                table: "PaymentModell",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentModell_BookingId",
                table: "PaymentModell",
                column: "BookingId",
                unique: true,
                filter: "[Status] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentModell_BookingId",
                table: "PaymentModell");

            migrationBuilder.DropColumn(
                name: "StripeCheckOutSessionId",
                table: "PaymentModell");

            migrationBuilder.AlterColumn<string>(
                name: "StripePaymentIntentId",
                table: "PaymentModell",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentModell_BookingId",
                table: "PaymentModell",
                column: "BookingId");
        }
    }
}
