using Shouldly;
using StockOrder.Modules.Identity.Domain;
using Xunit;

namespace StockOrder.UnitTests;

/// <summary>Section 10: 5 failed logins locks the account for 15 minutes.</summary>
public sealed class UserLockoutTests
{
    [Fact]
    public void User_locks_out_after_five_failed_logins()
    {
        var now = DateTime.UtcNow;
        var user = User.Create(Guid.NewGuid(), Guid.NewGuid(), "alice", "Alice", null, null, "hash", null, now, null);

        for (var i = 0; i < 4; i++)
        {
            user.RecordFailedLogin(now);
            user.IsLockedOut(now).ShouldBeFalse();
        }

        user.RecordFailedLogin(now);

        user.IsLockedOut(now).ShouldBeTrue();
        user.IsLockedOut(now.AddMinutes(16)).ShouldBeFalse();
    }

    [Fact]
    public void Successful_login_resets_the_failed_count()
    {
        var now = DateTime.UtcNow;
        var user = User.Create(Guid.NewGuid(), Guid.NewGuid(), "alice", "Alice", null, null, "hash", null, now, null);

        user.RecordFailedLogin(now);
        user.RecordFailedLogin(now);
        user.RecordSuccessfulLogin(now);

        user.FailedLoginCount.ShouldBe(0);
        user.IsLockedOut(now).ShouldBeFalse();
    }

    [Fact]
    public void Changing_password_rotates_the_security_stamp()
    {
        var now = DateTime.UtcNow;
        var user = User.Create(Guid.NewGuid(), Guid.NewGuid(), "alice", "Alice", null, null, "hash", null, now, null);
        var originalStamp = user.SecurityStamp;

        user.ChangePassword("newHash", now, null);

        user.SecurityStamp.ShouldNotBe(originalStamp);
        user.MustChangePassword.ShouldBeFalse();
    }
}
