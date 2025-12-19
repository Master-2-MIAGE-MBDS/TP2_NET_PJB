using System;
using System.Threading;
using System.Threading.Tasks;
using Gauniv.WebServer.Data;
using Microsoft.Extensions.Logging;

namespace Gauniv.WebServer.Services
{
    /// <summary>
    /// Aggregator seeder that runs category seeder then games seeder in order.
    /// Keeps persistence logic separated in their own classes; Program.cs will call this single method.
    /// </summary>
    public static class AddDataToDB_All
    {
        public static async Task SeedAsync(ApplicationDbContext db, string categoriesJsonPath, string gamesJsonPath, ILogger? logger = null, CancellationToken cancellationToken = default)
        {
            if (db is null) throw new ArgumentNullException(nameof(db));

            // Seed categories first (ensures categories exist before attaching to games)
            try
            {
                await AddDataToDB_Categorie.SeedAsync(db, categoriesJsonPath, logger, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "An error occurred while seeding categories from {path}", categoriesJsonPath);
                throw;
            }

            // Then seed games (they will attach to existing categories)
            try
            {
                await AddDataToDB_Games.SeedAsync(db, gamesJsonPath, logger, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "An error occurred while seeding games from {path}", gamesJsonPath);
                throw;
            }
        }
    }
}

