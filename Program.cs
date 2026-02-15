using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using TaskManagerAPI;

class Program
{
    static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddOpenApi();
        builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data source=AppDbContext.db"));

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

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = builder.Configuration["Jwt:Issuer"],
                    ValidAudience = builder.Configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SecurityKey"]!)),
                };
            });

        builder.Services.AddAuthorization();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseRateLimiter();
        app.UseHttpsRedirection();
        app.UseAuthentication();
        app.UseAuthorization();

        #region Handlers
        app.MapPost("/auth/register", async (AppDbContext db, JsonElement post) =>
        {
            if (!post.TryGetProperty("username", out var username))
                return Results.BadRequest("Missing 'username' property.");

            if (!post.TryGetProperty("password", out var password))
                return Results.BadRequest("Missing 'password' property");

            var usernameStr = username.GetString();

            if (string.IsNullOrEmpty(usernameStr) || usernameStr.Length < 8)
                return Results.BadRequest("Username must be at least 8 characters long.");

            var passwordStr = password.GetString();

            if (string.IsNullOrEmpty(passwordStr) || passwordStr.Length < 8)
                return Results.BadRequest("Password must be at least 8 characters long.");

            var existingUser = await db.Users.FirstOrDefaultAsync(u => u.Username == usernameStr);

            if (existingUser is not null)
                return Results.Conflict("Username already taken.");

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(passwordStr);

            var user = new User
            {
                Username = usernameStr,
                PasswordHash = passwordHash
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            return Results.Ok(new { message = "User registered succesfully", userId = user.Id } );
        });

        app.MapPost("/auth/login", async (AppDbContext db, JsonElement post) =>
        {
            if (!post.TryGetProperty("username", out var username))
                return Results.BadRequest("Missing 'username' property.");

            if (!post.TryGetProperty("password", out var password))
                return Results.BadRequest("Missing 'password' property");

            var usernameStr = username.GetString();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == usernameStr);

            if (user is null)
                return Results.Unauthorized();

            var passwordStr = password.GetString();

            if(!BCrypt.Net.BCrypt.Verify(passwordStr, user.PasswordHash))
                return Results.Unauthorized();

            var claims = new[]
            {
                new Claim("userId", user.Id.ToString())
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SecurityKey"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var expirationHours = double.Parse(builder.Configuration["Jwt:ExpirationHours"]!);

            var token = new JwtSecurityToken(
                issuer: builder.Configuration["Jwt:Issuer"]!,
                audience: builder.Configuration["Jwt:Audience"]!,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(expirationHours),
                signingCredentials: creds
            );

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

            return Results.Ok(new { token = tokenString });
        });

        app.MapGet("/tasks", async (HttpContext context, AppDbContext db, int? state) =>
        {
            var userId = long.Parse(context.User.FindFirst("userId")!.Value);

            var query = db.Tasks.Where(task => task.UserId == userId);

            if(state is not null)
            {
                if (!Enum.IsDefined(typeof(TaskState), state))
                    return Results.BadRequest("Invalid TaskState.");

                query = query.Where(task => (int)task.State == state);
            }

            var result = await query.ToListAsync();
            return Results.Ok(result);
        }).RequireAuthorization();

        app.MapGet("/tasks/{id}", async (HttpContext context, AppDbContext db, long id) =>
        {
            var userId = long.Parse(context.User.FindFirst("userId")!.Value);

            var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

            return task is null ? Results.NotFound($"Task (Id: {id}) not found.") : Results.Ok(task);
        }).RequireAuthorization();

        app.MapPost("/tasks", async (HttpContext context, AppDbContext db, TaskRequest request) =>
        {
            if (!request.IsValid(out List<string> reasons))
                return Results.BadRequest(new { message = "Invalid TaskItem object.", reasons });

            var userId = long.Parse(context.User.FindFirst("userId")!.Value);
            var task = request.Create(userId);

            await db.Tasks.AddAsync(task);
            await db.SaveChangesAsync();

            return Results.Created($"/tasks/{task.Id}", task);
        }).RequireAuthorization();

        app.MapDelete("/tasks/{id}", async (HttpContext context, AppDbContext db, long id) =>
        {
            var userId = long.Parse(context.User.FindFirst("userId")!.Value);
            var task = await db.Tasks.FindAsync(id);

            if (task is null)
                return Results.NotFound($"Task (Id: {id}) not found.");

            if (task.UserId != userId)
                return Results.Unauthorized();

            db.Tasks.Remove(task);
            await db.SaveChangesAsync();

            return Results.NoContent();
        }).RequireAuthorization();

        app.MapPut("/tasks/{id}", async (HttpContext context, AppDbContext db, long id, TaskRequest request) =>
        {
            if (!request.IsValid(out List<string> reasons))
                return Results.BadRequest(new { message = "Invalid TaskItem object.", reasons });

            var task = await db.Tasks.FindAsync(id);

            if (task is null)
                return Results.NotFound($"Task (Id: {id}) not found.");

            var userId = long.Parse(context.User.FindFirst("userId")!.Value);

            if (task.UserId != userId)
                return Results.Unauthorized();

            task = request.Create(userId) with { Id = id };
            db.Tasks.Update(task);

            await db.SaveChangesAsync();

            return Results.Ok(task);
        }).RequireAuthorization();

        app.MapPatch("/tasks/{id}", async (HttpContext context, AppDbContext db, long id, JsonElement patch) =>
        {
            var task = await db.Tasks.FindAsync(id);

            if (task is null)
                return Results.NotFound($"Task (Id: {id}) not found.");

            var userId = long.Parse(context.User.FindFirst("userId")!.Value);

            if (task.UserId != userId)
                return Results.Unauthorized();

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
        }).RequireAuthorization();
        #endregion

        app.Run();
    }
}