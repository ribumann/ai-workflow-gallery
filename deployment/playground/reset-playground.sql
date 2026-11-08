/*
  Explicit, destructive SQL reset for the dedicated public Playground only.
  Replace the database placeholder and the confirmation literal after a separate review.
  Execute as a reviewed database administrator, not the application runtime identity.
  This leaves the sandbox actor, schema, public/curated collections, and Composer reservation
  ledger untouched. It removes catalog rows only; the isolated Playground blob container must
  be cleared separately using the reviewed storage account/container from deployment outputs.
  A serializable preflight rejects any cross-collection workflow/task/method or engagement
  reference in either direction before the first delete.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ExpectedDatabase sysname = N'<exact-approved-public-catalog-database>';
DECLARE @ConfirmPlaygroundOnly nvarchar(32) = N'<type RESET-PLAYGROUND to continue>';

IF DB_NAME() <> @ExpectedDatabase OR @ExpectedDatabase LIKE N'%<%'
    THROW 51000, N'Confirm the exact target database before resetting Playground.', 1;
IF @ConfirmPlaygroundOnly <> N'RESET-PLAYGROUND'
    THROW 51000, N'Explicit Playground-only reset confirmation is required.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.CatalogSchemaMarker
    WHERE Id = 1 AND Version = N'white-label-catalog-v4-playground-writes'
)
    THROW 51000, N'The target does not have the reviewed Playground schema version.', 1;

SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRY
BEGIN TRANSACTION;

IF EXISTS (
    SELECT 1
    FROM dbo.WorkflowSteps AS step
    JOIN dbo.Workflows AS workflow ON workflow.Id = step.WorkflowId
    WHERE (step.CollectionId = N'playground' AND workflow.CollectionId <> N'playground')
       OR (step.CollectionId <> N'playground' AND workflow.CollectionId = N'playground')
)
    THROW 51006, N'A cross-collection WorkflowStep-to-Workflow reference exists; no Playground rows were deleted.', 1;

IF EXISTS (
    SELECT 1
    FROM dbo.WorkflowSteps AS step
    JOIN dbo.Tasks AS task ON task.Id = step.TaskId
    WHERE (step.CollectionId = N'playground' AND task.CollectionId <> N'playground')
       OR (step.CollectionId <> N'playground' AND task.CollectionId = N'playground')
)
    THROW 51007, N'A cross-collection WorkflowStep-to-Task reference exists; no Playground rows were deleted.', 1;

IF EXISTS (
    SELECT 1
    FROM dbo.Tasks AS task
    JOIN dbo.AiMethods AS method ON method.Id = task.MethodId
    WHERE (task.CollectionId = N'playground' AND method.CollectionId <> N'playground')
       OR (task.CollectionId <> N'playground' AND method.CollectionId = N'playground')
) OR EXISTS (
    SELECT 1
    FROM dbo.Workflows AS workflow
    JOIN dbo.AiMethods AS method ON method.Id = workflow.MethodId
    WHERE (workflow.CollectionId = N'playground' AND method.CollectionId <> N'playground')
       OR (workflow.CollectionId <> N'playground' AND method.CollectionId = N'playground')
)
    THROW 51008, N'A cross-collection Task/Workflow-to-Method reference exists; no Playground rows were deleted.', 1;

IF EXISTS (
    SELECT 1
    FROM dbo.Comments AS engagement
    WHERE (engagement.CollectionId = N'playground' AND (
        (engagement.SubjectType = 0 AND EXISTS (
            SELECT 1 FROM dbo.Tasks AS target
            WHERE target.Id = engagement.SubjectId AND target.CollectionId <> N'playground'))
        OR (engagement.SubjectType = 1 AND EXISTS (
            SELECT 1 FROM dbo.Workflows AS target
            WHERE target.Id = engagement.SubjectId AND target.CollectionId <> N'playground'))
        OR (engagement.SubjectType = 2 AND EXISTS (
            SELECT 1 FROM dbo.AiMethods AS target
            WHERE target.Id = engagement.SubjectId AND target.CollectionId <> N'playground'))
    )) OR (engagement.CollectionId <> N'playground' AND (
        (engagement.SubjectType = 0 AND EXISTS (
            SELECT 1 FROM dbo.Tasks AS target
            WHERE target.Id = engagement.SubjectId AND target.CollectionId = N'playground'))
        OR (engagement.SubjectType = 1 AND EXISTS (
            SELECT 1 FROM dbo.Workflows AS target
            WHERE target.Id = engagement.SubjectId AND target.CollectionId = N'playground'))
        OR (engagement.SubjectType = 2 AND EXISTS (
            SELECT 1 FROM dbo.AiMethods AS target
            WHERE target.Id = engagement.SubjectId AND target.CollectionId = N'playground'))
    ))
)
    THROW 51009, N'A cross-collection Comment reference exists; no Playground rows were deleted.', 1;

IF EXISTS (
    SELECT 1
    FROM dbo.Ratings AS engagement
    WHERE (engagement.CollectionId = N'playground' AND (
        (engagement.SubjectType = 0 AND EXISTS (
            SELECT 1 FROM dbo.Tasks AS target
            WHERE target.Id = engagement.SubjectId AND target.CollectionId <> N'playground'))
        OR (engagement.SubjectType = 1 AND EXISTS (
            SELECT 1 FROM dbo.Workflows AS target
            WHERE target.Id = engagement.SubjectId AND target.CollectionId <> N'playground'))
        OR (engagement.SubjectType = 2 AND EXISTS (
            SELECT 1 FROM dbo.AiMethods AS target
            WHERE target.Id = engagement.SubjectId AND target.CollectionId <> N'playground'))
    )) OR (engagement.CollectionId <> N'playground' AND (
        (engagement.SubjectType = 0 AND EXISTS (
            SELECT 1 FROM dbo.Tasks AS target
            WHERE target.Id = engagement.SubjectId AND target.CollectionId = N'playground'))
        OR (engagement.SubjectType = 1 AND EXISTS (
            SELECT 1 FROM dbo.Workflows AS target
            WHERE target.Id = engagement.SubjectId AND target.CollectionId = N'playground'))
        OR (engagement.SubjectType = 2 AND EXISTS (
            SELECT 1 FROM dbo.AiMethods AS target
            WHERE target.Id = engagement.SubjectId AND target.CollectionId = N'playground'))
    ))
)
    THROW 51010, N'A cross-collection Rating reference exists; no Playground rows were deleted.', 1;

DELETE FROM dbo.Comments WHERE CollectionId = N'playground';
DELETE FROM dbo.Ratings WHERE CollectionId = N'playground';
DELETE FROM dbo.Workflows WHERE CollectionId = N'playground';
DELETE FROM dbo.Tasks WHERE CollectionId = N'playground';
DELETE FROM dbo.AiMethods WHERE CollectionId = N'playground';
DELETE FROM dbo.ReportLinks WHERE CollectionId = N'playground';

COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
    THROW;
END CATCH;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

SELECT N'Playground catalog rows removed; the dedicated Playground blob container still requires its separately reviewed reset.' AS ReviewNote;
