using System.ComponentModel.DataAnnotations;

namespace TaskManagerAPI
{
    public record User
    {
        [Key]
        public long Id { get; set; }

        public required string Username { get; set; }
        public required string PasswordHash { get; set; }
    }
}
