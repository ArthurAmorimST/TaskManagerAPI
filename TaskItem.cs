using System.ComponentModel.DataAnnotations;

namespace TaskManagerAPI
{
    public enum TaskState
    {
        NotStarted,
        InProgress,
        Completed,
        OnHold
    }

    public record TaskItem
    {
        [Key]
        public long Id { get; set; }
        public long UserId { get; set; }

        public string Name { get; set; }
        public string Description { get; set; }

        public TaskState State { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime DueDate { get; set; }

        public TaskItem()
        {
            Name = string.Empty;
            Description = string.Empty;
        }
    }
}