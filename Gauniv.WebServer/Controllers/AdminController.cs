using Gauniv.WebServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gauniv.WebServer.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _db;

        public AdminController(ApplicationDbContext db)
        {
            _db = db;
        }

        // Index with left menu; ?section=games|categories
        public async Task<IActionResult> Index(string? section)
        {
            var sec = (section ?? "games").ToLowerInvariant();
            if (sec == "categories")
            {
                var cats = await _db.Categories.OrderBy(c => c.Libelle).ToListAsync();
                ViewData["Section"] = "categories";
                return View("Index", cats);
            }
            else
            {
                var games = await _db.Games.Include(g => g.Categories).OrderBy(g => g.Name).ToListAsync();
                ViewData["Section"] = "games";
                return View("Index", games);
            }
        }

        // Categories CRUD
        public IActionResult CreateCategory() => View(new Categorie());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCategory(Categorie model)
        {
            if (!ModelState.IsValid) return View(model);
            _db.Categories.Add(model);
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Index), new { section = "categories" });
        }

        public async Task<IActionResult> EditCategory(int id)
        {
            var cat = await _db.Categories.FindAsync(id);
            if (cat == null) return NotFound();
            return View(cat);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCategory(int id, Categorie model)
        {
            if (id != model.Id) return BadRequest();
            if (!ModelState.IsValid) return View(model);
            _db.Entry(model).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Index), new { section = "categories" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCategory(int id)
        {
            // Charger la catégorie
            var cat = await _db.Categories.FindAsync(id);
            if (cat == null) return NotFound();

            // Dissocier la catégorie de tous les jeux qui l'utilisent (sans supprimer les jeux)
            var gamesWithCat = await _db.Games
                .Include(g => g.Categories)
                .Where(g => g.Categories.Any(c => c.Id == id))
                .ToListAsync();

            try
            {
                foreach (var g in gamesWithCat)
                {
                    var toRemove = g.Categories.FirstOrDefault(c => c.Id == id);
                    if (toRemove != null)
                    {
                        g.Categories.Remove(toRemove);
                    }
                }

                // Supprimer ensuite la catégorie
                _db.Categories.Remove(cat);

                await _db.SaveChangesAsync();
            }
            catch
            {
                // In case of error, rethrow to be handled by the framework (no transaction support for InMemory)
                throw;
            }

            // If request comes from AJAX (fetch), return 200 OK so client can remove the row without redirect
            var isAjax = Request.Headers != null && Request.Headers["X-Requested-With"] == "XMLHttpRequest";
            if (isAjax)
            {
                return Ok(new { message = "Catégorie supprimée." });
            }

            return RedirectToAction(nameof(Index), new { section = "categories" });
        }

        // Games CRUD (simple: name, description, price, categories selection)
        public async Task<IActionResult> CreateGame()
        {
            ViewData["AllCategories"] = await _db.Categories.OrderBy(c => c.Libelle).ToListAsync();
            return View(new Game());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateGame(Game model, int[]? selectedCategories)
        {
            if (!ModelState.IsValid)
            {
                ViewData["AllCategories"] = await _db.Categories.OrderBy(c => c.Libelle).ToListAsync();
                return View(model);
            }

            // Server-side: ensure price has at most 2 decimals
            if (!model.Price.HasValue)
            {
                ModelState.AddModelError("Price", "Le prix est requis.");
                ViewData["AllCategories"] = await _db.Categories.OrderBy(c => c.Libelle).ToListAsync();
                return View(model);
            }
            var scaledCreate = model.Price.Value * 100m;
            if (decimal.Truncate(scaledCreate) != scaledCreate)
            {
                ModelState.AddModelError("Price", "Le prix ne peut pas avoir plus de 2 décimales.");
                ViewData["AllCategories"] = await _db.Categories.OrderBy(c => c.Libelle).ToListAsync();
                return View(model);
            }
            model.Categories = new List<Categorie>();
            if (selectedCategories != null && selectedCategories.Length > 0)
            {
                var cats = await _db.Categories.Where(c => selectedCategories.Contains(c.Id)).ToListAsync();
                model.Categories = cats;
            }
            _db.Games.Add(model);
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Index), new { section = "games" });
        }

        public async Task<IActionResult> EditGame(int id)
        {
            var g = await _db.Games.Include(x => x.Categories).FirstOrDefaultAsync(x => x.Id == id);
            if (g == null) return NotFound();
            ViewData["AllCategories"] = await _db.Categories.OrderBy(c => c.Libelle).ToListAsync();
            return View(g);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditGame(int id, Game model, int[]? selectedCategories)
        {
            if (id != model.Id) return BadRequest();
            if (!ModelState.IsValid)
            {
                ViewData["AllCategories"] = await _db.Categories.OrderBy(c => c.Libelle).ToListAsync();
                return View(model);
            }

            // Server-side: ensure price has at most 2 decimals
            if (!model.Price.HasValue)
            {
                ModelState.AddModelError("Price", "Le prix est requis.");
                ViewData["AllCategories"] = await _db.Categories.OrderBy(c => c.Libelle).ToListAsync();
                return View(model);
            }
            var scaledEdit = model.Price.Value * 100m;
            if (decimal.Truncate(scaledEdit) != scaledEdit)
            {
                ModelState.AddModelError("Price", "Le prix ne peut pas avoir plus de 2 décimales.");
                ViewData["AllCategories"] = await _db.Categories.OrderBy(c => c.Libelle).ToListAsync();
                return View(model);
            }
            var dbGame = await _db.Games.Include(x => x.Categories).FirstOrDefaultAsync(x => x.Id == id);
            if (dbGame == null) return NotFound();
            dbGame.Name = model.Name;
            dbGame.Description = model.Description;
            dbGame.Price = model.Price;
            dbGame.Categories.Clear();
            if (selectedCategories != null && selectedCategories.Length > 0)
            {
                var cats = await _db.Categories.Where(c => selectedCategories.Contains(c.Id)).ToListAsync();
                foreach (var c in cats) dbGame.Categories.Add(c);
            }
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Index), new { section = "games" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteGame(int id)
        {
            var g = await _db.Games.FindAsync(id);
            if (g == null) return NotFound();
            _db.Games.Remove(g);
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Index), new { section = "games" });
        }
    }
}
