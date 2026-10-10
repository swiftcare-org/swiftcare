using MySqlConnector;

namespace QueueService.Services;

public static class LockConflictDetector
{
    // True when MySQL gave up on a transaction because another one held the rows it needed:
    // a deadlock (InnoDB rolls one transaction back) or a lock wait that timed out. Both mean
    // "try again", not "something is broken". EF Core wraps the MySQL error in its own
    // exceptions when saving, so the whole chain of inner exceptions is checked.
    public static bool IsLockConflict(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is MySqlException { ErrorCode: MySqlErrorCode.LockDeadlock or MySqlErrorCode.LockWaitTimeout })
            {
                return true;
            }
        }

        return false;
    }
}
