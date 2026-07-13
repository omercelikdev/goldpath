using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CorPay.Api.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GoldpathArchiveChainState",
                columns: table => new
                {
                    Definition = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    LastIndex = table.Column<long>(type: "bigint", nullable: false),
                    LastHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PurgedThroughIndex = table.Column<long>(type: "bigint", nullable: false),
                    PurgedHeadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoldpathArchiveChainState", x => x.Definition);
                });

            migrationBuilder.CreateTable(
                name: "GoldpathArchiveEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Definition = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AggregateKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Tenant = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Document = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ChainIndex = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChainHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreviousHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ErasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoldpathArchiveEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GoldpathAuditLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    User = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Tenant = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EntityType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EntityKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PropertyName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OldValue = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NewValue = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoldpathAuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GoldpathBulkBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Definition = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Tenant = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TotalRows = table.Column<int>(type: "integer", nullable: false),
                    ValidRows = table.Column<int>(type: "integer", nullable: false),
                    InvalidRows = table.Column<int>(type: "integer", nullable: false),
                    ExecutedRows = table.Column<int>(type: "integer", nullable: false),
                    FailedRows = table.Column<int>(type: "integer", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TraceParent = table.Column<string>(type: "character varying(55)", maxLength: 55, nullable: true),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoldpathBulkBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GoldpathBulkFileChunks",
                columns: table => new
                {
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Index = table.Column<int>(type: "integer", nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoldpathBulkFileChunks", x => new { x.FileId, x.Index });
                });

            migrationBuilder.CreateTable(
                name: "GoldpathBulkFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoldpathBulkFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GoldpathBulkRowErrors",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    Field = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Message = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoldpathBulkRowErrors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GoldpathBulkRows",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoldpathBulkRows", x => new { x.BatchId, x.RowNumber });
                });

            migrationBuilder.CreateTable(
                name: "GoldpathErasureRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubjectKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EntriesAffected = table.Column<int>(type: "integer", nullable: false),
                    Detail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoldpathErasureRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GoldpathJobAdminAudit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Fleet = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Target = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Detail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoldpathJobAdminAudit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GoldpathJobExecutions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchedulerName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    JobName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    InstanceName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Error = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoldpathJobExecutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GoldpathJobItemFailures",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    ItemKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RedrivenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoldpathJobItemFailures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GoldpathJobRunChunks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Index = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ClaimedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FireInstanceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoldpathJobRunChunks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GoldpathJobRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchedulerName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    JobName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeadlineAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PredictedFinishAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TotalChunks = table.Column<int>(type: "integer", nullable: false),
                    CompletedChunks = table.Column<int>(type: "integer", nullable: false),
                    FailedChunks = table.Column<int>(type: "integer", nullable: false),
                    TotalItems = table.Column<long>(type: "bigint", nullable: true),
                    ItemFailures = table.Column<int>(type: "integer", nullable: false),
                    StartedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    InputVersion = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Executions = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoldpathJobRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GoldpathLegalHolds",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Definition = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AggregateKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CaseReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PlacedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PlacedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LiftedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LiftedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoldpathLegalHolds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InboxState",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                    Received = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceiveCount = table.Column<int>(type: "integer", nullable: false),
                    ExpirationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Consumed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Delivered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxState", x => x.Id);
                    table.UniqueConstraint("AK_InboxState_MessageId_ConsumerId", x => new { x.MessageId, x.ConsumerId });
                });

            migrationBuilder.CreateTable(
                name: "MediantAuditEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RequestType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RequestData = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ResponseData = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationMs = table.Column<double>(type: "double precision", nullable: false),
                    IsSuccess = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ExceptionType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ActionName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EntityType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EntityId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Metadata = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediantAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CustomerNationalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxState",
                columns: table => new
                {
                    OutboxId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Delivered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxState", x => x.OutboxId);
                });

            migrationBuilder.CreateTable(
                name: "qrtz_calendars",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    calendar_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    calendar = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qrtz_calendars", x => new { x.sched_name, x.calendar_name });
                });

            migrationBuilder.CreateTable(
                name: "qrtz_fired_triggers",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    entry_id = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    trigger_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    trigger_group = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    instance_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    fired_time = table.Column<long>(type: "bigint", nullable: false),
                    sched_time = table.Column<long>(type: "bigint", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    job_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    job_group = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    is_nonconcurrent = table.Column<bool>(type: "boolean", nullable: false),
                    requests_recovery = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qrtz_fired_triggers", x => new { x.sched_name, x.entry_id });
                });

            migrationBuilder.CreateTable(
                name: "qrtz_job_details",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    job_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    job_group = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    job_class_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    is_durable = table.Column<bool>(type: "boolean", nullable: false),
                    is_nonconcurrent = table.Column<bool>(type: "boolean", nullable: false),
                    is_update_data = table.Column<bool>(type: "boolean", nullable: false),
                    requests_recovery = table.Column<bool>(type: "boolean", nullable: false),
                    job_data = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qrtz_job_details", x => new { x.sched_name, x.job_name, x.job_group });
                });

            migrationBuilder.CreateTable(
                name: "qrtz_locks",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    lock_name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qrtz_locks", x => new { x.sched_name, x.lock_name });
                });

            migrationBuilder.CreateTable(
                name: "qrtz_paused_trigger_grps",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    trigger_group = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qrtz_paused_trigger_grps", x => new { x.sched_name, x.trigger_group });
                });

            migrationBuilder.CreateTable(
                name: "qrtz_scheduler_state",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    instance_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    last_checkin_time = table.Column<long>(type: "bigint", nullable: false),
                    checkin_interval = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qrtz_scheduler_state", x => new { x.sched_name, x.instance_name });
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessage",
                columns: table => new
                {
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EnqueueTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Headers = table.Column<string>(type: "text", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    InboxMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    InboxConsumerId = table.Column<Guid>(type: "uuid", nullable: true),
                    OutboxId = table.Column<Guid>(type: "uuid", nullable: true),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MessageType = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    InitiatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DestinationAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ResponseAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FaultAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ExpirationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessage", x => x.SequenceNumber);
                    table.ForeignKey(
                        name: "FK_OutboxMessage_InboxState_InboxMessageId_InboxConsumerId",
                        columns: x => new { x.InboxMessageId, x.InboxConsumerId },
                        principalTable: "InboxState",
                        principalColumns: new[] { "MessageId", "ConsumerId" });
                    table.ForeignKey(
                        name: "FK_OutboxMessage_OutboxState_OutboxId",
                        column: x => x.OutboxId,
                        principalTable: "OutboxState",
                        principalColumn: "OutboxId");
                });

            migrationBuilder.CreateTable(
                name: "qrtz_triggers",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    trigger_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    trigger_group = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    job_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    job_group = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    next_fire_time = table.Column<long>(type: "bigint", nullable: true),
                    prev_fire_time = table.Column<long>(type: "bigint", nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: true),
                    trigger_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    trigger_type = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    start_time = table.Column<long>(type: "bigint", nullable: false),
                    end_time = table.Column<long>(type: "bigint", nullable: true),
                    calendar_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    misfire_instr = table.Column<short>(type: "smallint", nullable: true),
                    job_data = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qrtz_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                    table.ForeignKey(
                        name: "FK_qrtz_triggers_qrtz_job_details_sched_name_job_name_job_group",
                        columns: x => new { x.sched_name, x.job_name, x.job_group },
                        principalTable: "qrtz_job_details",
                        principalColumns: new[] { "sched_name", "job_name", "job_group" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "qrtz_blob_triggers",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    trigger_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    trigger_group = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    blob_data = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qrtz_blob_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                    table.ForeignKey(
                        name: "FK_qrtz_blob_triggers_qrtz_triggers_sched_name_trigger_name_tr~",
                        columns: x => new { x.sched_name, x.trigger_name, x.trigger_group },
                        principalTable: "qrtz_triggers",
                        principalColumns: new[] { "sched_name", "trigger_name", "trigger_group" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "qrtz_cron_triggers",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    trigger_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    trigger_group = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    cron_expression = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qrtz_cron_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                    table.ForeignKey(
                        name: "FK_qrtz_cron_triggers_qrtz_triggers_sched_name_trigger_name_tr~",
                        columns: x => new { x.sched_name, x.trigger_name, x.trigger_group },
                        principalTable: "qrtz_triggers",
                        principalColumns: new[] { "sched_name", "trigger_name", "trigger_group" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "qrtz_simple_triggers",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    trigger_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    trigger_group = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    repeat_count = table.Column<long>(type: "bigint", nullable: false),
                    repeat_interval = table.Column<long>(type: "bigint", nullable: false),
                    times_triggered = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qrtz_simple_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                    table.ForeignKey(
                        name: "FK_qrtz_simple_triggers_qrtz_triggers_sched_name_trigger_name_~",
                        columns: x => new { x.sched_name, x.trigger_name, x.trigger_group },
                        principalTable: "qrtz_triggers",
                        principalColumns: new[] { "sched_name", "trigger_name", "trigger_group" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "qrtz_simprop_triggers",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    trigger_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    trigger_group = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    str_prop_1 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    str_prop_2 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    str_prop_3 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    int_prop_1 = table.Column<int>(type: "integer", nullable: true),
                    int_prop_2 = table.Column<int>(type: "integer", nullable: true),
                    long_prop_1 = table.Column<long>(type: "bigint", nullable: true),
                    long_prop_2 = table.Column<long>(type: "bigint", nullable: true),
                    dec_prop_1 = table.Column<decimal>(type: "numeric(13,4)", precision: 13, scale: 4, nullable: true),
                    dec_prop_2 = table.Column<decimal>(type: "numeric(13,4)", precision: 13, scale: 4, nullable: true),
                    bool_prop_1 = table.Column<bool>(type: "boolean", nullable: true),
                    bool_prop_2 = table.Column<bool>(type: "boolean", nullable: true),
                    time_zone_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qrtz_simprop_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                    table.ForeignKey(
                        name: "FK_qrtz_simprop_triggers_qrtz_triggers_sched_name_trigger_name~",
                        columns: x => new { x.sched_name, x.trigger_name, x.trigger_group },
                        principalTable: "qrtz_triggers",
                        principalColumns: new[] { "sched_name", "trigger_name", "trigger_group" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GoldpathArchiveEntries_Definition_AggregateKey",
                table: "GoldpathArchiveEntries",
                columns: new[] { "Definition", "AggregateKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoldpathArchiveEntries_Definition_ArchivedAt",
                table: "GoldpathArchiveEntries",
                columns: new[] { "Definition", "ArchivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GoldpathArchiveEntries_Definition_ChainIndex",
                table: "GoldpathArchiveEntries",
                columns: new[] { "Definition", "ChainIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoldpathAuditLog_EntityType_EntityKey",
                table: "GoldpathAuditLog",
                columns: new[] { "EntityType", "EntityKey" });

            migrationBuilder.CreateIndex(
                name: "IX_GoldpathAuditLog_Timestamp",
                table: "GoldpathAuditLog",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_GoldpathBulkBatches_Definition_State",
                table: "GoldpathBulkBatches",
                columns: new[] { "Definition", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_GoldpathBulkBatches_FileId",
                table: "GoldpathBulkBatches",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_GoldpathBulkFiles_Sha256",
                table: "GoldpathBulkFiles",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoldpathBulkRowErrors_BatchId_RowNumber",
                table: "GoldpathBulkRowErrors",
                columns: new[] { "BatchId", "RowNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_GoldpathErasureRecords_SubjectKey",
                table: "GoldpathErasureRecords",
                column: "SubjectKey");

            migrationBuilder.CreateIndex(
                name: "IX_GoldpathJobAdminAudit_At",
                table: "GoldpathJobAdminAudit",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_GoldpathJobExecutions_FiredAt",
                table: "GoldpathJobExecutions",
                column: "FiredAt");

            migrationBuilder.CreateIndex(
                name: "IX_GoldpathJobExecutions_SchedulerName_JobName",
                table: "GoldpathJobExecutions",
                columns: new[] { "SchedulerName", "JobName" });

            migrationBuilder.CreateIndex(
                name: "IX_GoldpathJobItemFailures_RunId",
                table: "GoldpathJobItemFailures",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_GoldpathJobRunChunks_RunId_Index",
                table: "GoldpathJobRunChunks",
                columns: new[] { "RunId", "Index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoldpathJobRunChunks_RunId_Status",
                table: "GoldpathJobRunChunks",
                columns: new[] { "RunId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_GoldpathJobRuns_SchedulerName_JobName_Status",
                table: "GoldpathJobRuns",
                columns: new[] { "SchedulerName", "JobName", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_GoldpathJobRuns_StartedAt",
                table: "GoldpathJobRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_GoldpathLegalHolds_Definition_AggregateKey_LiftedAt",
                table: "GoldpathLegalHolds",
                columns: new[] { "Definition", "AggregateKey", "LiftedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxState_Delivered",
                table: "InboxState",
                column: "Delivered");

            migrationBuilder.CreateIndex(
                name: "IX_MediantAuditEntries_CorrelationId",
                table: "MediantAuditEntries",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_MediantAuditEntries_Timestamp",
                table: "MediantAuditEntries",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_EnqueueTime",
                table: "OutboxMessage",
                column: "EnqueueTime");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_ExpirationTime",
                table: "OutboxMessage",
                column: "ExpirationTime");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber",
                table: "OutboxMessage",
                columns: new[] { "InboxMessageId", "InboxConsumerId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_OutboxId_SequenceNumber",
                table: "OutboxMessage",
                columns: new[] { "OutboxId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxState_Created",
                table: "OutboxState",
                column: "Created");

            migrationBuilder.CreateIndex(
                name: "idx_qrtz_ft_job_group",
                table: "qrtz_fired_triggers",
                column: "job_group");

            migrationBuilder.CreateIndex(
                name: "idx_qrtz_ft_job_name",
                table: "qrtz_fired_triggers",
                column: "job_name");

            migrationBuilder.CreateIndex(
                name: "idx_qrtz_ft_job_req_recovery",
                table: "qrtz_fired_triggers",
                column: "requests_recovery");

            migrationBuilder.CreateIndex(
                name: "idx_qrtz_ft_trig_group",
                table: "qrtz_fired_triggers",
                column: "trigger_group");

            migrationBuilder.CreateIndex(
                name: "idx_qrtz_ft_trig_inst_name",
                table: "qrtz_fired_triggers",
                column: "instance_name");

            migrationBuilder.CreateIndex(
                name: "idx_qrtz_ft_trig_name",
                table: "qrtz_fired_triggers",
                column: "trigger_name");

            migrationBuilder.CreateIndex(
                name: "idx_qrtz_ft_trig_nm_gp",
                table: "qrtz_fired_triggers",
                columns: new[] { "trigger_name", "trigger_group" });

            migrationBuilder.CreateIndex(
                name: "idx_qrtz_j_req_recovery",
                table: "qrtz_job_details",
                column: "requests_recovery");

            migrationBuilder.CreateIndex(
                name: "idx_qrtz_t_next_fire_time",
                table: "qrtz_triggers",
                column: "next_fire_time");

            migrationBuilder.CreateIndex(
                name: "idx_qrtz_t_nft_st",
                table: "qrtz_triggers",
                columns: new[] { "next_fire_time", "trigger_state" });

            migrationBuilder.CreateIndex(
                name: "idx_qrtz_t_state",
                table: "qrtz_triggers",
                column: "trigger_state");

            migrationBuilder.CreateIndex(
                name: "IX_qrtz_triggers_sched_name_job_name_job_group",
                table: "qrtz_triggers",
                columns: new[] { "sched_name", "job_name", "job_group" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoldpathArchiveChainState");

            migrationBuilder.DropTable(
                name: "GoldpathArchiveEntries");

            migrationBuilder.DropTable(
                name: "GoldpathAuditLog");

            migrationBuilder.DropTable(
                name: "GoldpathBulkBatches");

            migrationBuilder.DropTable(
                name: "GoldpathBulkFileChunks");

            migrationBuilder.DropTable(
                name: "GoldpathBulkFiles");

            migrationBuilder.DropTable(
                name: "GoldpathBulkRowErrors");

            migrationBuilder.DropTable(
                name: "GoldpathBulkRows");

            migrationBuilder.DropTable(
                name: "GoldpathErasureRecords");

            migrationBuilder.DropTable(
                name: "GoldpathJobAdminAudit");

            migrationBuilder.DropTable(
                name: "GoldpathJobExecutions");

            migrationBuilder.DropTable(
                name: "GoldpathJobItemFailures");

            migrationBuilder.DropTable(
                name: "GoldpathJobRunChunks");

            migrationBuilder.DropTable(
                name: "GoldpathJobRuns");

            migrationBuilder.DropTable(
                name: "GoldpathLegalHolds");

            migrationBuilder.DropTable(
                name: "MediantAuditEntries");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "OutboxMessage");

            migrationBuilder.DropTable(
                name: "qrtz_blob_triggers");

            migrationBuilder.DropTable(
                name: "qrtz_calendars");

            migrationBuilder.DropTable(
                name: "qrtz_cron_triggers");

            migrationBuilder.DropTable(
                name: "qrtz_fired_triggers");

            migrationBuilder.DropTable(
                name: "qrtz_locks");

            migrationBuilder.DropTable(
                name: "qrtz_paused_trigger_grps");

            migrationBuilder.DropTable(
                name: "qrtz_scheduler_state");

            migrationBuilder.DropTable(
                name: "qrtz_simple_triggers");

            migrationBuilder.DropTable(
                name: "qrtz_simprop_triggers");

            migrationBuilder.DropTable(
                name: "InboxState");

            migrationBuilder.DropTable(
                name: "OutboxState");

            migrationBuilder.DropTable(
                name: "qrtz_triggers");

            migrationBuilder.DropTable(
                name: "qrtz_job_details");
        }
    }
}
