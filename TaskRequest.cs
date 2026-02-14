namespace TaskManagerAPI
{
    public record TaskRequest(string Name, string Description, TaskState State, DateTime DueDate)
    {
        public TaskItem Create() => new TaskItem
        {
            Name = Name ?? "New Task",
            Description = Description ?? "",
            State = State,
            DueDate = DueDate
        };

        public bool IsValid(out List<string> reasons)
        {
            reasons = new List<string>();

            if (string.IsNullOrEmpty(Name))
                reasons.Add("'Name' parameter is Null or Empty.");

            if (!Enum.IsDefined(State))
                reasons.Add("'State' parameter is not valid.");

            return reasons.Count == 0;
        }
    }
}