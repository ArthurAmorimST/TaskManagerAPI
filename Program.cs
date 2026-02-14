using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Threading.RateLimiting;
using TaskManagerAPI;

class Program
{
    static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddOpenApi();
        builder.Services.AddDbContext<TaskDatabase>(options => options.UseSqlite("Data source=tasks.db"));

        builder.Services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "global";

                return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromSeconds(8),
                    PermitLimit = 4,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 2
                });
            });

            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.StatusCode = 429;
                await context.HttpContext.Response.WriteAsync("Too many requests!", token);
            };
        });

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseRateLimiter();
        app.UseHttpsRedirection();

        #region Handlers
        app.MapGet("/tasks", async (TaskDatabase db, int? state) =>
        {
            if (state is null)
                return Results.Ok(await db.Tasks.ToListAsync());

            if (!Enum.IsDefined(typeof(TaskState), state))
                return Results.BadRequest("Invalid Task State.");

            var result = await db.Tasks.Where(task => (int)task.State == state).ToListAsync();

            return result.Count > 0 ? Results.Ok(result) : Results.NotFound();
        });

        app.MapGet("/tasks/{id}", async (TaskDatabase db, long id) =>
        {
            var task = await db.Tasks.FindAsync(id);

            return task is null ? Results.NotFound($"Task (Id: {id}) not found.") : Results.Ok(task);
        });

        app.MapPost("/tasks", async (TaskDatabase db, TaskRequest request) =>
        {
            if (!request.IsValid(out List<string> reasons))
                return Results.BadRequest(new { message = "Invalid TaskItem object.", reasons });

            var task = request.Create();

            await db.Tasks.AddAsync(task);
            await db.SaveChangesAsync();

            return Results.Created($"/tasks/{task.Id}", task);
        });

        app.MapDelete("/tasks/{id}", async (TaskDatabase db, long id) =>
        {
            var task = await db.Tasks.FindAsync(id);

            if (task is null)
                return Results.NotFound($"Task (Id: {id}) not found.");

            db.Tasks.Remove(task);
            await db.SaveChangesAsync();

            return Results.NoContent();
        });

        app.MapPut("/tasks/{id}", async (TaskDatabase db, long id, TaskRequest request) =>
        {
            if (!request.IsValid(out List<string> reasons))
                return Results.BadRequest(new { message = "Invalid TaskItem object.", reasons });

            var task = request.Create() with { Id = id };
            db.Tasks.Update(task);

            var rowsAffected = await db.SaveChangesAsync();

            if (rowsAffected == 0)
                return Results.NotFound($"Task (Id: {id}) not found.");

            return Results.Ok(task);
        });

        app.MapPatch("/tasks/{id}", async (TaskDatabase db, long id, JsonElement patch) =>
        {
            var task = await db.Tasks.FindAsync(id);

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

            await db.SaveChangesAsync();

            return warnings.Count > 0 ? Results.Ok(new { task, warnings }) : Results.Ok(task);
        });
        #endregion

        app.Run();
    }
}