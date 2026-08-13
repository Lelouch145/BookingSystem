using BookingSystem.Api.Database;
using BookingSystem.Api.Migrations;
using BookingSystem.Api.Models;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string"
    + "'DefaultConnection' not found.");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddScoped<CourtService>();

var app = builder.Build();

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
    else if(result.ErrorMessage == Error.CourtAlreadyExists)
    {
        return Results.Conflict(result.ErrorMessage);
    }
    return Results.Ok(result);
});

app.MapGet("Courts/ShowCourts", (CourtService showCourt) =>
{
    return showCourt.ShowCourts();
});

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
});

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
    else if(result.ErrorMessage == Error.CourtAlreadyExists)
    {
        return Results.Conflict(result.ErrorMessage);
    }
    return Results.Ok();
});



app.Run();