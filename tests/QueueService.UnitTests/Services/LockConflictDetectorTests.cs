using System.Reflection;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using QueueService.Services;

namespace QueueService.UnitTests.Services;

// SWC-128: only a deadlock or a lock wait timeout counts as "another call got there first".
public class LockConflictDetectorTests
{
    [Theory]
    [InlineData(MySqlErrorCode.LockDeadlock)]
    [InlineData(MySqlErrorCode.LockWaitTimeout)]
    public void DeadlockAndLockWaitTimeoutAreLockConflicts(MySqlErrorCode code)
    {
        Assert.True(LockConflictDetector.IsLockConflict(MySqlError(code)));
    }

    [Fact]
    public void LockConflictWrappedByEfCoreWhenSavingIsRecognised()
    {
        var wrapped = new DbUpdateException("save failed", MySqlError(MySqlErrorCode.LockDeadlock));

        Assert.True(LockConflictDetector.IsLockConflict(wrapped));
    }

    [Fact]
    public void LockConflictSeveralLevelsDownIsRecognised()
    {
        var wrapped = new InvalidOperationException(
            "outer",
            new DbUpdateException("save failed", MySqlError(MySqlErrorCode.LockWaitTimeout)));

        Assert.True(LockConflictDetector.IsLockConflict(wrapped));
    }

    [Theory]
    [InlineData(MySqlErrorCode.DuplicateKeyEntry)]
    [InlineData(MySqlErrorCode.UnableToConnectToHost)]
    [InlineData(MySqlErrorCode.LockTableFull)]
    public void OtherMySqlErrorsAreNotLockConflicts(MySqlErrorCode code)
    {
        Assert.False(LockConflictDetector.IsLockConflict(MySqlError(code)));
        Assert.False(LockConflictDetector.IsLockConflict(new DbUpdateException("save failed", MySqlError(code))));
    }

    [Fact]
    public void ErrorsThatAreNotFromMySqlAreNotLockConflicts()
    {
        Assert.False(LockConflictDetector.IsLockConflict(new DbUpdateException("save failed")));
        Assert.False(LockConflictDetector.IsLockConflict(new InvalidOperationException("other")));
        Assert.False(LockConflictDetector.IsLockConflict(null));
    }

    private static MySqlException MySqlError(MySqlErrorCode code) =>
        (MySqlException)Activator.CreateInstance(
            typeof(MySqlException),
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            args: [code, "Simulated MySQL error"],
            culture: null)!;
}
