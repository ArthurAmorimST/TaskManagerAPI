using Microsoft.EntityFrameworkCore;

namespace TaskManagerAPI
{
    public class TaskDatabase(DbContextOptions<TaskDatabase> options) : DbContext(options)
    {
        public DbSet<TaskItem> Tasks { get; set; }
    }
}