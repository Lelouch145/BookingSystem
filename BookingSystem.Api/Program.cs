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

app.MapGet("Courts/ShowCourts", (CourtService showCourt) =>
{
    return showCourt.ShowCourts();
});

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



app.Run();