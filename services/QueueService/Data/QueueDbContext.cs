using Microsoft.EntityFrameworkCore;

namespace QueueService.Data;

// DbSets and entity configuration land with the queue data model (SWC-19 Stage 1) - this
// scaffold stage only wires up the connection so Program.cs and the maintenance runner
// have a concrete DbContext to depend on.
public sealed class QueueDbContext(DbContextOptions<QueueDbContext> options) : DbContext(options);
