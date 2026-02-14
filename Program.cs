using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TaskManagerAPI;

class Program
{
    static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddOpenApi();
        builder.Services.AddDbContext<TaskDatabase>(options => options.UseSqlite("Data source=tasks.db"));

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseHttpsRedirection();

        #region Handlers
        app.MapGet("/tasks", (TaskDatabase db, int ? state) =>
        {
            if (state is null)
                return Results.Ok(db.Tasks.ToList());

            if (!Enum.IsDefined(typeof(TaskState), state))
                return Results.BadRequest("Invalid Task State.");

            var result = db.Tasks.Where(task => (int)task.State == state).ToList();

            return result.Count > 0 ? Results.Ok(result) : Results.NotFound();
        });

        app.MapGet("/tasks/{id}", (TaskDatabase db, long id) =>
        {
            var task = db.Tasks.Find(id);

            return task is null ? Results.NotFound($"Task (Id: {id}) not found.") : Results.Ok(task);
        });

        app.MapPost("/tasks", (TaskDatabase db, TaskRequest request) =>
        {
            var task = request.Task;

            db.Tasks.Add(task);
            db.SaveChanges();

            return Results.Created($"/tasks/{task.Id}", task);
        });

        app.MapDelete("/tasks/{id}", (TaskDatabase db, long id) =>
        {
            var task = db.Tasks.Find(id);

            if (task is null)
                return Results.NotFound($"Task (Id: {id}) not found.");

            db.Tasks.Remove(task);
            db.SaveChanges();

            return Results.NoContent();
        });

        app.MapPut("/tasks/{id}", (TaskDatabase db, long id, TaskRequest request) =>
        {
            if (!request.IsValid(out List<string> reasons))
                return Results.BadRequest(new { message = "Invalid TaskItem object.", reasons });

            var task = db.Tasks.Find(id);

            if (task is null)
                return Results.NotFound($"Task (Id: {id}) not found.");

            var newTask = request.Task;
            newTask.Id = id;

            db.Entry(task).CurrentValues.SetValues(newTask);

            db.SaveChanges();

            return Results.Ok(task);
        });

        app.MapPatch("/tasks/{id}", (TaskDatabase db, long id, JsonElement patch) =>
        {
            var task = db.Tasks.Find(id);

            if (task is null)
                return Results.NotFound($"Task (Id: {id}) not found.");

            var warnings = new List<string>();

            if (patch.TryGetProperty("name", out var name))
            {
                var stringValue = name.GetString();

                if (!string.IsNullOrEmpty(stringValue))
                    task.Name = stringValue;
                else
                    warnings.Add("'Name' was not patched (null or empty).");
            }

            if (patch.TryGetProperty("description", out var description))
            {
                var stringValue = description.GetString();

                if(stringValue != null)
                    task.Description = stringValue;
            }

            if (patch.TryGetProperty("state", out var state))
            {
                if (state.TryGetInt32(out var stateVal) && Enum.IsDefined(typeof(TaskState), stateVal))
                    task.State = (TaskState)stateVal;
                else
                    warnings.Add("'State' was not patched (invalid TaskState value).");
            }

            if (patch.TryGetProperty("dueDate", out var dueDate))
            {
                if(dueDate.TryGetDateTime(out var dateTimeValue))
                    task.DueDate = dateTimeValue.Date;
                else
                    warnings.Add("'DueDate' was not patched (invalid DateTime value).");
            }

            db.SaveChanges();

            return warnings.Count > 0 ? Results.Ok(new { task, warnings }) : Results.Ok(task);
        });
        #endregion

        app.Run();
    }
}