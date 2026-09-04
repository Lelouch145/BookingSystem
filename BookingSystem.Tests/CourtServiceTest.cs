using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using System.Globalization;

namespace BookingSystem.Tests;

public class CourtServiceTests
{
    [Fact]
    public async Task CreateCourtTest()
    {
        var dbContextService = new DbContextHelper();
        var dbContext = dbContextService.DbContextHellper();


        var courtService = new CourtService(dbContext);
        var courtName = $"Court-{Guid.NewGuid()}";

        var normalizedCourtName = CultureInfo.InvariantCulture.TextInfo
            .ToTitleCase(courtName.Trim().ToLowerInvariant());

        var result = await courtService.CreateCourt(courtName, "", CancellationToken.None);

        Assert.NotNull(result.Courts);
        Assert.Equal(Error.none, result.ErrorMessage);
        Assert.Equal(normalizedCourtName, result.Courts.CourtName);


    }
    [Fact]
    public async Task CreateCourtTestInvalidcourtName()
    {
        var dbContextService = new DbContextHelper();
        var dbContext = dbContextService.DbContextHellper();

        var courtService = new CourtService(dbContext);

        var result = await courtService.CreateCourt("", "", CancellationToken.None);

        Assert.Equal(Error.InvalidCourtName, result.ErrorMessage);


    }
    [Fact]
    public async Task CreateCourtDuplicateTest()
    {
        var dbContextService = new DbContextHelper();
        var dbContext = dbContextService.DbContextHellper();

        var courtService = new CourtService(dbContext);

        var courtName = $"Court-{Guid.NewGuid()}";
        var normalizedCourtName = CultureInfo.InvariantCulture.TextInfo
            .ToTitleCase(courtName.Trim().ToLowerInvariant());

        Court newCourt = new Court
        {
            CourtName = courtName,
            Description = "",
            IsActive = true,
        };
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();
        var result = await courtService.CreateCourt(courtName, "", CancellationToken.None);

        Assert.Equal(Error.CourtAlreadyExists, result.ErrorMessage);

    }
    [Fact]
    public async Task DisableCourtTest()
    {
        var dbContextServices = new DbContextHelper();
        var dbContext = dbContextServices.DbContextHellper();

        var courtName = $"Court-{Guid.NewGuid()}";
        var normalizedCourtName = CultureInfo.InvariantCulture.TextInfo
            .ToTitleCase(courtName.Trim().ToLowerInvariant());

        Court newCourt = new Court
        {
            CourtName = courtName,
            Description = "",
            IsActive = true,
        };
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var courtService = new CourtService(dbContext);

        var result = await courtService.DisableCourt(normalizedCourtName, CancellationToken.None);
        var updateCourt = await dbContext.Courts.FirstAsync(x => x.Id == newCourt.Id);
        Assert.NotNull(result.Courts);
        Assert.Equal(Error.none, result.ErrorMessage);
        Assert.False(updateCourt.IsActive);
    }
    [Fact]
    public async Task DisableCourtSearchNullTest()
    {
        var dbContextServices = new DbContextHelper();
        var dbContext = dbContextServices.DbContextHellper();

        var courtName = $"Court-{Guid.NewGuid()}";
        var normalizedCourtName = CultureInfo.InvariantCulture.TextInfo
            .ToTitleCase(courtName.Trim().ToLowerInvariant());

        Court newCourt = new Court
        {
            CourtName = courtName,
            Description = "",
            IsActive = true,
        };
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();


        var courtService = new CourtService(dbContext);
        var result = await courtService.DisableCourt("Random", CancellationToken.None);

        Assert.Equal(Error.CouldNotFindTheCourtInDataBase, result.ErrorMessage);
    }
    [Fact]
    public async Task UpdateCourtTest()
    {
        var dbContextServices = new DbContextHelper();
        var dbContext = dbContextServices.DbContextHellper();

        var courtName = $"Court-{Guid.NewGuid()}";
        var normalizedCourtName = CultureInfo.InvariantCulture.TextInfo
            .ToTitleCase(courtName.Trim().ToLowerInvariant());

        var newCourtName = $"Court-{Guid.NewGuid()}";
        var newNormalizedCourtName = CultureInfo.InvariantCulture.TextInfo
            .ToTitleCase(newCourtName.Trim().ToLowerInvariant());
        

        Court newCourt = new Court
        {
            CourtName = courtName,
            Description = "",
        };
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var courtService = new CourtService(dbContext);
        var result = await courtService.UpdateCourt(newCourt.CourtName, newCourtName, "", true, CancellationToken.None);

        var databaseCourtName = await dbContext.Courts.FirstAsync(x => x.Id == newCourt.Id);
        Assert.NotNull(result.Courts);
        Assert.Equal(Error.none, result.ErrorMessage);
        Assert.Equal(newNormalizedCourtName, result.Courts.CourtName);

    }


}