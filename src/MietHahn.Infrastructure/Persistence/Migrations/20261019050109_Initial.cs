using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using MietHahn.Infrastructure.Persistence.MigrationHelpers;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MietHahn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:CollationDefinition:german", "de-DE,de-DE,icu,True")
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.CreateTable(
                name: "audit_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    changes = table.Column<string>(type: "jsonb", nullable: false),
                    purpose = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bank_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "german"),
                    iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: false),
                    bic = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    bank = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    property_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cpi_values",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    value = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: false),
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cpi_values", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "csv_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    delimiter = table.Column<char>(type: "character(1)", nullable: false),
                    charset = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    date_format = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    decimal_separator = table.Column<char>(type: "character(1)", nullable: false),
                    booking_date_column = table.Column<string>(type: "text", nullable: false),
                    value_date_column = table.Column<string>(type: "text", nullable: true),
                    amount_column = table.Column<string>(type: "text", nullable: true),
                    debit_column = table.Column<string>(type: "text", nullable: true),
                    credit_column = table.Column<string>(type: "text", nullable: true),
                    name_column = table.Column<string>(type: "text", nullable: true),
                    incoming_name_column = table.Column<string>(type: "text", nullable: true),
                    outgoing_name_column = table.Column<string>(type: "text", nullable: true),
                    iban_column = table.Column<string>(type: "text", nullable: true),
                    bic_column = table.Column<string>(type: "text", nullable: true),
                    purpose_column = table.Column<string>(type: "text", nullable: true),
                    booking_text_column = table.Column<string>(type: "text", nullable: true),
                    end_to_end_column = table.Column<string>(type: "text", nullable: true),
                    mandate_reference_column = table.Column<string>(type: "text", nullable: true),
                    creditor_id_column = table.Column<string>(type: "text", nullable: true),
                    currency_column = table.Column<string>(type: "text", nullable: true),
                    status_column = table.Column<string>(type: "text", nullable: true),
                    parse_pattern = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_csv_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "finance_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    accounting_start = table.Column<DateOnly>(type: "date", nullable: true),
                    allocation = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_finance_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    succeeded = table.Column<bool>(type: "boolean", nullable: true),
                    errors = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "landlord_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    postal_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    bic = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    bank = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tax_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_landlord_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outgoing_mails",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    to = table.Column<List<string>>(type: "text[]", nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    html = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    attachment_file_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    template = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outgoing_mails", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stored_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stored_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_sign_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    locked = table.Column<bool>(type: "boolean", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bank_imports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    profile = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    read = table.Column<int>(type: "integer", nullable: false),
                    added = table.Column<int>(type: "integer", nullable: false),
                    duplicates = table.Column<int>(type: "integer", nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: true),
                    to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_imports", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_imports_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_claims_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "properties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "german"),
                    street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    postal_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    year_built = table.Column<int>(type: "integer", nullable: true),
                    cadastral_district = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    parcel_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    property_tax_reference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    heating_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    energy_source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    image_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_properties", x => x.id);
                    table.ForeignKey(
                        name: "fk_properties_stored_file_image_file_id",
                        column: x => x.image_file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "magic_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    redeemed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_magic_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_magic_links_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    salutation = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    title = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, collation: "german"),
                    last_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "german"),
                    company = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, collation: "german"),
                    contact_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    occupation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    mobile = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    anonymized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_people", x => x.id);
                    table.ForeignKey(
                        name: "fk_people_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bank_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_import_id = table.Column<Guid>(type: "uuid", nullable: true),
                    booking_date = table.Column<DateOnly>(type: "date", nullable: false),
                    value_date = table.Column<DateOnly>(type: "date", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    counterparty_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    counterparty_iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    counterparty_bic = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    purpose = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    booking_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    end_to_end_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    mandate_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    creditor_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_transactions_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bank_transactions_bank_imports_bank_import_id",
                        column: x => x.bank_import_id,
                        principalTable: "bank_imports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "property_areas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total_area = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    common_area = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    validity_from = table.Column<DateOnly>(type: "date", nullable: false),
                    validity_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_property_areas", x => x.id);
                    table.ForeignKey(
                        name: "fk_property_areas_property_property_id",
                        column: x => x.property_id,
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "property_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_property_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_property_assignments_property_property_id",
                        column: x => x.property_id,
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_property_assignments_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "units",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "german"),
                    location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    room_count = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    bathroom = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    balcony = table.Column<bool>(type: "boolean", nullable: false),
                    fitted_kitchen = table.Column<bool>(type: "boolean", nullable: false),
                    energy_standard = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    amenities = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_units", x => x.id);
                    table.ForeignKey(
                        name: "fk_units_properties_property_id",
                        column: x => x.property_id,
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bank_details",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    iban = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    account_holder = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    bic = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_details", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_details_person_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "person_addresses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    postal_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    validity_from = table.Column<DateOnly>(type: "date", nullable: false),
                    validity_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_person_addresses", x => x.id);
                    table.ForeignKey(
                        name: "fk_person_addresses_person_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supplier_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, collation: "german"),
                    invoice_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: false),
                    service_from = table.Column<DateOnly>(type: "date", nullable: true),
                    service_to = table.Column<DateOnly>(type: "date", nullable: true),
                    gross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    vat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: true),
                    fuel_quantity = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: true),
                    fuel_unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    co2kg = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: true),
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
                    table.PrimaryKey("pk_receipts", x => x.id);
                    table.ForeignKey(
                        name: "fk_receipts_people_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rooms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "german"),
                    order = table.Column<int>(type: "integer", nullable: false),
                    furnishing = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rooms", x => x.id);
                    table.ForeignKey(
                        name: "fk_rooms_unit_unit_id",
                        column: x => x.unit_id,
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "unit_areas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    living_area = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    heated_area = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    validity_from = table.Column<DateOnly>(type: "date", nullable: false),
                    validity_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unit_areas", x => x.id);
                    table.ForeignKey(
                        name: "fk_unit_areas_unit_unit_id",
                        column: x => x.unit_id,
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cost_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allocable = table.Column<bool>(type: "boolean", nullable: false),
                    section35a = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    labor_costs = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    billing_period_from = table.Column<DateOnly>(type: "date", nullable: false),
                    billing_period_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_cost_items_property_property_id",
                        column: x => x.property_id,
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cost_items_receipt_receipt_id",
                        column: x => x.receipt_id,
                        principalTable: "receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cost_items_unit_unit_id",
                        column: x => x.unit_id,
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "receipt_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipt_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_receipt_files_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalTable: "receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_receipt_files_stored_file_file_id",
                        column: x => x.file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rental_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fixed_term_until = table.Column<DateOnly>(type: "date", nullable: true),
                    termination_received_on = table.Column<DateOnly>(type: "date", nullable: true),
                    terminated_by = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    notice_period_months = table.Column<int>(type: "integer", nullable: false),
                    billing_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    heating_costs_consumption_based = table.Column<bool>(type: "boolean", nullable: false),
                    due_on_working_day = table.Column<int>(type: "integer", nullable: false),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    deposit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    deposit_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    rent_model = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    index_base_month = table.Column<DateOnly>(type: "date", nullable: true),
                    index_base_value = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: true),
                    term_from = table.Column<DateOnly>(type: "date", nullable: false),
                    term_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leases", x => x.id);
                    table.ForeignKey(
                        name: "fk_leases_room_room_id",
                        column: x => x.room_id,
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leases_unit_unit_id",
                        column: x => x.unit_id,
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    room_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    measurement_unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "german"),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    main_meter_id = table.Column<Guid>(type: "uuid", nullable: true),
                    predecessor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    installed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    removed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    calibration_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meters", x => x.id);
                    table.ForeignKey(
                        name: "fk_meters_meters_main_meter_id",
                        column: x => x.main_meter_id,
                        principalTable: "meters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_meters_meters_predecessor_id",
                        column: x => x.predecessor_id,
                        principalTable: "meters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_meters_property_property_id",
                        column: x => x.property_id,
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_meters_room_room_id",
                        column: x => x.room_id,
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_meters_unit_unit_id",
                        column: x => x.unit_id,
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "room_areas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    area = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    common_area_share = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    validity_from = table.Column<DateOnly>(type: "date", nullable: false),
                    validity_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_areas", x => x.id);
                    table.ForeignKey(
                        name: "fk_room_areas_room_room_id",
                        column: x => x.room_id,
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "charges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    component = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    service_month = table.Column<DateOnly>(type: "date", nullable: true),
                    due = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_charges", x => x.id);
                    table.ForeignKey(
                        name: "fk_charges_lease_lease_id",
                        column: x => x.lease_id,
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "german"),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    property_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    shared_with_tenants = table.Column<bool>(type: "boolean", nullable: false),
                    shared_with_caretakers = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_documents_lease_lease_id",
                        column: x => x.lease_id,
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documents_person_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documents_property_property_id",
                        column: x => x.property_id,
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documents_stored_file_file_id",
                        column: x => x.file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documents_unit_unit_id",
                        column: x => x.unit_id,
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lease_parties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lease_parties", x => x.id);
                    table.ForeignKey(
                        name: "fk_lease_parties_leases_lease_id",
                        column: x => x.lease_id,
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_lease_parties_person_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "occupant_counts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                    validity_from = table.Column<DateOnly>(type: "date", nullable: false),
                    validity_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_occupant_counts", x => x.id);
                    table.ForeignKey(
                        name: "fk_occupant_counts_leases_lease_id",
                        column: x => x.lease_id,
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rent_components",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    validity_from = table.Column<DateOnly>(type: "date", nullable: false),
                    validity_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rent_components", x => x.id);
                    table.ForeignKey(
                        name: "fk_rent_components_leases_lease_id",
                        column: x => x.lease_id,
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rent_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    base_rent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rent_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_rent_steps_leases_lease_id",
                        column: x => x.lease_id,
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "meter_readings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    meter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    value = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    read_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    photo_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meter_readings", x => x.id);
                    table.ForeignKey(
                        name: "fk_meter_readings_meters_meter_id",
                        column: x => x.meter_id,
                        principalTable: "meters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_meter_readings_stored_file_photo_file_id",
                        column: x => x.photo_file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    purpose = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    charge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_payments_charges_charge_id",
                        column: x => x.charge_id,
                        principalTable: "charges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_payments_leases_lease_id",
                        column: x => x.lease_id,
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_entity_type_entity_id_timestamp",
                table: "audit_entries",
                columns: new[] { "entity_type", "entity_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_timestamp",
                table: "audit_entries",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_iban",
                table: "bank_accounts",
                column: "iban",
                unique: true,
                filter: "NOT deleted");

            migrationBuilder.CreateIndex(
                name: "ix_bank_details_iban",
                table: "bank_details",
                column: "iban");

            migrationBuilder.CreateIndex(
                name: "ix_bank_details_person_id",
                table: "bank_details",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_imports_bank_account_id",
                table: "bank_imports",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_transactions_bank_account_id_hash",
                table: "bank_transactions",
                columns: new[] { "bank_account_id", "hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bank_transactions_bank_account_id_status_booking_date",
                table: "bank_transactions",
                columns: new[] { "bank_account_id", "status", "booking_date" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_transactions_bank_import_id",
                table: "bank_transactions",
                column: "bank_import_id");

            migrationBuilder.CreateIndex(
                name: "ix_charges_lease_id_due",
                table: "charges",
                columns: new[] { "lease_id", "due" });

            migrationBuilder.CreateIndex(
                name: "ix_charges_monthly_rent_unique",
                table: "charges",
                columns: new[] { "lease_id", "service_month", "component" },
                unique: true,
                filter: "kind = 'Rent' AND NOT deleted");

            migrationBuilder.CreateIndex(
                name: "ix_cost_items_property_id_cost_type",
                table: "cost_items",
                columns: new[] { "property_id", "cost_type" });

            migrationBuilder.CreateIndex(
                name: "ix_cost_items_receipt_id",
                table: "cost_items",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_cost_items_unit_id",
                table: "cost_items",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_cpi_values_month",
                table: "cpi_values",
                column: "month",
                unique: true,
                filter: "NOT deleted");

            migrationBuilder.CreateIndex(
                name: "ix_csv_profiles_name",
                table: "csv_profiles",
                column: "name",
                unique: true,
                filter: "NOT deleted");

            migrationBuilder.CreateIndex(
                name: "ix_documents_expires_on",
                table: "documents",
                column: "expires_on",
                filter: "expires_on IS NOT NULL AND NOT deleted");

            migrationBuilder.CreateIndex(
                name: "ix_documents_file_id",
                table: "documents",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_lease_id",
                table: "documents",
                column: "lease_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_person_id",
                table: "documents",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_property_id",
                table: "documents",
                column: "property_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_unit_id",
                table: "documents",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_runs_job_name_started_at",
                table: "job_runs",
                columns: new[] { "job_name", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_lease_parties_lease_id_person_id",
                table: "lease_parties",
                columns: new[] { "lease_id", "person_id" },
                unique: true,
                filter: "NOT deleted");

            migrationBuilder.CreateIndex(
                name: "ix_lease_parties_person_id",
                table: "lease_parties",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_leases_rental_unit_id",
                table: "leases",
                column: "rental_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_leases_room_id",
                table: "leases",
                column: "room_id");

            migrationBuilder.CreateIndex(
                name: "ix_leases_unit_id",
                table: "leases",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_magic_links_token_hash",
                table: "magic_links",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_magic_links_user_id",
                table: "magic_links",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_meter_readings_meter_id_date",
                table: "meter_readings",
                columns: new[] { "meter_id", "date" },
                unique: true,
                filter: "NOT deleted");

            migrationBuilder.CreateIndex(
                name: "ix_meter_readings_photo_file_id",
                table: "meter_readings",
                column: "photo_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_meters_main_meter_id",
                table: "meters",
                column: "main_meter_id");

            migrationBuilder.CreateIndex(
                name: "ix_meters_number",
                table: "meters",
                column: "number");

            migrationBuilder.CreateIndex(
                name: "ix_meters_predecessor_id",
                table: "meters",
                column: "predecessor_id");

            migrationBuilder.CreateIndex(
                name: "ix_meters_property_id",
                table: "meters",
                column: "property_id");

            migrationBuilder.CreateIndex(
                name: "ix_meters_room_id",
                table: "meters",
                column: "room_id");

            migrationBuilder.CreateIndex(
                name: "ix_meters_unit_id",
                table: "meters",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_occupant_counts_lease_id",
                table: "occupant_counts",
                column: "lease_id");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_mails_next_attempt_at",
                table: "outgoing_mails",
                column: "next_attempt_at",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_mails_reference",
                table: "outgoing_mails",
                column: "reference");

            migrationBuilder.CreateIndex(
                name: "ix_payments_charge_id",
                table: "payments",
                column: "charge_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_lease_id_date",
                table: "payments",
                columns: new[] { "lease_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_people_last_name",
                table: "people",
                column: "last_name");

            migrationBuilder.CreateIndex(
                name: "ix_people_user_id",
                table: "people",
                column: "user_id",
                unique: true,
                filter: "user_id IS NOT NULL AND NOT deleted");

            migrationBuilder.CreateIndex(
                name: "ix_person_addresses_person_id",
                table: "person_addresses",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_properties_image_file_id",
                table: "properties",
                column: "image_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_property_areas_property_id",
                table: "property_areas",
                column: "property_id");

            migrationBuilder.CreateIndex(
                name: "ix_property_assignments_property_id",
                table: "property_assignments",
                column: "property_id");

            migrationBuilder.CreateIndex(
                name: "ix_property_assignments_user_id_property_id",
                table: "property_assignments",
                columns: new[] { "user_id", "property_id" },
                unique: true,
                filter: "NOT deleted");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_files_file_id",
                table: "receipt_files",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_files_receipt_id",
                table: "receipt_files",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_invoice_date",
                table: "receipts",
                column: "invoice_date");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_supplier_id",
                table: "receipts",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_rent_components_lease_id",
                table: "rent_components",
                column: "lease_id");

            migrationBuilder.CreateIndex(
                name: "ix_rent_steps_lease_id_from",
                table: "rent_steps",
                columns: new[] { "lease_id", "from" },
                unique: true,
                filter: "NOT deleted");

            migrationBuilder.CreateIndex(
                name: "ix_role_claims_role_id",
                table: "role_claims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "roles",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_room_areas_room_id",
                table: "room_areas",
                column: "room_id");

            migrationBuilder.CreateIndex(
                name: "ix_rooms_unit_id",
                table: "rooms",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_stored_files_sha256",
                table: "stored_files",
                column: "sha256");

            migrationBuilder.CreateIndex(
                name: "ix_unit_areas_unit_id",
                table: "unit_areas",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_units_property_id",
                table: "units",
                column: "property_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "users",
                column: "normalized_user_name",
                unique: true);

            // Tables for the persistent Quartz job store (official script, embedded resource).
            migrationBuilder.CreateQuartzSchema();

            // Time series and leases must not overlap (exclusion constraints, deferred to the end of the transaction).
            migrationBuilder.CreateOverlapProtection("ex_property_areas_property_validity", "property_areas", "validity_from", "validity_to", "property_id");
            migrationBuilder.CreateOverlapProtection("ex_unit_areas_unit_validity", "unit_areas", "validity_from", "validity_to", "unit_id");
            migrationBuilder.CreateOverlapProtection("ex_room_areas_room_validity", "room_areas", "validity_from", "validity_to", "room_id");
            migrationBuilder.CreateOverlapProtection("ex_person_addresses_person_validity", "person_addresses", "validity_from", "validity_to", "person_id");
            migrationBuilder.CreateOverlapProtection("ex_leases_rental_unit_term", "leases", "term_from", "term_to", "rental_unit_id");
            migrationBuilder.CreateOverlapProtection("ex_rent_components_lease_kind_validity", "rent_components", "validity_from", "validity_to", "lease_id", "kind");
            migrationBuilder.CreateOverlapProtection("ex_occupant_counts_lease_validity", "occupant_counts", "validity_from", "validity_to", "lease_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropOverlapProtection("ex_property_areas_property_validity", "property_areas");
            migrationBuilder.DropOverlapProtection("ex_unit_areas_unit_validity", "unit_areas");
            migrationBuilder.DropOverlapProtection("ex_room_areas_room_validity", "room_areas");
            migrationBuilder.DropOverlapProtection("ex_person_addresses_person_validity", "person_addresses");
            migrationBuilder.DropOverlapProtection("ex_leases_rental_unit_term", "leases");
            migrationBuilder.DropOverlapProtection("ex_rent_components_lease_kind_validity", "rent_components");
            migrationBuilder.DropOverlapProtection("ex_occupant_counts_lease_validity", "occupant_counts");
            migrationBuilder.DropQuartzSchema();

            migrationBuilder.DropTable(
                name: "audit_entries");

            migrationBuilder.DropTable(
                name: "bank_details");

            migrationBuilder.DropTable(
                name: "bank_transactions");

            migrationBuilder.DropTable(
                name: "cost_items");

            migrationBuilder.DropTable(
                name: "cpi_values");

            migrationBuilder.DropTable(
                name: "csv_profiles");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropTable(
                name: "finance_settings");

            migrationBuilder.DropTable(
                name: "job_runs");

            migrationBuilder.DropTable(
                name: "landlord_profiles");

            migrationBuilder.DropTable(
                name: "lease_parties");

            migrationBuilder.DropTable(
                name: "magic_links");

            migrationBuilder.DropTable(
                name: "meter_readings");

            migrationBuilder.DropTable(
                name: "occupant_counts");

            migrationBuilder.DropTable(
                name: "outgoing_mails");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "person_addresses");

            migrationBuilder.DropTable(
                name: "property_areas");

            migrationBuilder.DropTable(
                name: "property_assignments");

            migrationBuilder.DropTable(
                name: "receipt_files");

            migrationBuilder.DropTable(
                name: "rent_components");

            migrationBuilder.DropTable(
                name: "rent_steps");

            migrationBuilder.DropTable(
                name: "role_claims");

            migrationBuilder.DropTable(
                name: "room_areas");

            migrationBuilder.DropTable(
                name: "unit_areas");

            migrationBuilder.DropTable(
                name: "user_claims");

            migrationBuilder.DropTable(
                name: "user_logins");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "bank_imports");

            migrationBuilder.DropTable(
                name: "meters");

            migrationBuilder.DropTable(
                name: "charges");

            migrationBuilder.DropTable(
                name: "receipts");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "bank_accounts");

            migrationBuilder.DropTable(
                name: "leases");

            migrationBuilder.DropTable(
                name: "people");

            migrationBuilder.DropTable(
                name: "rooms");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "units");

            migrationBuilder.DropTable(
                name: "properties");

            migrationBuilder.DropTable(
                name: "stored_files");
        }
    }
}
