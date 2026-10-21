using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MietHahn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeadlineCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "deadline_reminders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    due = table.Column<DateOnly>(type: "date", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deadline_reminders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "deadline_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    default_lead_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    send_reminders = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    send_weekly_summary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    calendar_token = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deadline_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recurring_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "german"),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    interval_months = table.Column<int>(type: "integer", nullable: false),
                    lead_days = table.Column<int>(type: "integer", nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    last_done_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_recurring_tasks_properties_property_id",
                        column: x => x.property_id,
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_completions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    done_on = table.Column<DateOnly>(type: "date", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_completions", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_completions_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_completions_recurring_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "recurring_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_deadline_reminders_key_due",
                table: "deadline_reminders",
                columns: new[] { "key", "due" },
                unique: true,
                filter: "NOT deleted");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_tasks_due_on",
                table: "recurring_tasks",
                column: "due_on",
                filter: "active AND NOT deleted");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_tasks_property_id",
                table: "recurring_tasks",
                column: "property_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_completions_document_id",
                table: "task_completions",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_completions_task_id_done_on",
                table: "task_completions",
                columns: new[] { "task_id", "done_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deadline_reminders");

            migrationBuilder.DropTable(
                name: "deadline_settings");

            migrationBuilder.DropTable(
                name: "task_completions");

            migrationBuilder.DropTable(
                name: "recurring_tasks");
        }
    }
}
