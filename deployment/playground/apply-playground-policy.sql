/*
  Reviewed SQL Server change for the anonymous, disposable Playground.

  Run only after replacing both placeholders, connecting to the independently verified public
  catalog database through its   private endpoint as a reviewed database administrator. This is not run by the deployment
  helper or application startup. It accepts either a freshly initialized v4 catalog schema
  or migrates the reviewed v2 schema; it does not initialize, import, reset or publish data.

  The caller must separately review the exact runtime SQL principal and storage-container role.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ExpectedDatabase sysname = N'<exact-approved-public-catalog-database>';
DECLARE @RuntimeDatabaseUser sysname = N'<exact-contained-runtime-user>';
DECLARE @ActorId uniqueidentifier = 'a4c02c98-a4d3-4f4f-9f93-bdb6b8ebd9b8';
DECLARE @ExpectedVersion nvarchar(100) = N'white-label-catalog-v2-public-composer';
DECLARE @NewVersion nvarchar(100) = N'white-label-catalog-v4-playground-writes';
DECLARE @SourceVersion nvarchar(100);

IF DB_NAME() <> @ExpectedDatabase OR @ExpectedDatabase LIKE N'%<%'
    THROW 51000, N'Confirm the exact target database before applying this change.', 1;
IF @RuntimeDatabaseUser LIKE N'%<%' OR USER_ID(@RuntimeDatabaseUser) IS NULL
    THROW 51000, N'Confirm the existing contained runtime principal before applying grants.', 1;
IF IS_ROLEMEMBER(N'db_owner', @RuntimeDatabaseUser) = 1 OR
   IS_ROLEMEMBER(N'db_datawriter', @RuntimeDatabaseUser) = 1
    THROW 51000, N'The runtime principal must not be a database owner or blanket data writer.', 1;
BEGIN TRANSACTION;

SELECT @SourceVersion = Version
FROM dbo.CatalogSchemaMarker WITH (UPDLOCK, HOLDLOCK)
WHERE Id = 1;
IF @SourceVersion IS NULL OR @SourceVersion NOT IN (@ExpectedVersion, @NewVersion)
    THROW 51000, N'The catalog schema is not the reviewed v2 or v4 source; no schema was changed.', 1;
IF OBJECT_ID(N'dbo.AssertPlaygroundBounds', N'P') IS NOT NULL OR EXISTS (
    SELECT 1 FROM sys.triggers
    WHERE name IN (
        N'TR_PlaygroundGuard_Tasks', N'TR_PlaygroundGuard_Workflows',
        N'TR_PlaygroundGuard_WorkflowSteps', N'TR_PlaygroundGuard_AiMethods',
        N'TR_PlaygroundGuard_ReportLinks', N'TR_PlaygroundGuard_Comments',
        N'TR_PlaygroundGuard_Ratings', N'TR_PlaygroundReference_Tasks',
        N'TR_PlaygroundReference_Workflows', N'TR_PlaygroundReference_WorkflowSteps',
        N'TR_PlaygroundReference_Comments', N'TR_PlaygroundReference_Ratings'
    )
)
    THROW 51000, N'Playground policy objects already exist or are partially installed; manual review is required.', 1;

IF COL_LENGTH(N'dbo.Users', N'IsSandboxActor') IS NULL
    ALTER TABLE dbo.Users ADD IsSandboxActor bit NOT NULL
        CONSTRAINT DF_Users_IsSandboxActor DEFAULT (0) WITH VALUES;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'IX_Users_EntraTenantId_EntraObjectId')
    DROP INDEX IX_Users_EntraTenantId_EntraObjectId ON dbo.Users;
IF COL_LENGTH(N'dbo.Users', N'EntraObjectId') IS NOT NULL
    ALTER TABLE dbo.Users ALTER COLUMN EntraObjectId nvarchar(100) NULL;
CREATE UNIQUE INDEX IX_Users_EntraTenantId_EntraObjectId
    ON dbo.Users(EntraTenantId, EntraObjectId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'IX_Users_IsSandboxActor')
    CREATE UNIQUE INDEX IX_Users_IsSandboxActor ON dbo.Users(IsSandboxActor) WHERE IsSandboxActor = 1;

IF COL_LENGTH(N'dbo.WorkflowSteps', N'CollectionId') IS NULL
    ALTER TABLE dbo.WorkflowSteps ADD CollectionId nvarchar(64) NULL;
UPDATE step
SET CollectionId = workflow.CollectionId
FROM dbo.WorkflowSteps AS step
JOIN dbo.Workflows AS workflow ON workflow.Id = step.WorkflowId
WHERE step.CollectionId IS NULL;
IF EXISTS (SELECT 1 FROM dbo.WorkflowSteps WHERE CollectionId IS NULL)
    THROW 51000, N'Workflow steps without a parent workflow require manual review; no scope was guessed.', 1;
ALTER TABLE dbo.WorkflowSteps ALTER COLUMN CollectionId nvarchar(64) NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WorkflowSteps') AND name = N'IX_WorkflowSteps_CollectionId')
    CREATE INDEX IX_WorkflowSteps_CollectionId ON dbo.WorkflowSteps(CollectionId);

IF COL_LENGTH(N'dbo.Comments', N'CollectionId') IS NULL
    ALTER TABLE dbo.Comments ADD CollectionId nvarchar(64) NULL;
IF COL_LENGTH(N'dbo.Ratings', N'CollectionId') IS NULL
    ALTER TABLE dbo.Ratings ADD CollectionId nvarchar(64) NULL;

UPDATE comment
SET CollectionId = CASE comment.SubjectType
    WHEN 0 THEN task.CollectionId
    WHEN 1 THEN workflow.CollectionId
    WHEN 2 THEN method.CollectionId
END
FROM dbo.Comments AS comment
LEFT JOIN dbo.Tasks AS task ON comment.SubjectType = 0 AND task.Id = comment.SubjectId
LEFT JOIN dbo.Workflows AS workflow ON comment.SubjectType = 1 AND workflow.Id = comment.SubjectId
LEFT JOIN dbo.AiMethods AS method ON comment.SubjectType = 2 AND method.Id = comment.SubjectId
WHERE comment.CollectionId IS NULL;
UPDATE rating
SET CollectionId = CASE rating.SubjectType
    WHEN 0 THEN task.CollectionId
    WHEN 1 THEN workflow.CollectionId
    WHEN 2 THEN method.CollectionId
END
FROM dbo.Ratings AS rating
LEFT JOIN dbo.Tasks AS task ON rating.SubjectType = 0 AND task.Id = rating.SubjectId
LEFT JOIN dbo.Workflows AS workflow ON rating.SubjectType = 1 AND workflow.Id = rating.SubjectId
LEFT JOIN dbo.AiMethods AS method ON rating.SubjectType = 2 AND method.Id = rating.SubjectId
WHERE rating.CollectionId IS NULL;
IF EXISTS (SELECT 1 FROM dbo.Comments WHERE CollectionId IS NULL) OR
   EXISTS (SELECT 1 FROM dbo.Ratings WHERE CollectionId IS NULL)
    THROW 51000, N'Orphaned engagement rows require manual classification; no collection was guessed.', 1;
ALTER TABLE dbo.Comments ALTER COLUMN CollectionId nvarchar(64) NOT NULL;
ALTER TABLE dbo.Ratings ALTER COLUMN CollectionId nvarchar(64) NOT NULL;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Comments') AND name = N'IX_Comments_SubjectType_SubjectId')
    DROP INDEX IX_Comments_SubjectType_SubjectId ON dbo.Comments;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Comments') AND name = N'IX_Comments_CollectionId_SubjectType_SubjectId')
    CREATE INDEX IX_Comments_CollectionId_SubjectType_SubjectId
        ON dbo.Comments(CollectionId, SubjectType, SubjectId);
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Ratings') AND name = N'IX_Ratings_SubjectType_SubjectId_DeviceId')
    DROP INDEX IX_Ratings_SubjectType_SubjectId_DeviceId ON dbo.Ratings;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Ratings') AND name = N'IX_Ratings_CollectionId_SubjectType_SubjectId_DeviceId')
    CREATE UNIQUE INDEX IX_Ratings_CollectionId_SubjectType_SubjectId_DeviceId
        ON dbo.Ratings(CollectionId, SubjectType, SubjectId, DeviceId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Ratings') AND name = N'IX_Ratings_CollectionId_SubjectType_SubjectId')
    CREATE INDEX IX_Ratings_CollectionId_SubjectType_SubjectId
        ON dbo.Ratings(CollectionId, SubjectType, SubjectId);

IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @ActorId)
    INSERT dbo.Users(Id, EntraTenantId, EntraObjectId, DisplayName, IsSandboxActor)
    VALUES (@ActorId, NULL, NULL, N'Playground contributor', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @ActorId AND IsSandboxActor = 1
               AND EntraTenantId IS NULL AND EntraObjectId IS NULL)
    THROW 51000, N'The reserved Playground actor ID is occupied by a non-sandbox identity.', 1;
IF EXISTS (SELECT 1 FROM dbo.Users WHERE IsSandboxActor = 1 AND Id <> @ActorId)
    THROW 51000, N'A different sandbox actor already exists and requires manual review.', 1;

EXEC(N'
CREATE OR ALTER PROCEDURE dbo.AssertPlaygroundBounds
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock
        @Resource = N''Gallery.Playground.Admission'',
        @LockMode = N''Exclusive'',
        @LockOwner = N''Transaction'',
        @LockTimeout = 10000;
    IF @lockResult < 0
        THROW 51002, N''Playground admission is busy; retry after the current write completes.'', 1;

    DECLARE @rows bigint =
        (SELECT COUNT_BIG(*) FROM dbo.Tasks WHERE CollectionId = N''playground'') +
        (SELECT COUNT_BIG(*) FROM dbo.Workflows WHERE CollectionId = N''playground'') +
        (SELECT COUNT_BIG(*) FROM dbo.WorkflowSteps WHERE CollectionId = N''playground'') +
        (SELECT COUNT_BIG(*) FROM dbo.AiMethods WHERE CollectionId = N''playground'') +
        (SELECT COUNT_BIG(*) FROM dbo.ReportLinks WHERE CollectionId = N''playground'') +
        (SELECT COUNT_BIG(*) FROM dbo.Comments WHERE CollectionId = N''playground'') +
        (SELECT COUNT_BIG(*) FROM dbo.Ratings WHERE CollectionId = N''playground'');
    IF @rows > 5000
        THROW 51003, N''The Playground catalog row limit has been reached.'', 1;

    DECLARE @fileBytes bigint =
        (SELECT COALESCE(SUM(CONVERT(bigint, AttachedFileSizeBytes)), 0)
         FROM dbo.Tasks WHERE CollectionId = N''playground'');
    IF @fileBytes > 536870912
        THROW 51004, N''The Playground attachment storage limit has been reached.'', 1;
END');

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
    DECLARE @actorGuard nvarchar(1000) = N'';
    IF @table IN (N'Tasks', N'Workflows', N'AiMethods', N'ReportLinks')
        SET @actorGuard = N' OR CreatedByUserId <> ''' + CONVERT(nvarchar(36), @ActorId) +
            N''' OR LastEditedByUserId <> ''' + CONVERT(nvarchar(36), @ActorId) +
            N''' OR IsPublished <> 0';
    ELSE IF @table = N'Comments'
        SET @actorGuard = N' OR CreatedByUserId <> ''' + CONVERT(nvarchar(36), @ActorId) + N'''';

    DECLARE @triggerSql nvarchar(max) =
        N'CREATE OR ALTER TRIGGER dbo.' + QUOTENAME(N'TR_PlaygroundGuard_' + @table) +
        N' ON dbo.' + QUOTENAME(@table) + N' AFTER INSERT, UPDATE, DELETE AS
        BEGIN
            SET NOCOUNT ON;
            IF IS_MEMBER(N''db_owner'') = 1 RETURN;
            IF EXISTS (SELECT 1 FROM inserted WHERE CollectionId IS NULL OR CollectionId <> N''playground''' +
                @actorGuard + N') OR EXISTS (SELECT 1 FROM deleted WHERE CollectionId IS NULL OR CollectionId <> N''playground'')
                THROW 51001, N''Only Playground rows and the reserved sandbox actor may be changed by the runtime.'', 1;
            IF EXISTS (SELECT 1 FROM inserted WHERE CollectionId = N''playground'')
                EXEC dbo.AssertPlaygroundBounds;
        END';
    EXEC sys.sp_executesql @triggerSql;
    FETCH NEXT FROM tableCursor INTO @table;
END;
CLOSE tableCursor;
DEALLOCATE tableCursor;

EXEC(N'
CREATE OR ALTER TRIGGER dbo.TR_PlaygroundReference_Tasks ON dbo.Tasks AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF IS_MEMBER(N''db_owner'') = 1 RETURN;
    IF EXISTS (SELECT 1 FROM inserted AS i WHERE i.CollectionId = N''playground'' AND i.MethodId IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM dbo.AiMethods AS m WHERE m.Id = i.MethodId AND m.CollectionId = N''playground''))
        THROW 51005, N''A Playground task must reference a method from Playground.'', 1;
END');
EXEC(N'
CREATE OR ALTER TRIGGER dbo.TR_PlaygroundReference_Workflows ON dbo.Workflows AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF IS_MEMBER(N''db_owner'') = 1 RETURN;
    IF EXISTS (SELECT 1 FROM inserted AS i WHERE i.CollectionId = N''playground'' AND i.MethodId IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM dbo.AiMethods AS m WHERE m.Id = i.MethodId AND m.CollectionId = N''playground''))
        THROW 51005, N''A Playground workflow must reference a method from Playground.'', 1;
END');
EXEC(N'
CREATE OR ALTER TRIGGER dbo.TR_PlaygroundReference_WorkflowSteps ON dbo.WorkflowSteps AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF IS_MEMBER(N''db_owner'') = 1 RETURN;
    IF EXISTS (SELECT 1 FROM inserted AS i WHERE i.CollectionId = N''playground'' AND
        (NOT EXISTS (SELECT 1 FROM dbo.Workflows AS w WHERE w.Id = i.WorkflowId AND w.CollectionId = N''playground'')
         OR (i.TaskId IS NOT NULL AND NOT EXISTS
            (SELECT 1 FROM dbo.Tasks AS t WHERE t.Id = i.TaskId AND t.CollectionId = N''playground''))))
        THROW 51005, N''Every Playground workflow step must reference Playground resources.'', 1;
END');
EXEC(N'
CREATE OR ALTER TRIGGER dbo.TR_PlaygroundReference_Comments ON dbo.Comments AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF IS_MEMBER(N''db_owner'') = 1 RETURN;
    IF EXISTS (SELECT 1 FROM inserted AS i WHERE i.CollectionId = N''playground'' AND NOT (
        (i.SubjectType = 0 AND EXISTS (SELECT 1 FROM dbo.Tasks t WHERE t.Id = i.SubjectId AND t.CollectionId = N''playground'')) OR
        (i.SubjectType = 1 AND EXISTS (SELECT 1 FROM dbo.Workflows w WHERE w.Id = i.SubjectId AND w.CollectionId = N''playground'')) OR
        (i.SubjectType = 2 AND EXISTS (SELECT 1 FROM dbo.AiMethods m WHERE m.Id = i.SubjectId AND m.CollectionId = N''playground''))))
        THROW 51005, N''A Playground comment must reference a Playground catalog item.'', 1;
END');
EXEC(N'
CREATE OR ALTER TRIGGER dbo.TR_PlaygroundReference_Ratings ON dbo.Ratings AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF IS_MEMBER(N''db_owner'') = 1 RETURN;
    IF EXISTS (SELECT 1 FROM inserted AS i WHERE i.CollectionId = N''playground'' AND NOT (
        (i.SubjectType = 0 AND EXISTS (SELECT 1 FROM dbo.Tasks t WHERE t.Id = i.SubjectId AND t.CollectionId = N''playground'')) OR
        (i.SubjectType = 1 AND EXISTS (SELECT 1 FROM dbo.Workflows w WHERE w.Id = i.SubjectId AND w.CollectionId = N''playground'')) OR
        (i.SubjectType = 2 AND EXISTS (SELECT 1 FROM dbo.AiMethods m WHERE m.Id = i.SubjectId AND m.CollectionId = N''playground''))))
        THROW 51005, N''A Playground rating must reference a Playground catalog item.'', 1;
END');

DECLARE @grantSql nvarchar(max) = N'';
DECLARE grantCursor CURSOR LOCAL FAST_FORWARD FOR SELECT Name FROM @tables;
OPEN grantCursor;
FETCH NEXT FROM grantCursor INTO @table;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @grantSql += N'GRANT INSERT, UPDATE, DELETE ON OBJECT::dbo.' + QUOTENAME(@table) +
        N' TO ' + QUOTENAME(@RuntimeDatabaseUser) + N';';
    FETCH NEXT FROM grantCursor INTO @table;
END;
CLOSE grantCursor;
DEALLOCATE grantCursor;
SET @grantSql += N'GRANT EXECUTE ON OBJECT::dbo.AssertPlaygroundBounds TO ' +
    QUOTENAME(@RuntimeDatabaseUser) + N';';
EXEC sys.sp_executesql @grantSql;

UPDATE dbo.CatalogSchemaMarker SET Version = @NewVersion WHERE Id = 1;
COMMIT TRANSACTION;

SELECT DB_NAME() AS DatabaseName, @NewVersion AS SchemaVersion, @ActorId AS SandboxActorId,
       N'Playground-only triggers and bounded storage admission installed for this database.' AS ReviewNote;
