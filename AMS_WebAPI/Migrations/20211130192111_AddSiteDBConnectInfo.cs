using Microsoft.EntityFrameworkCore.Migrations;

namespace AMS_WebAPI.Migrations
{
    public partial class AddSiteDBConnectInfo : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DBPassword",
                table: "AspNetUsers",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DBUID",
                table: "AspNetUsers",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DBPassword",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DBUID",
                table: "AspNetUsers");
        }
    }
}
