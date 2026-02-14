using Microsoft.EntityFrameworkCore;

namespace TaskManagerAPI
{
    public class TaskDatabase(DbContextOptions<TaskDatabase> options) : DbContext(options)
    {
        public DbSet<TaskItem> Tasks { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TaskItem>(entity =>
            {
                entity.Property(task => task.Name)
                    .HasDefaultValue(string.Empty)
                    .IsRequired();

                entity.Property(task => task.Description)
                    .HasDefaultValue(string.Empty);

                entity.Property(t => t.State)
                    .HasConversion<string>();

                entity.Property(task => task.CreatedAt)
                    .HasDefaultValueSql("CURRENT_DATE")
                    .ValueGeneratedOnAddOrUpdate();

                entity.HasIndex(t => t.State);
            });
        }
    }
}