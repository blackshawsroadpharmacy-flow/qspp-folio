using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QsppManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M2PageAnchors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // M2: verified page-anchor provenance on requirements (imported from
            // seed/page-anchors.json). InitialCreate represents the v1 EnsureCreated-era
            // schema; this migration is what adds the column on a fresh install.
            migrationBuilder.AddColumn<bool>(
                name: "AnchorVerified",
                table: "Requirements",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnchorVerified",
                table: "Requirements");
        }
    }
}
