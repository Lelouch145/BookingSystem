using System.Globalization;
using System.Net.Mime;
using System.Threading.Tasks;
using BookingSystem.Api.Database;
using BookingSystem.Api.Models.ResponseModel;
using BookingSystem.Api.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update.Internal;

namespace BookingSystem.Api.Services;

public class CourtService
{
    private readonly AppDbContext _dbContext;

    public CourtService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    public async Task<CourtResult> CreateCourt(string courtName, string description)
    {
        courtName = courtName.Trim().ToLowerInvariant();
        courtName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(courtName);
        if (string.IsNullOrWhiteSpace(courtName))
        {
            return new CourtResult
            {
                ErrorMessage = Error.InvalidCourtName
            };
        }
        var duplicateControl = await _dbContext.Courts.FirstOrDefaultAsync(x =>
            x.CourtName == courtName);
        if(duplicateControl != null)
        {
            return new CourtResult
            {
                Courts = null,
                ErrorMessage = Error.CourtAlreadyExists
            };
        }
        Court newCourt = new Court
        {
            CourtName = courtName,
            Description = description,
        };

        _dbContext.Courts.Add(newCourt);
        await _dbContext.SaveChangesAsync();
        CourtResult courtResult = new CourtResult
        {
            Courts = newCourt,
            ErrorMessage = Error.none
        };

        return courtResult;

    }

    public async Task<IEnumerable<Court>> ShowCourts()
    {
        return await _dbContext.Courts.ToListAsync();
    }

    public async Task<CourtResult> DisableCourt(string courtName)
    {
        courtName = courtName.Trim().ToLowerInvariant();
        courtName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(courtName);
        var search = await _dbContext.Courts.FirstOrDefaultAsync(x => x.CourtName == courtName);

        if (search == null)
        {
            return new CourtResult
            {
                ErrorMessage = Error.CouldNotFindTheCourtInDataBase
            };
        }
        if(search.IsActive == false)
        {
            return new CourtResult
            {
                Courts = null,
                ErrorMessage = Error.CourtIsAlreadyInActive
            };
        }
        search.IsActive = false;

        await _dbContext.SaveChangesAsync();

        return new CourtResult
        {
            Courts = null,
            ErrorMessage = Error.none
        };


    }

    public async Task<CourtResult> UpdateCourt(string findCourt, string courtName, string description, bool isActive)
    {
        courtName = courtName.Trim().ToLowerInvariant();
        courtName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(courtName);
        findCourt = findCourt.Trim().ToLowerInvariant();
        findCourt = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(findCourt);

        if (string.IsNullOrWhiteSpace(courtName))
        {
            return new CourtResult
            {
                Courts = null,
                ErrorMessage = Error.InvalidCourtName
            };
        }
        else if (string.IsNullOrWhiteSpace(findCourt))
        {
            return new CourtResult
            {
                Courts = null,
                ErrorMessage = Error.InvalidCourtSearchName
            };
        }
        var databaseCourt = await _dbContext.Courts.FirstOrDefaultAsync(x => x.CourtName == findCourt);

        if (databaseCourt == null)
        {
            return new CourtResult
            {
                Courts = null,
                ErrorMessage = Error.CouldNotFindTheCourtInDataBase
            };
        }
        var duplicateControl = await _dbContext.Courts.FirstOrDefaultAsync(x =>
        x.CourtName == courtName && x.Id != databaseCourt.Id);
        if(duplicateControl != null)
        {
            return new CourtResult
            {
                Courts = null,
                ErrorMessage = Error.CourtAlreadyExists
            };
        }

        databaseCourt.CourtName = courtName;
        databaseCourt.Description = description;
        databaseCourt.IsActive = isActive;
        await _dbContext.SaveChangesAsync();

        return new CourtResult
        {
            Courts = databaseCourt,
            ErrorMessage = Error.none
        };

    }
}