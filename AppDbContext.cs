using Microsoft.EntityFrameworkCore;

namespace TaskManagerAPI
{
    public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        public DbSet<User> Users { get; set; }
        public DbSet<TaskItem> Tasks { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>(entity =>
            {
                entity.Property(u => u.Username)
                    .HasMaxLength(16)
                    .IsRequired();

                entity.HasIndex(u => u.Username)
                    .IsUnique();
            });

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

                entity.HasIndex(t => t.UserId);
                entity.HasIndex(t => t.State);
            });
        }
    }
}