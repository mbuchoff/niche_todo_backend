// ABOUTME: Migration adding parent relationships and scoped ordering for todo items.
// ABOUTME: Enables nested todo hierarchies and sibling-specific sort order.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoBackend.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTodoHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_todos_UserId_SortOrder",
                table: "todos");

            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                table: "todos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_todos_UserId_ParentId_SortOrder",
                table: "todos",
                columns: new[] { "UserId", "ParentId", "SortOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_todos_todos_ParentId",
                table: "todos",
                column: "ParentId",
                principalTable: "todos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_todos_todos_ParentId",
                table: "todos");

            migrationBuilder.DropIndex(
                name: "IX_todos_UserId_ParentId_SortOrder",
                table: "todos");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "todos");

            migrationBuilder.CreateIndex(
                name: "IX_todos_UserId_SortOrder",
                table: "todos",
                columns: new[] { "UserId", "SortOrder" });
        }
    }
}
