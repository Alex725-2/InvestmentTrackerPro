using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestmentTracker.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddBondDetailsToSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AccruedInterest",
                table: "Securities",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FaceValue",
                table: "Securities",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "IssueSize",
                table: "Securities",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextCouponDate",
                table: "Securities",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Rating",
                table: "Securities",
                type: "TEXT",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccruedInterest",
                table: "Securities");

            migrationBuilder.DropColumn(
                name: "FaceValue",
                table: "Securities");

            migrationBuilder.DropColumn(
                name: "IssueSize",
                table: "Securities");

            migrationBuilder.DropColumn(
                name: "NextCouponDate",
                table: "Securities");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "Securities");
        }
    }
}
