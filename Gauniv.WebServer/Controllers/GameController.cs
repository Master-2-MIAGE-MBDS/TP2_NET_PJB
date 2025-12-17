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
        private readonly ILogger<GameController> _logger;
        private readonly ApplicationDbContext _db;
        private readonly UserManager<User> _userManager;
        
        public GameController(ILogger<GameController> logger, ApplicationDbContext db, UserManager<User> userManager)
        {
            _logger = logger;
            _db = db;
            _userManager = userManager;
        }
        
        public async Task<IActionResult> Index()
        {
            var games = await _db.Games
                .Select(g => new GameDto
                {
                    Id = g.Id,
                    Name = g.Name,
                    Description = g.Description
                })
                .ToListAsync();

            return View(games);
        }
        
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}