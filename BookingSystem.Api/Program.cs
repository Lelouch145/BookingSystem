using BookingSystem.Api.Database;
using BookingSystem.Api.Migrations;
using BookingSystem.Api.Models;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Expressions;
using BookingSystem.Api.Models.ResponseModel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.OpenApi.Models;
using System.Runtime.InteropServices.Marshalling;
using System.Security.Claims;
using System.Text.Json.Serialization;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header

    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
});
});
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string"
    + "'DefaultConnection' not found.");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
.AddEntityFrameworkStores<AppDbContext>();

builder.Services.Configure<IdentityOptions>(options =>
{
    options.User.RequireUniqueEmail = true;
});
builder.Services.AddScoped<CourtService>();
builder.Services.AddScoped<RegisterService>();
builder.Services.AddScoped<LoginService>();
builder.Services.AddScoped<JWTService>();
builder.Services.AddScoped<SeedService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<BookingCompletionService>();
builder.Services.AddHostedService<BookingCompletionBackgroundService>();
var jwtKey = builder.Configuration["JWT:KEY"]
    ?? throw new InvalidOperationException("JWT key is missing");

var issuer = builder.Configuration["JWT:Issuer"]
    ?? throw new InvalidOperationException("JWT issuer is missing");

var audience = builder.Configuration["JWT:Audience"]
    ?? throw new InvalidOperationException("JWT audience is missing");

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = issuer,
            ValidAudience = audience,

            IssuerSigningKey =
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy =>
        policy.RequireRole("Admin"));
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
var app = builder.Build();

await using var scope = app.Services.CreateAsyncScope();

    var seedService = scope.ServiceProvider
        .GetRequiredService<SeedService>();

    await seedService.SeedRoleAsync();

app.UseAuthentication();
app.UseAuthorization();
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapPost("Courts/{courtName}/CreateCourt", async (CourtService court, string courtName, string description) =>
{
    var result = await court.CreateCourt(courtName, description);

    if (result.ErrorMessage == Error.InvalidCourtName)
    {
        return Results.BadRequest(result.ErrorMessage);
    }
    else if (result.ErrorMessage == Error.CourtAlreadyExists)
    {
        return Results.Conflict(result.ErrorMessage);
    }
    return Results.Ok(result);
}).RequireAuthorization("Admin");

app.MapGet("Courts/ShowCourts", (CourtService showCourt, ClaimsPrincipal user) =>
{
    var isAdmin = user.IsInRole("Admin");

    if (isAdmin)
    {
        return showCourt.ShowCourts();
    }
    return showCourt.ShowActiveCourts();


}).RequireAuthorization();

app.MapGet("/test-auth", () =>
{
    return Results.Ok("Du är authat");
})
.RequireAuthorization();


app.MapPatch("Courts/{courtName}/disable", async (CourtService disableCourt, string courtName) =>
{


    var result = await disableCourt.DisableCourt(courtName);

    if (result.ErrorMessage == Error.CouldNotFindTheCourtInDataBase)
    {
        return Results.NotFound(result.ErrorMessage);
    }
    else if(result.ErrorMessage == Error.CourtIsAlreadyInActive)
    {
        return Results.Conflict(result.ErrorMessage);
    }
    return Results.Ok(result);
}).RequireAuthorization("Admin");

app.MapPut("Courts/{courtName}/update", async (CourtService courtUpdate, string courtName, string description, bool IsActive, string findCourt) =>
{


    var result = await courtUpdate.UpdateCourt(findCourt, courtName, description, IsActive);
    if (result.ErrorMessage == Error.InvalidCourtName)
    {
        return Results.BadRequest(result.ErrorMessage);
    }
    else if (result.ErrorMessage == Error.CouldNotFindTheCourtInDataBase)
    {
        return Results.NotFound(result.ErrorMessage);
    }
    else if (result.ErrorMessage == Error.CourtAlreadyExists)
    {
        return Results.Conflict(result.ErrorMessage);
    }
    return Results.Ok();
}).RequireAuthorization("Admin");

app.MapPost("Register", async (RegisterService services, RegisterRequest request) =>
{

    var result = await services.Register(request.Email, request.UserName, request.Password);

    if (result.Success)
    {
        return Results.Ok(result);
    }
    else
    {
        return Results.BadRequest(result);
    }
});

app.MapPost("Login", async (LoginService services, LoginRequest request) =>
{
    var result = await services.LoginAuthentication(request.Email, request.Password);

    if (result.Success)
    {
        return Results.Ok(result);
    }
    else
    {
        return Results.BadRequest(result);
    }
});

