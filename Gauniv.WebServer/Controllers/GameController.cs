using Gauniv.WebServer.Data;
using Gauniv.WebServer.Dtos;
using Gauniv.WebServer.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace Gauniv.WebServer.Controllers
{
    public class GameController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<GameController> _logger;
        private readonly UserManager<User> _userManager;

        public GameController(ILogger<GameController> logger, ApplicationDbContext db, UserManager<User> userManager)
        {
            _logger = logger;
            _db = db;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index(int? categoryId, decimal? minPrice, decimal? maxPrice, string? search, bool onlyOwned = false)
        {
            GameIndexViewModel vm = new();

            decimal GlobalMaxPrice = await _db.Games.Select(g => (decimal?)g.Price).MaxAsync() ?? 0m;

            vm.Categories = await _db.Categories
                .Select(c => new CategorySelect { Id = c.Id, Libelle = c.Libelle })
                .ToListAsync();

            IQueryable<Game> query = _db.Games.AsQueryable();

            // Appliquer le filtre de possession si demandé et si l'utilisateur est authentifié
            var user = await _userManager.GetUserAsync(User);
            bool isAuthenticated = user != null;

            // Récupérer purchasedIds si utilisateur authentifié (utilisé pour le filtre et pour le DTO)
            List<int> purchasedIds = new List<int>();
            if (isAuthenticated)
            {
                var userId = user!.Id;
                purchasedIds = await _db.Users
                    .Where(u => u.Id == userId)
                    .SelectMany(u => u.PurchasedGames.Select(pg => pg.Id))
                    .ToListAsync();
            }

            if (onlyOwned && isAuthenticated)
            {
                _logger?.LogInformation("Applying 'OnlyOwned' filter for user {UserId}", user!.Id);

                // Si aucun jeu acheté, retourner une requête vide (aucun jeu)
                if (purchasedIds.Count == 0)
                {
                    _logger?.LogInformation("User {UserId} has no purchased games.", user.Id);
                    query = query.Where(g => false);
                }
                else
                {
                    query = query.Where(g => purchasedIds.Contains(g.Id));
                }
            }
            else if (onlyOwned && !isAuthenticated)
            {
                _logger?.LogInformation("OnlyOwned requested but user not authenticated; ignoring filter.");
            }

            if (categoryId.HasValue)
            {
                query = query.Where(g => g.Categories.Any(c => c.Id == categoryId.Value));
            }

            if (minPrice.HasValue)
            {
                query = query.Where(g => g.Price >= minPrice.Value);
            }

            if (maxPrice.HasValue)
            {
                query = query.Where(g => g.Price <= maxPrice.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                string s = search.Trim();
                query = query.Where(g => EF.Functions.Like(g.Name, $"%{s}%"));
            }

            decimal overallMaxPrice = await query.Select(g => (decimal?)g.Price).MaxAsync() ?? 0m;

            // Charger les jeux avec leurs catégories
            List<Game> games = await query.Include(g => g.Categories).ToListAsync();

            // Mapper en mémoire vers GameDto
            vm.Games = games.Select(g => new GameDto
            {
                Id = g.Id,
                Name = g.Name,
                Description = g.Description,
                Price = g.Price.GetValueOrDefault(),
                Categories = g.Categories.Select(c => new CategorieDtoLight { Libelle = c.Libelle }).ToList(),
                Purchased = purchasedIds.Contains(g.Id),
                HasPayload = g.Payload != null && g.Payload.Length > 0
            }).ToList();

            // Renseigner les filtres sélectionnés
            vm.SelectedCategory = categoryId;
            vm.MinPrice = minPrice ?? 0m;

            // Arrondir à l'unité supérieure pour l'affichage (gère n'importe quel nombre de décimales)
            var displayMax = (decimal)System.Math.Ceiling((double)(maxPrice ?? overallMaxPrice));
            var displayGlobalMax = (decimal)System.Math.Ceiling((double)GlobalMaxPrice);

            vm.MaxPrice = displayMax;
            vm.Search = search;
            vm.GlobalMaxPrice = displayGlobalMax;
            
            vm.IsAuthenticated = isAuthenticated;
            vm.OnlyOwned = onlyOwned;

            return View(vm);
        }

        // Statistics page: total games and games per category
        public async Task<IActionResult> Stats()
        {
            var vm = new Gauniv.WebServer.Models.GameStatsViewModel();

            vm.TotalGames = await _db.Games.CountAsync();

            vm.GamesPerCategory = await _db.Categories
                .Select(c => new Gauniv.WebServer.Models.GameStatsViewModel.CategoryCount
                {
                    CategoryId = c.Id,
                    Libelle = c.Libelle,
                    Count = c.Games.Count()
                })
                .OrderByDescending(x => x.Count)
                .ToListAsync();

            return View("~/Views/Game/Stats.cshtml", vm);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}