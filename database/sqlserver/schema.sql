IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [Timestamp] datetimeoffset NOT NULL,
        [UserName] nvarchar(256) NOT NULL,
        [Action] nvarchar(50) NOT NULL,
        [EntityType] nvarchar(100) NOT NULL,
        [EntityId] uniqueidentifier NOT NULL,
        [RootEntityType] nvarchar(100) NULL,
        [RootEntityId] uniqueidentifier NULL,
        [ChangesJson] nvarchar(max) NULL,
        [Summary] nvarchar(2000) NULL,
        [CorrelationId] nvarchar(max) NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [BackgroundJobs] (
        [Id] uniqueidentifier NOT NULL,
        [Type] nvarchar(100) NOT NULL,
        [PayloadJson] nvarchar(max) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [Attempts] int NOT NULL,
        [MaxAttempts] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [AvailableAt] datetimeoffset NOT NULL,
        [LockedUntil] datetimeoffset NULL,
        [LockedBy] nvarchar(max) NULL,
        [CompletedAt] datetimeoffset NULL,
        [LastError] nvarchar(max) NULL,
        CONSTRAINT [PK_BackgroundJobs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [FeatureStatuses] (
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [SortOrder] int NOT NULL,
        [IsTerminal] bit NOT NULL,
        CONSTRAINT [PK_FeatureStatuses] PRIMARY KEY ([Code])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [FeatureTypes] (
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [SortOrder] int NOT NULL,
        [IsContainer] bit NOT NULL,
        [Icon] nvarchar(max) NULL,
        CONSTRAINT [PK_FeatureTypes] PRIMARY KEY ([Code])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [Roles] (
        [Id] uniqueidentifier NOT NULL,
        [Description] nvarchar(max) NULL,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [SequenceScopes] (
        [Id] uniqueidentifier NOT NULL,
        [Key] nvarchar(100) NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [NextValue] bigint NOT NULL,
        [DisplayWidth] int NOT NULL,
        [Prefix] nvarchar(20) NULL,
        CONSTRAINT [PK_SequenceScopes] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [TestFolders] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [ParentId] uniqueidentifier NULL,
        [Path] nvarchar(850) NOT NULL,
        [DisplayOrder] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_TestFolders] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TestFolders_TestFolders_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [TestFolders] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [TestingFrameworks] (
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [Language] nvarchar(max) NOT NULL,
        [Platform] nvarchar(30) NOT NULL,
        CONSTRAINT [PK_TestingFrameworks] PRIMARY KEY ([Code])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [TestPackages] (
        [Id] uniqueidentifier NOT NULL,
        [Number] bigint NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [Description] nvarchar(max) NULL,
        [ProductId] uniqueidentifier NULL,
        [LatestVersionNumber] int NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetimeoffset NULL,
        [DeletedBy] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_TestPackages] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [TestSuites] (
        [Id] uniqueidentifier NOT NULL,
        [Number] bigint NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [Description] nvarchar(max) NULL,
        [ProductId] uniqueidentifier NULL,
        [Tags] nvarchar(max) NULL,
        [OwnerUserName] nvarchar(max) NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetimeoffset NULL,
        [DeletedBy] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_TestSuites] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [TestTypes] (
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [SortOrder] int NOT NULL,
        [IsCustom] bit NOT NULL,
        CONSTRAINT [PK_TestTypes] PRIMARY KEY ([Code])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] uniqueidentifier NOT NULL,
        [DisplayName] nvarchar(max) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [LastLoginAt] datetimeoffset NULL,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [RoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] uniqueidentifier NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_RoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RoleClaims_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [Products] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(4000) NULL,
        [DisplayOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [SequenceScopeId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetimeoffset NULL,
        [DeletedBy] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_Products] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Products_SequenceScopes_SequenceScopeId] FOREIGN KEY ([SequenceScopeId]) REFERENCES [SequenceScopes] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [TestPackageVersions] (
        [Id] uniqueidentifier NOT NULL,
        [PackageId] uniqueidentifier NOT NULL,
        [VersionNumber] int NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [ParentVersionId] uniqueidentifier NULL,
        [ChangeNote] nvarchar(max) NULL,
        [OriginalFileName] nvarchar(max) NOT NULL,
        [ArchiveStorageKey] nvarchar(max) NULL,
        [Sha256] nvarchar(64) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [FileCount] int NOT NULL,
        [SourceRepository] nvarchar(max) NULL,
        [SourceCommit] nvarchar(max) NULL,
        [DetectedFramework] nvarchar(max) NULL,
        [DetectedLanguage] nvarchar(max) NULL,
        [Platform] nvarchar(30) NOT NULL,
        [ValidationReportJson] nvarchar(max) NULL,
        [ValidatedAt] datetimeoffset NULL,
        [ReviewedBy] nvarchar(max) NULL,
        [ReviewedAt] datetimeoffset NULL,
        [ReviewComment] nvarchar(max) NULL,
        [PublishedBy] nvarchar(max) NULL,
        [PublishedAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_TestPackageVersions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TestPackageVersions_TestPackages_PackageId] FOREIGN KEY ([PackageId]) REFERENCES [TestPackages] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [TestCases] (
        [Id] uniqueidentifier NOT NULL,
        [Number] bigint NOT NULL,
        [Name] nvarchar(300) NOT NULL,
        [Description] nvarchar(max) NULL,
        [Objective] nvarchar(max) NULL,
        [TestTypeCode] nvarchar(50) NOT NULL,
        [Platform] nvarchar(30) NOT NULL,
        [FrameworkCode] nvarchar(50) NULL,
        [Priority] nvarchar(20) NOT NULL,
        [FailureSeverity] nvarchar(20) NOT NULL,
        [Preconditions] nvarchar(max) NULL,
        [TestDataRequirements] nvarchar(max) NULL,
        [ExpectedResults] nvarchar(max) NULL,
        [CleanupSteps] nvarchar(max) NULL,
        [EstimatedDurationMinutes] int NULL,
        [AutomationStatus] nvarchar(30) NOT NULL,
        [OwnerUserName] nvarchar(max) NULL,
        [ReviewStatus] nvarchar(30) NOT NULL,
        [Revision] int NOT NULL,
        [FolderId] uniqueidentifier NULL,
        [IsTemplate] bit NOT NULL,
        [IsSharedSteps] bit NOT NULL,
        [ParametersJson] nvarchar(max) NULL,
        [ReviewComment] nvarchar(max) NULL,
        [ApprovedBy] nvarchar(max) NULL,
        [ApprovedAt] datetimeoffset NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetimeoffset NULL,
        [DeletedBy] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_TestCases] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TestCases_TestFolders_FolderId] FOREIGN KEY ([FolderId]) REFERENCES [TestFolders] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_TestCases_TestTypes_TestTypeCode] FOREIGN KEY ([TestTypeCode]) REFERENCES [TestTypes] ([Code]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TestCases_TestingFrameworks_FrameworkCode] FOREIGN KEY ([FrameworkCode]) REFERENCES [TestingFrameworks] ([Code]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [UserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] uniqueidentifier NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_UserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserClaims_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [UserLogins] (
        [LoginProvider] nvarchar(450) NOT NULL,
        [ProviderKey] nvarchar(450) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_UserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_UserLogins_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [UserRoles] (
        [UserId] uniqueidentifier NOT NULL,
        [RoleId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_UserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_UserRoles_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserRoles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [UserTokens] (
        [UserId] uniqueidentifier NOT NULL,
        [LoginProvider] nvarchar(450) NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_UserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_UserTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [ProductVersions] (
        [Id] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [VersionLabel] nvarchar(50) NOT NULL,
        [ReleaseDate] date NULL,
        [IsReleased] bit NOT NULL,
        [Notes] nvarchar(4000) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_ProductVersions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductVersions_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [UserProductAccess] (
        [UserId] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [CanEdit] bit NOT NULL,
        CONSTRAINT [PK_UserProductAccess] PRIMARY KEY ([UserId], [ProductId]),
        CONSTRAINT [FK_UserProductAccess_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserProductAccess_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [TestPackageFiles] (
        [Id] uniqueidentifier NOT NULL,
        [VersionId] uniqueidentifier NOT NULL,
        [Path] nvarchar(800) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [Sha256] nvarchar(64) NOT NULL,
        [StorageKey] nvarchar(max) NOT NULL,
        [IsText] bit NOT NULL,
        [Language] nvarchar(max) NULL,
        CONSTRAINT [PK_TestPackageFiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TestPackageFiles_TestPackageVersions_VersionId] FOREIGN KEY ([VersionId]) REFERENCES [TestPackageVersions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [TestCaseDependencies] (
        [Id] uniqueidentifier NOT NULL,
        [TestCaseId] uniqueidentifier NOT NULL,
        [DependsOnTestCaseId] uniqueidentifier NOT NULL,
        [Type] nvarchar(30) NOT NULL,
        [Notes] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_TestCaseDependencies] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TestCaseDependencies_TestCases_DependsOnTestCaseId] FOREIGN KEY ([DependsOnTestCaseId]) REFERENCES [TestCases] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TestCaseDependencies_TestCases_TestCaseId] FOREIGN KEY ([TestCaseId]) REFERENCES [TestCases] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [TestCaseTags] (
        [TestCaseId] uniqueidentifier NOT NULL,
        [Tag] nvarchar(60) NOT NULL,
        CONSTRAINT [PK_TestCaseTags] PRIMARY KEY ([TestCaseId], [Tag]),
        CONSTRAINT [FK_TestCaseTags_TestCases_TestCaseId] FOREIGN KEY ([TestCaseId]) REFERENCES [TestCases] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [TestCaseVersions] (
        [Id] uniqueidentifier NOT NULL,
        [TestCaseId] uniqueidentifier NOT NULL,
        [Revision] int NOT NULL,
        [SnapshotJson] nvarchar(max) NOT NULL,
        [ChangeSummary] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_TestCaseVersions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TestCaseVersions_TestCases_TestCaseId] FOREIGN KEY ([TestCaseId]) REFERENCES [TestCases] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [TestSteps] (
        [Id] uniqueidentifier NOT NULL,
        [TestCaseId] uniqueidentifier NOT NULL,
        [StepNumber] int NOT NULL,
        [Action] nvarchar(max) NOT NULL,
        [ExpectedResult] nvarchar(max) NULL,
        [TestData] nvarchar(max) NULL,
        [SharedStepsTestCaseId] uniqueidentifier NULL,
        CONSTRAINT [PK_TestSteps] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TestSteps_TestCases_SharedStepsTestCaseId] FOREIGN KEY ([SharedStepsTestCaseId]) REFERENCES [TestCases] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TestSteps_TestCases_TestCaseId] FOREIGN KEY ([TestCaseId]) REFERENCES [TestCases] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [TestSuiteItems] (
        [Id] uniqueidentifier NOT NULL,
        [SuiteId] uniqueidentifier NOT NULL,
        [TestCaseId] uniqueidentifier NOT NULL,
        [Order] int NOT NULL,
        CONSTRAINT [PK_TestSuiteItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TestSuiteItems_TestCases_TestCaseId] FOREIGN KEY ([TestCaseId]) REFERENCES [TestCases] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TestSuiteItems_TestSuites_SuiteId] FOREIGN KEY ([SuiteId]) REFERENCES [TestSuites] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [Features] (
        [Id] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [ParentId] uniqueidentifier NULL,
        [Path] nvarchar(850) NOT NULL,
        [Depth] int NOT NULL,
        [DisplayOrder] int NOT NULL,
        [SequenceNumber] bigint NOT NULL,
        [SequenceScopeId] uniqueidentifier NOT NULL,
        [IsLegacySequence] bit NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [FeatureTypeCode] nvarchar(50) NOT NULL,
        [StatusCode] nvarchar(50) NOT NULL,
        [ApplicationType] nvarchar(30) NOT NULL,
        [Description] nvarchar(max) NULL,
        [BusinessObjective] nvarchar(max) NULL,
        [FunctionalRequirements] nvarchar(max) NULL,
        [CurrentVersion] nvarchar(max) NULL,
        [IntroducedInVersionId] uniqueidentifier NULL,
        [OriginatingRelease] nvarchar(max) NULL,
        [IsDeprecated] bit NOT NULL,
        [ReplacedByFeatureId] uniqueidentifier NULL,
        [DeprecationNote] nvarchar(max) NULL,
        [AssemblyName] nvarchar(max) NULL,
        [Namespace] nvarchar(max) NULL,
        [ClassName] nvarchar(max) NULL,
        [FormOrRoute] nvarchar(max) NULL,
        [RelatedServices] nvarchar(max) NULL,
        [ApiEndpoints] nvarchar(max) NULL,
        [DatabaseObjects] nvarchar(max) NULL,
        [SourceRepository] nvarchar(max) NULL,
        [BuildPackage] nvarchar(max) NULL,
        [EntryPoint] nvarchar(max) NULL,
        [RequiredPermissions] nvarchar(max) NULL,
        [Preconditions] nvarchar(max) NULL,
        [InputParameters] nvarchar(max) NULL,
        [ExpectedOutcomes] nvarchar(max) NULL,
        [BusinessRules] nvarchar(max) NULL,
        [ValidationRules] nvarchar(max) NULL,
        [ErrorConditions] nvarchar(max) NULL,
        [UserRoles] nvarchar(max) NULL,
        [KnownLimitations] nvarchar(max) NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetimeoffset NULL,
        [DeletedBy] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_Features] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Features_FeatureStatuses_StatusCode] FOREIGN KEY ([StatusCode]) REFERENCES [FeatureStatuses] ([Code]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Features_FeatureTypes_FeatureTypeCode] FOREIGN KEY ([FeatureTypeCode]) REFERENCES [FeatureTypes] ([Code]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Features_Features_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [Features] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Features_Features_ReplacedByFeatureId] FOREIGN KEY ([ReplacedByFeatureId]) REFERENCES [Features] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Features_ProductVersions_IntroducedInVersionId] FOREIGN KEY ([IntroducedInVersionId]) REFERENCES [ProductVersions] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_Features_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Features_SequenceScopes_SequenceScopeId] FOREIGN KEY ([SequenceScopeId]) REFERENCES [SequenceScopes] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [FeatureAttachments] (
        [Id] uniqueidentifier NOT NULL,
        [FeatureId] uniqueidentifier NOT NULL,
        [FileName] nvarchar(max) NOT NULL,
        [ContentType] nvarchar(max) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [Sha256] nvarchar(max) NOT NULL,
        [StorageKey] nvarchar(max) NULL,
        [ExternalUrl] nvarchar(max) NULL,
        [Caption] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_FeatureAttachments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FeatureAttachments_Features_FeatureId] FOREIGN KEY ([FeatureId]) REFERENCES [Features] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [FeatureDependencies] (
        [Id] uniqueidentifier NOT NULL,
        [FeatureId] uniqueidentifier NOT NULL,
        [DependsOnFeatureId] uniqueidentifier NOT NULL,
        [Type] nvarchar(30) NOT NULL,
        [Notes] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_FeatureDependencies] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FeatureDependencies_Features_DependsOnFeatureId] FOREIGN KEY ([DependsOnFeatureId]) REFERENCES [Features] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_FeatureDependencies_Features_FeatureId] FOREIGN KEY ([FeatureId]) REFERENCES [Features] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [FeatureRequirements] (
        [Id] uniqueidentifier NOT NULL,
        [FeatureId] uniqueidentifier NOT NULL,
        [Code] nvarchar(50) NOT NULL,
        [Title] nvarchar(max) NOT NULL,
        [Description] nvarchar(max) NULL,
        [Priority] nvarchar(max) NOT NULL,
        [ExternalLink] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_FeatureRequirements] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FeatureRequirements_Features_FeatureId] FOREIGN KEY ([FeatureId]) REFERENCES [Features] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [FeatureSourceMappings] (
        [Id] uniqueidentifier NOT NULL,
        [FeatureId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(30) NOT NULL,
        [Value] nvarchar(450) NOT NULL,
        [Repository] nvarchar(max) NULL,
        [Notes] nvarchar(max) NULL,
        [IsApproved] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_FeatureSourceMappings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FeatureSourceMappings_Features_FeatureId] FOREIGN KEY ([FeatureId]) REFERENCES [Features] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [TestCaseFeatureMappings] (
        [TestCaseId] uniqueidentifier NOT NULL,
        [FeatureId] uniqueidentifier NOT NULL,
        [IsPrimary] bit NOT NULL,
        CONSTRAINT [PK_TestCaseFeatureMappings] PRIMARY KEY ([TestCaseId], [FeatureId]),
        CONSTRAINT [FK_TestCaseFeatureMappings_Features_FeatureId] FOREIGN KEY ([FeatureId]) REFERENCES [Features] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TestCaseFeatureMappings_TestCases_TestCaseId] FOREIGN KEY ([TestCaseId]) REFERENCES [TestCases] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [TestPackageTests] (
        [Id] uniqueidentifier NOT NULL,
        [VersionId] uniqueidentifier NOT NULL,
        [Identifier] nvarchar(800) NOT NULL,
        [DisplayName] nvarchar(max) NOT NULL,
        [FilePath] nvarchar(800) NOT NULL,
        [Line] int NOT NULL,
        [Framework] nvarchar(max) NOT NULL,
        [Traits] nvarchar(max) NULL,
        [TestCaseId] uniqueidentifier NULL,
        [FeatureId] uniqueidentifier NULL,
        CONSTRAINT [PK_TestPackageTests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TestPackageTests_Features_FeatureId] FOREIGN KEY ([FeatureId]) REFERENCES [Features] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_TestPackageTests_TestCases_TestCaseId] FOREIGN KEY ([TestCaseId]) REFERENCES [TestCases] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_TestPackageTests_TestPackageVersions_VersionId] FOREIGN KEY ([VersionId]) REFERENCES [TestPackageVersions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE TABLE [TestCaseRequirementLinks] (
        [TestCaseId] uniqueidentifier NOT NULL,
        [RequirementId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_TestCaseRequirementLinks] PRIMARY KEY ([TestCaseId], [RequirementId]),
        CONSTRAINT [FK_TestCaseRequirementLinks_FeatureRequirements_RequirementId] FOREIGN KEY ([RequirementId]) REFERENCES [FeatureRequirements] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TestCaseRequirementLinks_TestCases_TestCaseId] FOREIGN KEY ([TestCaseId]) REFERENCES [TestCases] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_EntityType_EntityId] ON [AuditLogs] ([EntityType], [EntityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_RootEntityType_RootEntityId] ON [AuditLogs] ([RootEntityType], [RootEntityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_Timestamp] ON [AuditLogs] ([Timestamp]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_BackgroundJobs_Status_AvailableAt] ON [BackgroundJobs] ([Status], [AvailableAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_FeatureAttachments_FeatureId] ON [FeatureAttachments] ([FeatureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_FeatureDependencies_DependsOnFeatureId] ON [FeatureDependencies] ([DependsOnFeatureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_FeatureDependencies_FeatureId_DependsOnFeatureId] ON [FeatureDependencies] ([FeatureId], [DependsOnFeatureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_FeatureRequirements_FeatureId_Code] ON [FeatureRequirements] ([FeatureId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Features_CreatedAt] ON [Features] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Features_FeatureTypeCode] ON [Features] ([FeatureTypeCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Features_IntroducedInVersionId] ON [Features] ([IntroducedInVersionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Features_Name] ON [Features] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Features_ParentId] ON [Features] ([ParentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Features_Path] ON [Features] ([Path]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Features_ProductId_ParentId_DisplayOrder] ON [Features] ([ProductId], [ParentId], [DisplayOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Features_ReplacedByFeatureId] ON [Features] ([ReplacedByFeatureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Features_SequenceScopeId_SequenceNumber] ON [Features] ([SequenceScopeId], [SequenceNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Features_StatusCode] ON [Features] ([StatusCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_FeatureSourceMappings_FeatureId] ON [FeatureSourceMappings] ([FeatureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_FeatureSourceMappings_Kind_Value] ON [FeatureSourceMappings] ([Kind], [Value]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Products_Code] ON [Products] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_SequenceScopeId] ON [Products] ([SequenceScopeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductVersions_ProductId_VersionLabel] ON [ProductVersions] ([ProductId], [VersionLabel]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RoleClaims_RoleId] ON [RoleClaims] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [Roles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SequenceScopes_Key] ON [SequenceScopes] ([Key]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TestCaseDependencies_DependsOnTestCaseId] ON [TestCaseDependencies] ([DependsOnTestCaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TestCaseDependencies_TestCaseId_DependsOnTestCaseId] ON [TestCaseDependencies] ([TestCaseId], [DependsOnTestCaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TestCaseFeatureMappings_FeatureId] ON [TestCaseFeatureMappings] ([FeatureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TestCaseRequirementLinks_RequirementId] ON [TestCaseRequirementLinks] ([RequirementId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TestCases_FolderId] ON [TestCases] ([FolderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TestCases_FrameworkCode] ON [TestCases] ([FrameworkCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TestCases_Name] ON [TestCases] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TestCases_Number] ON [TestCases] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TestCases_ReviewStatus] ON [TestCases] ([ReviewStatus]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TestCases_TestTypeCode] ON [TestCases] ([TestTypeCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TestCaseTags_Tag] ON [TestCaseTags] ([Tag]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TestCaseVersions_TestCaseId_Revision] ON [TestCaseVersions] ([TestCaseId], [Revision]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TestFolders_ParentId] ON [TestFolders] ([ParentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TestFolders_Path] ON [TestFolders] ([Path]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TestPackageFiles_VersionId_Path] ON [TestPackageFiles] ([VersionId], [Path]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TestPackages_Number] ON [TestPackages] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TestPackageTests_FeatureId] ON [TestPackageTests] ([FeatureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TestPackageTests_TestCaseId] ON [TestPackageTests] ([TestCaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TestPackageTests_VersionId_Identifier] ON [TestPackageTests] ([VersionId], [Identifier]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TestPackageVersions_PackageId_VersionNumber] ON [TestPackageVersions] ([PackageId], [VersionNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TestPackageVersions_Status] ON [TestPackageVersions] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TestSteps_SharedStepsTestCaseId] ON [TestSteps] ([SharedStepsTestCaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TestSteps_TestCaseId_StepNumber] ON [TestSteps] ([TestCaseId], [StepNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TestSuiteItems_SuiteId_TestCaseId] ON [TestSuiteItems] ([SuiteId], [TestCaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TestSuiteItems_TestCaseId] ON [TestSuiteItems] ([TestCaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TestSuites_Number] ON [TestSuites] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserClaims_UserId] ON [UserClaims] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserLogins_UserId] ON [UserLogins] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserProductAccess_ProductId] ON [UserProductAccess] ([ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserRoles_RoleId] ON [UserRoles] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [Users] ([NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [Users] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027061607_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261027061607_InitialCreate', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE TABLE [ExecutionAgents] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Kind] nvarchar(30) NOT NULL,
        [KeyHash] nvarchar(64) NOT NULL,
        [KeyPrefix] nvarchar(16) NOT NULL,
        [Pool] nvarchar(100) NULL,
        [Description] nvarchar(max) NULL,
        [IsEnabled] bit NOT NULL,
        [InMaintenance] bit NOT NULL,
        [MachineName] nvarchar(max) NULL,
        [OperatingSystem] nvarchar(max) NULL,
        [AgentVersion] nvarchar(max) NULL,
        [Slots] int NOT NULL,
        [FrameworksCsv] nvarchar(400) NULL,
        [InteractiveSession] bit NOT NULL,
        [ScreenResolution] nvarchar(max) NULL,
        [DpiScalePercent] int NULL,
        [ProcessorCount] int NOT NULL,
        [TotalMemoryMb] bigint NOT NULL,
        [CapabilitiesJson] nvarchar(max) NULL,
        [LastHeartbeatAt] datetimeoffset NULL,
        [RegisteredAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_ExecutionAgents] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE TABLE [ExecutionArtifacts] (
        [Id] uniqueidentifier NOT NULL,
        [RunId] uniqueidentifier NOT NULL,
        [JobId] uniqueidentifier NOT NULL,
        [TestExecutionId] uniqueidentifier NULL,
        [Kind] nvarchar(30) NOT NULL,
        [FileName] nvarchar(400) NOT NULL,
        [ContentType] nvarchar(200) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [Sha256] nvarchar(64) NOT NULL,
        [StorageKey] nvarchar(200) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_ExecutionArtifacts] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE TABLE [ExecutionLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [JobId] uniqueidentifier NOT NULL,
        [Sequence] int NOT NULL,
        [Timestamp] datetimeoffset NOT NULL,
        [Level] nvarchar(20) NOT NULL,
        [Message] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_ExecutionLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE TABLE [ExecutionRuns] (
        [Id] uniqueidentifier NOT NULL,
        [Number] bigint NOT NULL,
        [Name] nvarchar(300) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [ScopeDescription] nvarchar(1000) NOT NULL,
        [ScopeJson] nvarchar(max) NULL,
        [Priority] int NOT NULL,
        [SchedulingPaused] bit NOT NULL,
        [CancelRequested] bit NOT NULL,
        [VariablesJson] nvarchar(max) NULL,
        [MaxInfrastructureRetries] int NOT NULL,
        [TimeoutMinutes] int NOT NULL,
        [SourceRunId] uniqueidentifier NULL,
        [StartedAt] datetimeoffset NULL,
        [CompletedAt] datetimeoffset NULL,
        [TotalTests] int NOT NULL,
        [Passed] int NOT NULL,
        [Failed] int NOT NULL,
        [Skipped] int NOT NULL,
        [Blocked] int NOT NULL,
        [NotRun] int NOT NULL,
        [InfrastructureErrors] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_ExecutionRuns] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE TABLE [ExecutionJobs] (
        [Id] uniqueidentifier NOT NULL,
        [RunId] uniqueidentifier NOT NULL,
        [PackageVersionId] uniqueidentifier NOT NULL,
        [Framework] nvarchar(50) NOT NULL,
        [RequiresDesktop] bit NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [Attempt] int NOT NULL,
        [AgentId] uniqueidentifier NULL,
        [LeaseExpiresAt] datetimeoffset NULL,
        [LeasedAt] datetimeoffset NULL,
        [StartedAt] datetimeoffset NULL,
        [CompletedAt] datetimeoffset NULL,
        [CancelRequested] bit NOT NULL,
        [TestIdentifiersJson] nvarchar(max) NOT NULL,
        [Error] nvarchar(max) NULL,
        [ExitCode] int NULL,
        [RetryOfJobId] uniqueidentifier NULL,
        [LogLineCount] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_ExecutionJobs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ExecutionJobs_ExecutionAgents_AgentId] FOREIGN KEY ([AgentId]) REFERENCES [ExecutionAgents] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_ExecutionJobs_ExecutionRuns_RunId] FOREIGN KEY ([RunId]) REFERENCES [ExecutionRuns] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ExecutionJobs_TestPackageVersions_PackageVersionId] FOREIGN KEY ([PackageVersionId]) REFERENCES [TestPackageVersions] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE TABLE [TestExecutions] (
        [Id] uniqueidentifier NOT NULL,
        [RunId] uniqueidentifier NOT NULL,
        [JobId] uniqueidentifier NULL,
        [Attempt] int NOT NULL,
        [PackageVersionId] uniqueidentifier NULL,
        [PackageTestId] uniqueidentifier NULL,
        [TestCaseId] uniqueidentifier NULL,
        [FeatureId] uniqueidentifier NULL,
        [Identifier] nvarchar(800) NOT NULL,
        [DisplayName] nvarchar(400) NOT NULL,
        [Outcome] nvarchar(30) NOT NULL,
        [Reason] nvarchar(1000) NULL,
        [DurationMs] bigint NULL,
        [ErrorMessage] nvarchar(max) NULL,
        [StackTrace] nvarchar(max) NULL,
        [Output] nvarchar(max) NULL,
        [StartedAt] datetimeoffset NULL,
        [CompletedAt] datetimeoffset NULL,
        [AgentId] uniqueidentifier NULL,
        [IsSuperseded] bit NOT NULL,
        CONSTRAINT [PK_TestExecutions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TestExecutions_ExecutionRuns_RunId] FOREIGN KEY ([RunId]) REFERENCES [ExecutionRuns] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ExecutionAgents_KeyHash] ON [ExecutionAgents] ([KeyHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ExecutionAgents_Name] ON [ExecutionAgents] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE INDEX [IX_ExecutionArtifacts_RunId] ON [ExecutionArtifacts] ([RunId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE INDEX [IX_ExecutionArtifacts_TestExecutionId] ON [ExecutionArtifacts] ([TestExecutionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE INDEX [IX_ExecutionJobs_AgentId_Status] ON [ExecutionJobs] ([AgentId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE INDEX [IX_ExecutionJobs_PackageVersionId] ON [ExecutionJobs] ([PackageVersionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE INDEX [IX_ExecutionJobs_RunId] ON [ExecutionJobs] ([RunId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE INDEX [IX_ExecutionJobs_Status_Framework] ON [ExecutionJobs] ([Status], [Framework]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE INDEX [IX_ExecutionLogs_JobId_Sequence] ON [ExecutionLogs] ([JobId], [Sequence]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE INDEX [IX_ExecutionRuns_CreatedAt] ON [ExecutionRuns] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ExecutionRuns_Number] ON [ExecutionRuns] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE INDEX [IX_ExecutionRuns_Status] ON [ExecutionRuns] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE INDEX [IX_TestExecutions_FeatureId_CompletedAt] ON [TestExecutions] ([FeatureId], [CompletedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE INDEX [IX_TestExecutions_JobId_Identifier] ON [TestExecutions] ([JobId], [Identifier]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE INDEX [IX_TestExecutions_RunId_Outcome] ON [TestExecutions] ([RunId], [Outcome]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    CREATE INDEX [IX_TestExecutions_TestCaseId_CompletedAt] ON [TestExecutions] ([TestCaseId], [CompletedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027070412_ExecutionInfrastructure'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261027070412_ExecutionInfrastructure', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    ALTER TABLE [TestExecutions] ADD [ExecutedBy] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    ALTER TABLE [TestExecutions] ADD [PrerequisitesJson] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    ALTER TABLE [TestCases] ADD [DataRequirements] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    ALTER TABLE [TestCases] ADD [IsDestructive] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    ALTER TABLE [TestCases] ADD [ResourceKeys] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    ALTER TABLE [ExecutionRuns] ADD [BuildVersion] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    ALTER TABLE [ExecutionRuns] ADD [CampaignId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    ALTER TABLE [ExecutionRuns] ADD [EnvironmentId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    ALTER TABLE [ExecutionRuns] ADD [IncludeManual] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    ALTER TABLE [ExecutionRuns] ADD [PlanId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    ALTER TABLE [ExecutionRuns] ADD [ScheduleId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    ALTER TABLE [ExecutionRuns] ADD [TriggeredBy] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    ALTER TABLE [ExecutionJobs] ADD [AgentPools] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    ALTER TABLE [ExecutionJobs] ADD [DataRequirementsJson] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    ALTER TABLE [ExecutionJobs] ADD [EnvironmentId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    ALTER TABLE [ExecutionJobs] ADD [ResourceKeys] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    ALTER TABLE [ExecutionJobs] ADD [Wave] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE TABLE [ExecutionJobDependencies] (
        [JobId] uniqueidentifier NOT NULL,
        [DependsOnJobId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_ExecutionJobDependencies] PRIMARY KEY ([JobId], [DependsOnJobId]),
        CONSTRAINT [FK_ExecutionJobDependencies_ExecutionJobs_DependsOnJobId] FOREIGN KEY ([DependsOnJobId]) REFERENCES [ExecutionJobs] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ExecutionJobDependencies_ExecutionJobs_JobId] FOREIGN KEY ([JobId]) REFERENCES [ExecutionJobs] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE TABLE [ResourceLocks] (
        [Key] nvarchar(200) NOT NULL,
        [JobId] uniqueidentifier NOT NULL,
        [RunId] uniqueidentifier NOT NULL,
        [AcquiredAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_ResourceLocks] PRIMARY KEY ([Key])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE TABLE [TestCampaigns] (
        [Id] uniqueidentifier NOT NULL,
        [Number] bigint NOT NULL,
        [Name] nvarchar(300) NOT NULL,
        [Description] nvarchar(max) NULL,
        [BuildVersion] nvarchar(max) NULL,
        [Status] nvarchar(30) NOT NULL,
        [StartedAt] datetimeoffset NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetimeoffset NULL,
        [DeletedBy] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_TestCampaigns] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE TABLE [TestEnvironments] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Kind] nvarchar(30) NOT NULL,
        [Description] nvarchar(max) NULL,
        [IsProduction] bit NOT NULL,
        [AllowedDestructiveTestCaseIds] nvarchar(max) NULL,
        [BaseUrl] nvarchar(max) NULL,
        [ApplicationBuildVersion] nvarchar(max) NULL,
        [DatabaseReference] nvarchar(max) NULL,
        [TenantCompany] nvarchar(max) NULL,
        [AuthenticationNotes] nvarchar(max) NULL,
        [Dependencies] nvarchar(max) NULL,
        [AgentPools] nvarchar(400) NULL,
        [HealthCheckUrl] nvarchar(max) NULL,
        [Health] nvarchar(30) NOT NULL,
        [HealthMessage] nvarchar(max) NULL,
        [LastHealthCheckAt] datetimeoffset NULL,
        [InMaintenance] bit NOT NULL,
        [ResetProcedure] nvarchar(max) NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetimeoffset NULL,
        [DeletedBy] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_TestEnvironments] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE TABLE [TestPlans] (
        [Id] uniqueidentifier NOT NULL,
        [Number] bigint NOT NULL,
        [Name] nvarchar(300) NOT NULL,
        [Objective] nvarchar(max) NULL,
        [Scope] nvarchar(max) NULL,
        [ProductId] uniqueidentifier NULL,
        [TargetReleaseId] uniqueidentifier NULL,
        [BuildVersion] nvarchar(max) NULL,
        [EnvironmentId] uniqueidentifier NULL,
        [Owners] nvarchar(max) NULL,
        [Testers] nvarchar(max) NULL,
        [StartDate] date NULL,
        [EndDate] date NULL,
        [EntryCriteria] nvarchar(max) NULL,
        [ExitCriteria] nvarchar(max) NULL,
        [Risk] nvarchar(30) NOT NULL,
        [DefectAcceptanceRules] nvarchar(max) NULL,
        [Status] nvarchar(30) NOT NULL,
        [IsTemplate] bit NOT NULL,
        [ApprovedBy] nvarchar(max) NULL,
        [ApprovedAt] datetimeoffset NULL,
        [ApprovalComment] nvarchar(max) NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetimeoffset NULL,
        [DeletedBy] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_TestPlans] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE TABLE [TestSchedules] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [IsEnabled] bit NOT NULL,
        [Trigger] nvarchar(30) NOT NULL,
        [CronExpression] nvarchar(100) NULL,
        [TimeZoneId] nvarchar(100) NOT NULL,
        [RunOnceAt] datetimeoffset NULL,
        [TargetType] nvarchar(30) NOT NULL,
        [TargetId] uniqueidentifier NOT NULL,
        [EnvironmentId] uniqueidentifier NULL,
        [VariablesJson] nvarchar(max) NULL,
        [Priority] int NOT NULL,
        [NextRunAt] datetimeoffset NULL,
        [LastTriggeredAt] datetimeoffset NULL,
        [LastRunId] uniqueidentifier NULL,
        [LastError] nvarchar(max) NULL,
        [WebhookTokenHash] nvarchar(64) NULL,
        [WebhookTokenPrefix] nvarchar(16) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_TestSchedules] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE TABLE [EnvironmentVariables] (
        [Id] uniqueidentifier NOT NULL,
        [EnvironmentId] uniqueidentifier NOT NULL,
        [Name] nvarchar(64) NOT NULL,
        [Value] nvarchar(max) NULL,
        [ProtectedValue] nvarchar(max) NULL,
        [IsSecret] bit NOT NULL,
        [Description] nvarchar(max) NULL,
        CONSTRAINT [PK_EnvironmentVariables] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EnvironmentVariables_TestEnvironments_EnvironmentId] FOREIGN KEY ([EnvironmentId]) REFERENCES [TestEnvironments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE TABLE [TestCampaignEnvironments] (
        [CampaignId] uniqueidentifier NOT NULL,
        [EnvironmentId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_TestCampaignEnvironments] PRIMARY KEY ([CampaignId], [EnvironmentId]),
        CONSTRAINT [FK_TestCampaignEnvironments_TestCampaigns_CampaignId] FOREIGN KEY ([CampaignId]) REFERENCES [TestCampaigns] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TestCampaignEnvironments_TestEnvironments_EnvironmentId] FOREIGN KEY ([EnvironmentId]) REFERENCES [TestEnvironments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE TABLE [TestDataSets] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(max) NULL,
        [Category] nvarchar(max) NULL,
        [EnvironmentId] uniqueidentifier NULL,
        [DataVersion] int NOT NULL,
        [SetupNotes] nvarchar(max) NULL,
        [CleanupNotes] nvarchar(max) NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetimeoffset NULL,
        [DeletedBy] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_TestDataSets] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TestDataSets_TestEnvironments_EnvironmentId] FOREIGN KEY ([EnvironmentId]) REFERENCES [TestEnvironments] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE TABLE [TestCampaignPlans] (
        [CampaignId] uniqueidentifier NOT NULL,
        [PlanId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_TestCampaignPlans] PRIMARY KEY ([CampaignId], [PlanId]),
        CONSTRAINT [FK_TestCampaignPlans_TestCampaigns_CampaignId] FOREIGN KEY ([CampaignId]) REFERENCES [TestCampaigns] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TestCampaignPlans_TestPlans_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [TestPlans] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE TABLE [TestPlanSuites] (
        [PlanId] uniqueidentifier NOT NULL,
        [SuiteId] uniqueidentifier NOT NULL,
        [Order] int NOT NULL,
        CONSTRAINT [PK_TestPlanSuites] PRIMARY KEY ([PlanId], [SuiteId]),
        CONSTRAINT [FK_TestPlanSuites_TestPlans_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [TestPlans] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TestPlanSuites_TestSuites_SuiteId] FOREIGN KEY ([SuiteId]) REFERENCES [TestSuites] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE TABLE [TestDataRecords] (
        [Id] uniqueidentifier NOT NULL,
        [DataSetId] uniqueidentifier NOT NULL,
        [Key] nvarchar(200) NOT NULL,
        [ValuesJson] nvarchar(max) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_TestDataRecords] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TestDataRecords_TestDataSets_DataSetId] FOREIGN KEY ([DataSetId]) REFERENCES [TestDataSets] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE TABLE [TestDataReservations] (
        [Id] uniqueidentifier NOT NULL,
        [RecordId] uniqueidentifier NOT NULL,
        [DataSetId] uniqueidentifier NOT NULL,
        [RunId] uniqueidentifier NOT NULL,
        [JobId] uniqueidentifier NOT NULL,
        [ReservedAt] datetimeoffset NOT NULL,
        [ReleasedAt] datetimeoffset NULL,
        [Active] int NULL,
        CONSTRAINT [PK_TestDataReservations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TestDataReservations_TestDataRecords_RecordId] FOREIGN KEY ([RecordId]) REFERENCES [TestDataRecords] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE INDEX [IX_ExecutionRuns_CampaignId] ON [ExecutionRuns] ([CampaignId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE INDEX [IX_ExecutionRuns_PlanId] ON [ExecutionRuns] ([PlanId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE INDEX [IX_ExecutionRuns_ScheduleId] ON [ExecutionRuns] ([ScheduleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EnvironmentVariables_EnvironmentId_Name] ON [EnvironmentVariables] ([EnvironmentId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE INDEX [IX_ExecutionJobDependencies_DependsOnJobId] ON [ExecutionJobDependencies] ([DependsOnJobId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE INDEX [IX_ResourceLocks_JobId] ON [ResourceLocks] ([JobId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE INDEX [IX_TestCampaignEnvironments_EnvironmentId] ON [TestCampaignEnvironments] ([EnvironmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE INDEX [IX_TestCampaignPlans_PlanId] ON [TestCampaignPlans] ([PlanId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TestCampaigns_Number] ON [TestCampaigns] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TestDataRecords_DataSetId_Key] ON [TestDataRecords] ([DataSetId], [Key]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE INDEX [IX_TestDataReservations_JobId] ON [TestDataReservations] ([JobId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_TestDataReservations_RecordId] ON [TestDataReservations] ([RecordId]) WHERE [Active] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TestDataSets_Code] ON [TestDataSets] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE INDEX [IX_TestDataSets_EnvironmentId] ON [TestDataSets] ([EnvironmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TestEnvironments_Code] ON [TestEnvironments] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TestPlans_Number] ON [TestPlans] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE INDEX [IX_TestPlanSuites_SuiteId] ON [TestPlanSuites] ([SuiteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    CREATE INDEX [IX_TestSchedules_IsEnabled_NextRunAt] ON [TestSchedules] ([IsEnabled], [NextRunAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027073700_Orchestration'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261027073700_Orchestration', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    ALTER TABLE [ProductVersions] ADD [BuildVersion] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    ALTER TABLE [ProductVersions] ADD [KnownRisks] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    ALTER TABLE [ProductVersions] ADD [Status] nvarchar(30) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    ALTER TABLE [ProductVersions] ADD [TestingStartDate] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    ALTER TABLE [ExecutionRuns] ADD [DefectId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    ALTER TABLE [ExecutionRuns] ADD [ReleaseId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    ALTER TABLE [ExecutionAgents] ADD [OfflineNotifiedAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [ApplicationBuilds] (
        [Id] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [RepositoryId] uniqueidentifier NULL,
        [ReleaseId] uniqueidentifier NULL,
        [BuildNumber] nvarchar(100) NOT NULL,
        [Branch] nvarchar(200) NULL,
        [CommitId] nvarchar(100) NULL,
        [BuildUrl] nvarchar(800) NULL,
        [ChangedFilesJson] nvarchar(max) NULL,
        [CommitsJson] nvarchar(max) NULL,
        [ImpactedFeatureCount] int NOT NULL,
        [TriggeredRunId] uniqueidentifier NULL,
        [GateStatus] nvarchar(30) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_ApplicationBuilds] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ApplicationBuilds_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [Defects] (
        [Id] uniqueidentifier NOT NULL,
        [Number] bigint NOT NULL,
        [Title] nvarchar(300) NOT NULL,
        [Description] nvarchar(max) NULL,
        [ProductId] uniqueidentifier NULL,
        [FeatureId] uniqueidentifier NULL,
        [TestCaseId] uniqueidentifier NULL,
        [FailingTestExecutionId] uniqueidentifier NULL,
        [FailingRunId] uniqueidentifier NULL,
        [BuildVersion] nvarchar(100) NULL,
        [EnvironmentId] uniqueidentifier NULL,
        [Severity] nvarchar(30) NOT NULL,
        [Priority] nvarchar(10) NOT NULL,
        [ReproductionSteps] nvarchar(max) NULL,
        [ExpectedResult] nvarchar(max) NULL,
        [ActualResult] nvarchar(max) NULL,
        [AssignedTo] nvarchar(256) NULL,
        [Status] nvarchar(30) NOT NULL,
        [RootCause] nvarchar(max) NULL,
        [FixVersionId] uniqueidentifier NULL,
        [VerificationResult] nvarchar(max) NULL,
        [DuplicateOfId] uniqueidentifier NULL,
        [Fingerprint] nvarchar(64) NULL,
        [OccurrenceCount] int NOT NULL,
        [LastSeenAt] datetimeoffset NULL,
        [ExternalSystem] nvarchar(30) NULL,
        [ExternalKey] nvarchar(100) NULL,
        [ExternalUrl] nvarchar(800) NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetimeoffset NULL,
        [DeletedBy] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_Defects] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [IntegrationKeys] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [KeyHash] nvarchar(64) NOT NULL,
        [KeyPrefix] nvarchar(16) NOT NULL,
        [IsEnabled] bit NOT NULL,
        [LastUsedAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_IntegrationKeys] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [IssueTrackerConnections] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Kind] nvarchar(30) NOT NULL,
        [Target] nvarchar(400) NOT NULL,
        [BaseUrl] nvarchar(400) NULL,
        [Username] nvarchar(256) NULL,
        [ProtectedToken] nvarchar(max) NULL,
        [ProductId] uniqueidentifier NULL,
        [IsEnabled] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_IssueTrackerConnections] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [NotificationChannels] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Type] nvarchar(30) NOT NULL,
        [Recipients] nvarchar(max) NULL,
        [ProtectedUrl] nvarchar(max) NULL,
        [ProtectedSigningSecret] nvarchar(max) NULL,
        [IsEnabled] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_NotificationChannels] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [NotificationEvents] (
        [Id] uniqueidentifier NOT NULL,
        [Type] nvarchar(60) NOT NULL,
        [Severity] nvarchar(20) NOT NULL,
        [Title] nvarchar(400) NOT NULL,
        [Body] nvarchar(max) NULL,
        [Link] nvarchar(400) NULL,
        [ProductId] uniqueidentifier NULL,
        [DataJson] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [AcknowledgedAt] datetimeoffset NULL,
        [AcknowledgedBy] nvarchar(256) NULL,
        [EscalatedAt] datetimeoffset NULL,
        CONSTRAINT [PK_NotificationEvents] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [NotificationTemplates] (
        [EventType] nvarchar(60) NOT NULL,
        [Subject] nvarchar(400) NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_NotificationTemplates] PRIMARY KEY ([EventType])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [QualityGates] (
        [Id] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [ReleaseId] uniqueidentifier NULL,
        [Name] nvarchar(200) NOT NULL,
        [Type] nvarchar(40) NOT NULL,
        [Parameter] nvarchar(200) NULL,
        [IsBlocking] bit NOT NULL,
        [Order] int NOT NULL,
        [IsEnabled] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_QualityGates] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_QualityGates_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [ReleaseApprovals] (
        [Id] uniqueidentifier NOT NULL,
        [ReleaseId] uniqueidentifier NOT NULL,
        [Decision] nvarchar(30) NOT NULL,
        [Comment] nvarchar(max) NOT NULL,
        [ApprovedBy] nvarchar(256) NOT NULL,
        [ApprovedAt] datetimeoffset NOT NULL,
        [ReportStorageKey] nvarchar(200) NOT NULL,
        [ReportSha256] nvarchar(64) NOT NULL,
        [GateSummaryJson] nvarchar(max) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_ReleaseApprovals] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ReleaseApprovals_ProductVersions_ReleaseId] FOREIGN KEY ([ReleaseId]) REFERENCES [ProductVersions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [ReleaseFeatureChanges] (
        [Id] uniqueidentifier NOT NULL,
        [ReleaseId] uniqueidentifier NOT NULL,
        [FeatureId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(30) NOT NULL,
        [Source] nvarchar(30) NOT NULL,
        [Risk] nvarchar(30) NOT NULL,
        [Notes] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_ReleaseFeatureChanges] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ReleaseFeatureChanges_Features_FeatureId] FOREIGN KEY ([FeatureId]) REFERENCES [Features] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ReleaseFeatureChanges_ProductVersions_ReleaseId] FOREIGN KEY ([ReleaseId]) REFERENCES [ProductVersions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [SavedReports] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [ReportKey] nvarchar(60) NOT NULL,
        [ParametersJson] nvarchar(max) NOT NULL,
        [Columns] nvarchar(2000) NULL,
        [Owner] nvarchar(256) NOT NULL,
        [IsShared] bit NOT NULL,
        [ScheduleCron] nvarchar(100) NULL,
        [DeliveryChannelId] uniqueidentifier NULL,
        [Format] nvarchar(10) NOT NULL,
        [NextRunAt] datetimeoffset NULL,
        [LastRunAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_SavedReports] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [SourceRepositories] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Type] nvarchar(30) NOT NULL,
        [Url] nvarchar(800) NULL,
        [ProductId] uniqueidentifier NULL,
        [DefaultBranch] nvarchar(200) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_SourceRepositories] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [BuildFeatureImpacts] (
        [BuildId] uniqueidentifier NOT NULL,
        [FeatureId] uniqueidentifier NOT NULL,
        [Reason] nvarchar(30) NOT NULL,
        [Evidence] nvarchar(1000) NOT NULL,
        CONSTRAINT [PK_BuildFeatureImpacts] PRIMARY KEY ([BuildId], [FeatureId]),
        CONSTRAINT [FK_BuildFeatureImpacts_ApplicationBuilds_BuildId] FOREIGN KEY ([BuildId]) REFERENCES [ApplicationBuilds] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_BuildFeatureImpacts_Features_FeatureId] FOREIGN KEY ([FeatureId]) REFERENCES [Features] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [SourceCommits] (
        [Id] uniqueidentifier NOT NULL,
        [BuildId] uniqueidentifier NOT NULL,
        [RepositoryId] uniqueidentifier NULL,
        [CommitId] nvarchar(100) NOT NULL,
        [Author] nvarchar(256) NULL,
        [Message] nvarchar(max) NOT NULL,
        [CommittedAt] datetimeoffset NULL,
        [FilesJson] nvarchar(max) NULL,
        CONSTRAINT [PK_SourceCommits] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SourceCommits_ApplicationBuilds_BuildId] FOREIGN KEY ([BuildId]) REFERENCES [ApplicationBuilds] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [DefectEvents] (
        [Id] bigint NOT NULL IDENTITY,
        [DefectId] uniqueidentifier NOT NULL,
        [Timestamp] datetimeoffset NOT NULL,
        [UserName] nvarchar(256) NOT NULL,
        [Kind] nvarchar(30) NOT NULL,
        [FromStatus] nvarchar(30) NULL,
        [ToStatus] nvarchar(30) NULL,
        [Text] nvarchar(max) NULL,
        CONSTRAINT [PK_DefectEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DefectEvents_Defects_DefectId] FOREIGN KEY ([DefectId]) REFERENCES [Defects] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [DefectOccurrences] (
        [DefectId] uniqueidentifier NOT NULL,
        [TestExecutionId] uniqueidentifier NOT NULL,
        [RunId] uniqueidentifier NOT NULL,
        [LinkedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_DefectOccurrences] PRIMARY KEY ([DefectId], [TestExecutionId]),
        CONSTRAINT [FK_DefectOccurrences_Defects_DefectId] FOREIGN KEY ([DefectId]) REFERENCES [Defects] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [EscalationPolicies] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [EventTypes] nvarchar(1000) NOT NULL,
        [AfterMinutes] int NOT NULL,
        [ChannelId] uniqueidentifier NOT NULL,
        [IsEnabled] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_EscalationPolicies] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EscalationPolicies_NotificationChannels_ChannelId] FOREIGN KEY ([ChannelId]) REFERENCES [NotificationChannels] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [NotificationRules] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [EventTypes] nvarchar(1000) NOT NULL,
        [ProductId] uniqueidentifier NULL,
        [MinSeverity] nvarchar(20) NOT NULL,
        [UserId] uniqueidentifier NULL,
        [EmailUser] bit NOT NULL,
        [ChannelId] uniqueidentifier NULL,
        [IsEnabled] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_NotificationRules] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_NotificationRules_NotificationChannels_ChannelId] FOREIGN KEY ([ChannelId]) REFERENCES [NotificationChannels] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [NotificationDeliveries] (
        [Id] uniqueidentifier NOT NULL,
        [EventId] uniqueidentifier NOT NULL,
        [ChannelId] uniqueidentifier NULL,
        [EmailTo] nvarchar(400) NULL,
        [Status] nvarchar(30) NOT NULL,
        [Attempts] int NOT NULL,
        [Error] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [SentAt] datetimeoffset NULL,
        [IsEscalation] bit NOT NULL,
        [AttachmentKey] nvarchar(200) NULL,
        [AttachmentName] nvarchar(200) NULL,
        CONSTRAINT [PK_NotificationDeliveries] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_NotificationDeliveries_NotificationEvents_EventId] FOREIGN KEY ([EventId]) REFERENCES [NotificationEvents] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [UserNotifications] (
        [EventId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [ReadAt] datetimeoffset NULL,
        CONSTRAINT [PK_UserNotifications] PRIMARY KEY ([UserId], [EventId]),
        CONSTRAINT [FK_UserNotifications_NotificationEvents_EventId] FOREIGN KEY ([EventId]) REFERENCES [NotificationEvents] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserNotifications_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [GateWaivers] (
        [Id] uniqueidentifier NOT NULL,
        [GateId] uniqueidentifier NOT NULL,
        [ReleaseId] uniqueidentifier NOT NULL,
        [Justification] nvarchar(max) NOT NULL,
        [ApprovedBy] nvarchar(256) NOT NULL,
        [ApprovedAt] datetimeoffset NOT NULL,
        [RevokedAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_GateWaivers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_GateWaivers_QualityGates_GateId] FOREIGN KEY ([GateId]) REFERENCES [QualityGates] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE TABLE [CommitFeatureLinks] (
        [CommitId] uniqueidentifier NOT NULL,
        [FeatureId] uniqueidentifier NOT NULL,
        [Reason] nvarchar(30) NOT NULL,
        CONSTRAINT [PK_CommitFeatureLinks] PRIMARY KEY ([CommitId], [FeatureId]),
        CONSTRAINT [FK_CommitFeatureLinks_SourceCommits_CommitId] FOREIGN KEY ([CommitId]) REFERENCES [SourceCommits] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_ExecutionRuns_DefectId] ON [ExecutionRuns] ([DefectId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_ExecutionRuns_ReleaseId] ON [ExecutionRuns] ([ReleaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_ApplicationBuilds_ProductId_CreatedAt] ON [ApplicationBuilds] ([ProductId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_ApplicationBuilds_ReleaseId] ON [ApplicationBuilds] ([ReleaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_BuildFeatureImpacts_FeatureId] ON [BuildFeatureImpacts] ([FeatureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_CommitFeatureLinks_FeatureId] ON [CommitFeatureLinks] ([FeatureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_DefectEvents_DefectId_Id] ON [DefectEvents] ([DefectId], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_DefectOccurrences_TestExecutionId] ON [DefectOccurrences] ([TestExecutionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_Defects_FeatureId] ON [Defects] ([FeatureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_Defects_Fingerprint] ON [Defects] ([Fingerprint]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Defects_Number] ON [Defects] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_Defects_ProductId_Status] ON [Defects] ([ProductId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_Defects_TestCaseId] ON [Defects] ([TestCaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_EscalationPolicies_ChannelId] ON [EscalationPolicies] ([ChannelId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_GateWaivers_GateId] ON [GateWaivers] ([GateId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_GateWaivers_ReleaseId_GateId] ON [GateWaivers] ([ReleaseId], [GateId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE UNIQUE INDEX [IX_IntegrationKeys_KeyHash] ON [IntegrationKeys] ([KeyHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_NotificationDeliveries_EventId] ON [NotificationDeliveries] ([EventId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_NotificationDeliveries_Status_CreatedAt] ON [NotificationDeliveries] ([Status], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_NotificationEvents_CreatedAt] ON [NotificationEvents] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_NotificationEvents_Type_AcknowledgedAt] ON [NotificationEvents] ([Type], [AcknowledgedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_NotificationRules_ChannelId] ON [NotificationRules] ([ChannelId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_NotificationRules_UserId] ON [NotificationRules] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_QualityGates_ProductId_ReleaseId] ON [QualityGates] ([ProductId], [ReleaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_ReleaseApprovals_ReleaseId] ON [ReleaseApprovals] ([ReleaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_ReleaseFeatureChanges_FeatureId] ON [ReleaseFeatureChanges] ([FeatureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ReleaseFeatureChanges_ReleaseId_FeatureId_Kind] ON [ReleaseFeatureChanges] ([ReleaseId], [FeatureId], [Kind]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_SavedReports_Owner] ON [SavedReports] ([Owner]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_SourceCommits_BuildId] ON [SourceCommits] ([BuildId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_SourceCommits_CommitId] ON [SourceCommits] ([CommitId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SourceRepositories_Name] ON [SourceRepositories] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    CREATE INDEX [IX_UserNotifications_EventId] ON [UserNotifications] ([EventId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027083835_Quality'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261027083835_Quality', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027100520_AiAssistant'
)
BEGIN
    CREATE TABLE [AiInteractions] (
        [Id] uniqueidentifier NOT NULL,
        [Feature] nvarchar(30) NOT NULL,
        [UserName] nvarchar(256) NOT NULL,
        [Provider] nvarchar(50) NOT NULL,
        [Model] nvarchar(100) NOT NULL,
        [SubjectType] nvarchar(50) NULL,
        [SubjectId] uniqueidentifier NULL,
        [SubjectLabel] nvarchar(400) NULL,
        [Status] nvarchar(30) NOT NULL,
        [Error] nvarchar(max) NULL,
        [RedactionCount] int NOT NULL,
        [InputSha256] nvarchar(64) NOT NULL,
        [InputChars] int NOT NULL,
        [ImageCount] int NOT NULL,
        [ContextManifestJson] nvarchar(max) NULL,
        [InputTokens] bigint NULL,
        [OutputTokens] bigint NULL,
        [DurationMs] bigint NOT NULL,
        [ResultJson] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [Outcome] nvarchar(30) NULL,
        [OutcomeBy] nvarchar(256) NULL,
        [OutcomeAt] datetimeoffset NULL,
        [OutcomeNote] nvarchar(max) NULL,
        [AppliedJson] nvarchar(max) NULL,
        CONSTRAINT [PK_AiInteractions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027100520_AiAssistant'
)
BEGIN
    CREATE TABLE [AiPolicies] (
        [Id] uniqueidentifier NOT NULL,
        [IsEnabled] bit NOT NULL,
        [AllowSourceCode] bit NOT NULL,
        [AllowScreenshots] bit NOT NULL,
        [MaskEmails] bit NOT NULL,
        [MaskPhoneNumbers] bit NOT NULL,
        [AdditionalRedactionPatterns] nvarchar(max) NULL,
        [DailyRequestsPerUser] int NOT NULL,
        [ProjectConventions] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NOT NULL,
        [ModifiedAt] datetimeoffset NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_AiPolicies] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027100520_AiAssistant'
)
BEGIN
    CREATE INDEX [IX_AiInteractions_SubjectType_SubjectId] ON [AiInteractions] ([SubjectType], [SubjectId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027100520_AiAssistant'
)
BEGIN
    CREATE INDEX [IX_AiInteractions_UserName_CreatedAt] ON [AiInteractions] ([UserName], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027100520_AiAssistant'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261027100520_AiAssistant', N'10.0.14');
END;

COMMIT;
GO

