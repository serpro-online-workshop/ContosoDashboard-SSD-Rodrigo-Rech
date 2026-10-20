using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ContosoDashboard.Data;

public static class DocumentSchemaInitializer
{
    private static readonly string[] SchemaCommands =
    {
        """
        CREATE TABLE IF NOT EXISTS Documents (
            DocumentId INTEGER NOT NULL CONSTRAINT PK_Documents PRIMARY KEY AUTOINCREMENT,
            Title TEXT NOT NULL,
            Description TEXT NULL,
            Category TEXT NOT NULL,
            UploaderId INTEGER NOT NULL,
            ProjectId INTEGER NULL,
            TaskId INTEGER NULL,
            OriginalFileName TEXT NOT NULL,
            FilePath TEXT NOT NULL,
            FileSize INTEGER NOT NULL,
            FileType TEXT NOT NULL,
            UploadedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL,
            IsAvailable INTEGER NOT NULL,
            CONSTRAINT FK_Documents_Users_UploaderId FOREIGN KEY (UploaderId) REFERENCES Users (UserId) ON DELETE RESTRICT,
            CONSTRAINT FK_Documents_Projects_ProjectId FOREIGN KEY (ProjectId) REFERENCES Projects (ProjectId) ON DELETE RESTRICT,
            CONSTRAINT FK_Documents_Tasks_TaskId FOREIGN KEY (TaskId) REFERENCES Tasks (TaskId) ON DELETE RESTRICT
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS DocumentTags (
            DocumentTagId INTEGER NOT NULL CONSTRAINT PK_DocumentTags PRIMARY KEY AUTOINCREMENT,
            DocumentId INTEGER NOT NULL,
            TagText TEXT NOT NULL,
            NormalizedTagText TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            CONSTRAINT FK_DocumentTags_Documents_DocumentId FOREIGN KEY (DocumentId) REFERENCES Documents (DocumentId) ON DELETE CASCADE
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS DocumentShares (
            DocumentShareId INTEGER NOT NULL CONSTRAINT PK_DocumentShares PRIMARY KEY AUTOINCREMENT,
            DocumentId INTEGER NOT NULL,
            TargetUserId INTEGER NULL,
            TargetDepartment TEXT NULL,
            GrantedByUserId INTEGER NOT NULL,
            GrantedAtUtc TEXT NOT NULL,
            RevokedAtUtc TEXT NULL,
            CONSTRAINT FK_DocumentShares_Documents_DocumentId FOREIGN KEY (DocumentId) REFERENCES Documents (DocumentId) ON DELETE CASCADE,
            CONSTRAINT FK_DocumentShares_Users_TargetUserId FOREIGN KEY (TargetUserId) REFERENCES Users (UserId) ON DELETE RESTRICT,
            CONSTRAINT FK_DocumentShares_Users_GrantedByUserId FOREIGN KEY (GrantedByUserId) REFERENCES Users (UserId) ON DELETE RESTRICT,
            CONSTRAINT CK_DocumentShare_ExactlyOneTarget CHECK ((TargetUserId IS NOT NULL AND TargetDepartment IS NULL) OR (TargetUserId IS NULL AND TargetDepartment IS NOT NULL))
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS DocumentActivities (
            DocumentActivityId INTEGER NOT NULL CONSTRAINT PK_DocumentActivities PRIMARY KEY AUTOINCREMENT,
            DocumentId INTEGER NULL,
            ActorUserId INTEGER NULL,
            ActionType INTEGER NOT NULL,
            DocumentTitleSnapshot TEXT NOT NULL,
            FileType TEXT NULL,
            UploaderDisplayNameSnapshot TEXT NULL,
            OccurredAtUtc TEXT NOT NULL,
            Details TEXT NULL,
            CONSTRAINT FK_DocumentActivities_Documents_DocumentId FOREIGN KEY (DocumentId) REFERENCES Documents (DocumentId) ON DELETE SET NULL,
            CONSTRAINT FK_DocumentActivities_Users_ActorUserId FOREIGN KEY (ActorUserId) REFERENCES Users (UserId) ON DELETE RESTRICT
        );
        """,
        "CREATE UNIQUE INDEX IF NOT EXISTS IX_Documents_FilePath ON Documents (FilePath);",
        "CREATE INDEX IF NOT EXISTS IX_Documents_UploaderId_UploadedAtUtc ON Documents (UploaderId, UploadedAtUtc);",
        "CREATE INDEX IF NOT EXISTS IX_Documents_ProjectId_UploadedAtUtc ON Documents (ProjectId, UploadedAtUtc);",
        "CREATE INDEX IF NOT EXISTS IX_Documents_TaskId ON Documents (TaskId);",
        "CREATE INDEX IF NOT EXISTS IX_Documents_IsAvailable_UploadedAtUtc ON Documents (IsAvailable, UploadedAtUtc);",
        "CREATE UNIQUE INDEX IF NOT EXISTS IX_DocumentTags_DocumentId_NormalizedTagText ON DocumentTags (DocumentId, NormalizedTagText);",
        "CREATE INDEX IF NOT EXISTS IX_DocumentTags_NormalizedTagText_DocumentId ON DocumentTags (NormalizedTagText, DocumentId);",
        "CREATE INDEX IF NOT EXISTS IX_DocumentShares_TargetUserId_RevokedAtUtc ON DocumentShares (TargetUserId, RevokedAtUtc);",
        "CREATE INDEX IF NOT EXISTS IX_DocumentShares_TargetDepartment_RevokedAtUtc ON DocumentShares (TargetDepartment, RevokedAtUtc);",
        "CREATE INDEX IF NOT EXISTS IX_DocumentActivities_OccurredAtUtc_ActionType ON DocumentActivities (OccurredAtUtc, ActionType);"
    };

    public static void EnsureCreated(ApplicationDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen)
        {
            context.Database.OpenConnection();
        }

        try
        {
            context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
            using var transaction = context.Database.BeginTransaction();
            foreach (var command in SchemaCommands)
            {
                context.Database.ExecuteSqlRaw(command);
            }

            transaction.Commit();
        }
        finally
        {
            if (!wasOpen)
            {
                context.Database.CloseConnection();
            }
        }
    }
}
