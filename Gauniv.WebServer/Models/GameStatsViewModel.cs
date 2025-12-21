namespace Gauniv.WebServer.Models
{
    public class GameStatsViewModel
    {
        public int TotalGames { get; set; }
        public List<CategoryCount> GamesPerCategory { get; set; } = new();

        public class CategoryCount
        {
            public int CategoryId { get; set; }
            public string Libelle { get; set; } = string.Empty;
            public int Count { get; set; }
        }
    }
}

