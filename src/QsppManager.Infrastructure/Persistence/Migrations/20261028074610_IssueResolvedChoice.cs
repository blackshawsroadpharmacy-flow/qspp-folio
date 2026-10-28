using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QsppManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IssueResolvedChoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResolvedChoice",
                table: "InterpretationIssues",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResolvedChoice",
                table: "InterpretationIssues");
        }
    }
}