app.MapPost("Booking{courtId}/createBooking", async (BookingService booking, int courtId, DateTime startTime, int duration, ClaimsPrincipal user) =>
{

    var userIdClaim = user.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier);

    if (userIdClaim == null)
    {
        return Results.Unauthorized();
    }
    string userId = userIdClaim.Value;

    var result = await booking.CreateBooking(courtId, startTime, duration, userId);

    if (result.ErrorMessage == Error.CouldNotFindTheCourtInDataBase)
    {
        return Results.NotFound(result.ErrorMessage);
    }
    else if (result.ErrorMessage == Error.CourtIsNotAvailable)
    {
        return Results.BadRequest(result.ErrorMessage);
    }
    else if (result.ErrorMessage == Error.BookingTimeIsOverlappingWithAnotherBooking)
    {
        return Results.BadRequest(result.ErrorMessage);
    }
    else if (result.ErrorMessage == Error.InvalidDuration)
    {
        return Results.BadRequest(result.ErrorMessage);
    }
    else if (result.ErrorMessage == Error.BookingCannotBeInThePast)
    {
        return Results.BadRequest(result.ErrorMessage);
    }
    return Results.Ok(result);



}).RequireAuthorization();

app.MapGet("Booking", async (ClaimsPrincipal user, BookingService booking) =>
{
    var isAdmin = user.IsInRole("Admin");

    if (isAdmin)
    {
        var result = await booking.GetAllBookings();
        return Results.Ok(result);
    }
    var userIdClaim = user.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier);

    if (userIdClaim == null)
    {
        return Results.Unauthorized();
    }
    var userId = userIdClaim.Value;

    var resultUser = await booking.GetUserBookings(userId);
    return Results.Ok(resultUser);
}).RequireAuthorization();

app.MapPatch("Booking/Cancel", async (ClaimsPrincipal user, BookingService booking, int bookingId) =>
{
    var isAdmin = user.IsInRole("Admin");

    if (isAdmin)
    {
        var result = await booking.CancelAdminBooking(bookingId);
        if (result.ErrorMessage == Error.BookingNotFound)
        {
            return Results.NotFound(result.ErrorMessage);
        }
        else if (result.ErrorMessage == Error.BookingIsAlreadyCancelled)
        {
            return Results.BadRequest(result.ErrorMessage);
        }
        else if (result.ErrorMessage == Error.BookingIsCompletedAndCannotBeCancelled)
        {
            return Results.BadRequest(result.ErrorMessage);
        }
        return Results.Ok(result);
    }
    var userIdClaim = user.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier);

    if (userIdClaim == null)
    {
        return Results.Unauthorized();
    }
    var userId = userIdClaim.Value;

    var resultUser = await booking.CancelUserBooking(userId, bookingId);

    if (resultUser.ErrorMessage == Error.BookingNotFound)
    {
        return Results.NotFound(resultUser.ErrorMessage);
    }
    else if (resultUser.ErrorMessage == Error.BookingIsAlreadyCancelled)
    {
        return Results.BadRequest(resultUser.ErrorMessage);
    }
    else if (resultUser.ErrorMessage == Error.BookingIsCompletedAndCannotBeCancelled)
    {
        return Results.BadRequest(resultUser.ErrorMessage);
    }
    else if (resultUser.ErrorMessage == Error.CannotCancelBooking24HoursBeforeTheBooking)
    {
        return Results.BadRequest(resultUser.ErrorMessage);
    }
    else if(resultUser.ErrorMessage == Error.BookingWasModifiedByAnotherRequest)
    {
        return Results.Conflict(resultUser);
    }
    return Results.Ok(resultUser);
});

app.MapPatch("Booking/UpdateTime", async (ClaimsPrincipal user, BookingService booking, int bookingId, int duration, DateTime newStartTime) =>
{
    var userIdClaim = user.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier);
    if (userIdClaim == null)
    {
        return Results.Unauthorized();
    }

    var userId = userIdClaim.Value;
    var isAdmin = user.IsInRole("Admin");

    var result = await booking.RescheduleBooking(userId, bookingId, duration, newStartTime, isAdmin);

    if (result.ErrorMessage == Error.InvalidDuration)
    {
        return Results.BadRequest(result.ErrorMessage);
    }
    else if (result.ErrorMessage == Error.BookingIsCancelledAndCannotBeChanged)
    {
        return Results.Conflict(result.ErrorMessage);
    }
    else if (result.ErrorMessage == Error.BookingIsCompletedAndCannotBeChanged)
    {
        return Results.Conflict(result.ErrorMessage);
    }
    else if (result.ErrorMessage == Error.BookingTimeIsNotAvailable)
    {
        return Results.BadRequest(result.ErrorMessage);
    }
    else if (result.ErrorMessage == Error.BookingTimeIsOverlappingWithAnotherBooking)
    {
        return Results.Conflict(result.ErrorMessage);
    }
    else if (result.ErrorMessage == Error.BookingNotFound)
    {
        return Results.NotFound(result.ErrorMessage);
    }
    else if (result.ErrorMessage == Error.UserDoesNotOwnBooking)
    {
        return Results.Forbid();
    }
    else if (result.ErrorMessage == Error.BookingCannotBeInThePast)
    {
        return Results.BadRequest(result.ErrorMessage);
    }
    else if(result.ErrorMessage == Error.BookingWasModifiedByAnotherRequest)
    {
        return Results.Conflict(result.ErrorMessage);
    }
    return Results.Ok(result);
}).RequireAuthorization();




app.Run();