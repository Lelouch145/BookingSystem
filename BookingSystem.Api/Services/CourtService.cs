using System.Globalization;
using System.Net.Mime;
using System.Threading.Tasks;
using BookingSystem.Api.Database;
using BookingSystem.Api.Models.ResponseModel;
using BookingSystem.Api.Models.SystemModels;
using Microsoft.Data.SqlClient;
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
    public async Task<CourtResult> CreateCourt(string courtName, string description, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courtName))
        {
            return new CourtResult
            {
                ErrorMessage = Error.InvalidCourtName
            };
        }
        courtName = NormalizeString(courtName);
        try
        {
            var duplicateControl = await _dbContext.Courts.AnyAsync(x =>
                x.CourtName == courtName, cancellationToken);
            if (duplicateControl)
            {
                return new CourtResult
                {
                    ErrorMessage = Error.CourtAlreadyExists
                };
            }
            Court newCourt = new Court
            {
                CourtName = courtName,
                Description = description,
            };

            _dbContext.Courts.Add(newCourt);
            await _dbContext.SaveChangesAsync(cancellationToken);
            CourtResult courtResult = new CourtResult
            {
                Courts = newCourt,
                ErrorMessage = Error.none
            };



            return courtResult;
        }
        catch (DbUpdateException ex)
        {
            if (ex.InnerException is SqlException sqlException && (sqlException.Number == 2601 || sqlException.Number == 2627))
            {
                return new CourtResult
                {
                    ErrorMessage = Error.CourtAlreadyExists
                };
            }
            throw;
        }


    }

    public async Task<IEnumerable<Court>> ShowCourts(CancellationToken cancellationToken)
    {
        return await _dbContext.Courts.ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Court>> ShowActiveCourts(CancellationToken cancellationToken)
    {
        var activeCourts = await _dbContext.Courts.Where(x => x.IsActive).ToListAsync(cancellationToken);

        return activeCourts;

    }

    public async Task<CourtResult> DisableCourt(string courtName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courtName))
        {
            return new CourtResult
            {
                ErrorMessage = Error.InvalidCourtName
            };
        }
        courtName = NormalizeString(courtName);
        var search = await _dbContext.Courts.FirstOrDefaultAsync(x => x.CourtName == courtName, cancellationToken);

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

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new CourtResult
        {
            ErrorMessage = Error.none
        };


    }

    public async Task<CourtResult> UpdateCourt(string findCourt, string courtName, string description, bool isActive, CancellationToken cancellationToken)
    {

        if (string.IsNullOrWhiteSpace(courtName))
        {
            return new CourtResult
            {
                Courts = null,
                ErrorMessage = Error.InvalidCourtName
            };
        }
        if (string.IsNullOrWhiteSpace(findCourt))
        {
            return new CourtResult
            {
                ErrorMessage = Error.InvalidCourtSearchName
            };
        }

        courtName = NormalizeString(courtName);
        findCourt = NormalizeString(findCourt);

        var databaseCourt = await _dbContext.Courts.FirstOrDefaultAsync(x => x.CourtName == findCourt, cancellationToken);

        if (databaseCourt == null)
        {
            return new CourtResult
            {
                ErrorMessage = Error.CouldNotFindTheCourtInDataBase
            };
        }
        try
        {
            var duplicateControl = await _dbContext.Courts.AnyAsync(x =>
            x.CourtName == courtName && x.Id != databaseCourt.Id, cancellationToken);
            if (duplicateControl)
            {
                return new CourtResult
                {
                    ErrorMessage = Error.CourtAlreadyExists
                };
            }

            databaseCourt.CourtName = courtName;
            databaseCourt.Description = description;
            databaseCourt.IsActive = isActive;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new CourtResult
            {
                Courts = databaseCourt,
                ErrorMessage = Error.none
            };
        }
        catch(DbUpdateException ex)
        {
            if (ex.InnerException is SqlException sqlException && (sqlException.Number == 2601 || sqlException.Number == 2627))
            {
                return new CourtResult
                {
                    ErrorMessage = Error.CourtAlreadyExists
                };
            }
            throw;
        }


    }
    
    private string NormalizeString(string courtName)
    {
        courtName = courtName.Trim().ToLowerInvariant();
        courtName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(courtName);
        return courtName;
    }
}