using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisputePortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillDisputeReviewers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Disputes already picked up before reviewers were recorded: the agent who started the review
            // becomes the reviewer, so those disputes can still be closed.
            migrationBuilder.Sql("""
                UPDATE "Disputes"
                SET "ReviewerId" = (SELECT h."ActorUserId" FROM "DisputeHistory" h
                                    WHERE h."DisputeId" = "Disputes"."Id" AND h."Status" = 'UnderReview'
                                    ORDER BY h."OccurredAtUtc" DESC LIMIT 1),
                    "ReviewerName" = (SELECT h."ActorName" FROM "DisputeHistory" h
                                      WHERE h."DisputeId" = "Disputes"."Id" AND h."Status" = 'UnderReview'
                                      ORDER BY h."OccurredAtUtc" DESC LIMIT 1)
                WHERE "ReviewerId" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the previous migration's Down drops the columns.
        }
    }
}
