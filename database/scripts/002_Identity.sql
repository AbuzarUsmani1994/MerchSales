/*
  002_Identity.sql
  Users, roles, permissions (global catalogue), role-permission and user-role links,
  refresh tokens. Passwords are hashed by the API (ASP.NET Core PasswordHasher, PBKDF2);
  the database never sees a plain password.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE ScriptName = N'002_Identity')
BEGIN
    PRINT N'002_Identity already applied - skipped.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    CREATE TABLE [identity].[User]
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_User_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        Username            nvarchar(100)    NOT NULL,   -- unique within the tenant
        DisplayName         nvarchar(200)    NOT NULL,
        Email               nvarchar(256)    NULL,
        Phone               nvarchar(40)     NULL,
        PasswordHash        nvarchar(500)    NOT NULL,
        SecurityStamp       uniqueidentifier NOT NULL CONSTRAINT DF_User_SecurityStamp DEFAULT NEWID(), -- change to invalidate all tokens
        MustChangePassword  bit              NOT NULL CONSTRAINT DF_User_MustChange DEFAULT 1,
        IsActive            bit              NOT NULL CONSTRAINT DF_User_IsActive DEFAULT 1,
        FailedLoginCount    int              NOT NULL CONSTRAINT DF_User_Failed DEFAULT 0,
        LockoutEndUtc       datetime2(3)     NULL,
        MfaEnabled          bit              NOT NULL CONSTRAINT DF_User_Mfa DEFAULT 0,
        MfaSecretProtected  nvarchar(500)    NULL,       -- encrypted by the API (Data Protection)
        PinHash             nvarchar(500)    NULL,       -- manager PIN for voids/discount overrides
        LastLoginAtUtc      datetime2(3)     NULL,
        DefaultBranchId     uniqueidentifier NULL,
        CreatedAtUtc        datetime2(3)     NOT NULL CONSTRAINT DF_User_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy           uniqueidentifier NULL,
        UpdatedAtUtc        datetime2(3)     NULL,
        UpdatedBy           uniqueidentifier NULL,
        RowVer              rowversion       NOT NULL,
        CONSTRAINT PK_User PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_User_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_User_Username UNIQUE NONCLUSTERED (TenantId, Username),
        CONSTRAINT FK_User_Tenant FOREIGN KEY (TenantId) REFERENCES tenancy.Tenant (Id),
        CONSTRAINT FK_User_DefaultBranch FOREIGN KEY (TenantId, DefaultBranchId) REFERENCES tenancy.Branch (TenantId, Id)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_User ON [identity].[User] (TenantId, ClusterKey);

    CREATE TABLE [identity].Role
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_Role_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        Name            nvarchar(100)    NOT NULL,
        Description     nvarchar(500)    NULL,
        IsSystem        bit              NOT NULL CONSTRAINT DF_Role_IsSystem DEFAULT 0, -- seeded by template, cannot be deleted
        CreatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_Role_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy       uniqueidentifier NULL,
        UpdatedAtUtc    datetime2(3)     NULL,
        UpdatedBy       uniqueidentifier NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_Role PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_Role_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_Role_Name UNIQUE NONCLUSTERED (TenantId, Name),
        CONSTRAINT FK_Role_Tenant FOREIGN KEY (TenantId) REFERENCES tenancy.Tenant (Id)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_Role ON [identity].Role (TenantId, ClusterKey);

    -- Global permission catalogue (not tenant-owned; seeded by script 011)
    CREATE TABLE [identity].Permission
    (
        Code            varchar(100)     NOT NULL,   -- e.g. 'Inventory.Adjustment.Approve'
        Module          varchar(40)      NOT NULL,
        Description     nvarchar(300)    NOT NULL,
        IsPlatformOnly  bit              NOT NULL CONSTRAINT DF_Permission_PlatformOnly DEFAULT 0,
        CONSTRAINT PK_Permission PRIMARY KEY CLUSTERED (Code)
    );

    CREATE TABLE [identity].RolePermission
    (
        TenantId        uniqueidentifier NOT NULL,
        RoleId          uniqueidentifier NOT NULL,
        PermissionCode  varchar(100)     NOT NULL,
        CONSTRAINT PK_RolePermission PRIMARY KEY CLUSTERED (TenantId, RoleId, PermissionCode),
        CONSTRAINT FK_RolePermission_Role FOREIGN KEY (TenantId, RoleId) REFERENCES [identity].Role (TenantId, Id) ON DELETE CASCADE,
        CONSTRAINT FK_RolePermission_Permission FOREIGN KEY (PermissionCode) REFERENCES [identity].Permission (Code)
    );

    -- BranchId NULL = role applies to all branches of the tenant
    CREATE TABLE [identity].UserRole
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_UserRole_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        UserId          uniqueidentifier NOT NULL,
        RoleId          uniqueidentifier NOT NULL,
        BranchId        uniqueidentifier NULL,
        CONSTRAINT PK_UserRole PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_UserRole_User FOREIGN KEY (TenantId, UserId) REFERENCES [identity].[User] (TenantId, Id),
        CONSTRAINT FK_UserRole_Role FOREIGN KEY (TenantId, RoleId) REFERENCES [identity].Role (TenantId, Id),
        CONSTRAINT FK_UserRole_Branch FOREIGN KEY (TenantId, BranchId) REFERENCES tenancy.Branch (TenantId, Id)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_UserRole ON [identity].UserRole (TenantId, ClusterKey);
    CREATE UNIQUE INDEX UX_UserRole ON [identity].UserRole (TenantId, UserId, RoleId, BranchId);

    -- Rotating refresh tokens; only the SHA-256 hash is stored
    CREATE TABLE [identity].RefreshToken
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_RefreshToken_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        UserId              uniqueidentifier NOT NULL,
        DeviceId            uniqueidentifier NULL,       -- sync.Device for mobile; NULL for web
        TokenHash           varchar(64)      NOT NULL,
        FamilyId            uniqueidentifier NOT NULL,   -- all rotations of one login; reuse of an old token revokes the family
        CreatedAtUtc        datetime2(3)     NOT NULL CONSTRAINT DF_RefreshToken_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        ExpiresAtUtc        datetime2(3)     NOT NULL,
        RevokedAtUtc        datetime2(3)     NULL,
        RevokedReason       varchar(50)      NULL,
        ReplacedByTokenId   uniqueidentifier NULL,
        CreatedByIp         varchar(45)      NULL,
        CONSTRAINT PK_RefreshToken PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UX_RefreshToken_Hash UNIQUE NONCLUSTERED (TokenHash),
        CONSTRAINT FK_RefreshToken_User FOREIGN KEY (TenantId, UserId) REFERENCES [identity].[User] (TenantId, Id)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_RefreshToken ON [identity].RefreshToken (TenantId, ClusterKey);
    CREATE INDEX IX_RefreshToken_Family ON [identity].RefreshToken (TenantId, FamilyId);

    INSERT dbo.SchemaVersion (ScriptName) VALUES (N'002_Identity');
    COMMIT TRANSACTION;
    PRINT N'002_Identity applied.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
