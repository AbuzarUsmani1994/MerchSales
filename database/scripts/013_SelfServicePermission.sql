/*
  013_SelfServicePermission.sql
  Adds one permission code the Build Spec's seed data doesn't have: section 9 requires every
  endpoint to carry an explicit [Authorize(Policy = "<PermissionCode>")] or [AllowAnonymous],
  but logout / change-password / me / MFA enrollment / manager-PIN setup are actions every
  signed-in user needs regardless of their job permissions (Cashier, Waiter, Rider...). There
  is no existing code that fits, so this follows 011's own stated extension rule ("add a new
  numbered script that inserts only the new codes") instead of editing 011 itself.

  Every role created by tenant provisioning (section 11) is granted this code automatically;
  it is not meant to be toggled per role in the permission matrix UI.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE ScriptName = N'013_SelfServicePermission')
BEGIN
    PRINT N'013_SelfServicePermission already applied - skipped.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    INSERT [identity].Permission (Code, Module, Description, IsPlatformOnly)
    SELECT v.Code, v.Module, v.Description, v.IsPlatformOnly
    FROM (VALUES
        ('Identity.Self.Manage', 'Identity', N'Manage your own session: logout, change password, view your profile, enrol MFA, set your PIN', 0)
    ) AS v (Code, Module, Description, IsPlatformOnly)
    WHERE NOT EXISTS (SELECT 1 FROM [identity].Permission p WHERE p.Code = v.Code);

    INSERT dbo.SchemaVersion (ScriptName) VALUES (N'013_SelfServicePermission');
    COMMIT TRANSACTION;
    PRINT N'013_SelfServicePermission applied.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
