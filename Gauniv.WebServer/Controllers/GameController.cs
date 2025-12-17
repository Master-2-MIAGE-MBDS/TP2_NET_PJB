using Gauniv.WebServer.Data;
using Gauniv.WebServer.Dtos;
using Gauniv.WebServer.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Linq;

namespace Gauniv.WebServer.Controllers
{
    public class GameController : Controller
    {
        private readonly ILogger<GameController> _logger;
        private readonly ApplicationDbContext _db;
        private readonly UserManager<User> _userManager;

        public GameController(ILogger<GameController> logger, ApplicationDbContext db, UserManager<User> userManager)
        {
            _logger = logger;
            _db = db;
            _userManager = userManager;
        }
        
        public async Task<IActionResult> Index(int? categoryId, decimal? minPrice, decimal? maxPrice, string? search)
        {
            var vm = new GameIndexViewModel();
            
            var GlobalMaxPrice = await _db.Games.Select(g => (decimal?)g.Price).MaxAsync() ?? 0m;
            
            vm.Categories = await _db.Categories
                .Select(c => new CategorySelect { Id = c.Id, Libelle = c.Libelle })
                .ToListAsync();
            
            var query = _db.Games.AsQueryable();
            
            if (categoryId.HasValue)
                query = query.Where(g => g.Categories.Any(c => c.Id == categoryId.Value));

            if (minPrice.HasValue)
                query = query.Where(g => g.Price >= minPrice.Value);

            if (maxPrice.HasValue)
                query = query.Where(g => g.Price <= maxPrice.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(g => EF.Functions.Like(g.Name, $"%{s}%"));
            }
            
            var overallMaxPrice = await query.Select(g => (decimal?)g.Price).MaxAsync() ?? 0m;

            // Charger les jeux avec leurs catégories
            var games = await query.Include(g => g.Categories).ToListAsync();

            // Mapper en mémoire vers GameDto
            vm.Games = games.Select(g => new GameDto
            {
                Id = g.Id,
                Name = g.Name,
                Description = g.Description,
                Price = g.Price,
                Categories = g.Categories.Select(c => new CategorieDtoLight { Libelle = c.Libelle }).ToList()
            }).ToList();

            // Renseigner les filtres sélectionnés
            vm.SelectedCategory = categoryId;
            vm.MinPrice = minPrice ?? 0m;
            vm.MaxPrice = maxPrice ?? overallMaxPrice;
            vm.Search = search;
            vm.GlobalMaxPrice = GlobalMaxPrice;

            return View(vm);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}

