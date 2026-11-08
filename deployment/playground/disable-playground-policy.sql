/*
  Disable only the direct public Playground write surface.
  Replace placeholders and verify the database exactly as for apply-playground-policy.sql.
  First set PublicPlaygroundWritesEnabled=false and remove the Playground storage writer role.
  This script retains the additive schema, sandbox actor, and Playground content so rollback is
  non-destructive. It does not change Composer reservations or public/curated catalog rows.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ExpectedDatabase sysname = N'<exact-approved-public-catalog-database>';
DECLARE @RuntimeDatabaseUser sysname = N'<exact-contained-runtime-user>';

IF DB_NAME() <> @ExpectedDatabase OR @ExpectedDatabase LIKE N'%<%'
    THROW 51000, N'Confirm the exact target database before disabling Playground writes.', 1;
IF @RuntimeDatabaseUser LIKE N'%<%' OR USER_ID(@RuntimeDatabaseUser) IS NULL
    THROW 51000, N'Confirm the existing contained runtime principal before revoking grants.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.CatalogSchemaMarker
    WHERE Id = 1 AND Version = N'white-label-catalog-v4-playground-writes'
)
    THROW 51000, N'The target does not have the reviewed Playground schema version.', 1;

BEGIN TRANSACTION;

DECLARE @table sysname;
DECLARE @tables TABLE (Name sysname PRIMARY KEY);
INSERT @tables(Name) VALUES
    (N'Tasks'), (N'Workflows'), (N'WorkflowSteps'), (N'AiMethods'),
    (N'ReportLinks'), (N'Comments'), (N'Ratings');

DECLARE tableCursor CURSOR LOCAL FAST_FORWARD FOR SELECT Name FROM @tables;
OPEN tableCursor;
FETCH NEXT FROM tableCursor INTO @table;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF OBJECT_ID(N'dbo.TR_PlaygroundGuard_' + @table, N'TR') IS NOT NULL
        EXEC(N'DROP TRIGGER dbo.' + QUOTENAME(N'TR_PlaygroundGuard_' + @table));
    IF OBJECT_ID(N'dbo.TR_PlaygroundReference_' + @table, N'TR') IS NOT NULL
        EXEC(N'DROP TRIGGER dbo.' + QUOTENAME(N'TR_PlaygroundReference_' + @table));

    DECLARE @revokeSql nvarchar(max) = N'REVOKE INSERT, UPDATE, DELETE ON OBJECT::dbo.' +
        QUOTENAME(@table) + N' FROM ' + QUOTENAME(@RuntimeDatabaseUser) + N';';
    EXEC sys.sp_executesql @revokeSql;
    FETCH NEXT FROM tableCursor INTO @table;
END;
CLOSE tableCursor;
DEALLOCATE tableCursor;

IF OBJECT_ID(N'dbo.AssertPlaygroundBounds', N'P') IS NOT NULL
BEGIN
    DECLARE @revokeExecuteSql nvarchar(max) = N'REVOKE EXECUTE ON OBJECT::dbo.AssertPlaygroundBounds FROM ' +
        QUOTENAME(@RuntimeDatabaseUser) + N';';
    EXEC sys.sp_executesql @revokeExecuteSql;
    DROP PROCEDURE dbo.AssertPlaygroundBounds;
END;

COMMIT TRANSACTION;

SELECT DB_NAME() AS DatabaseName,
       N'Playground direct grants and database triggers removed. Preserve or export data before separately approving any deletion.' AS ReviewNote;
