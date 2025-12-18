using Gauniv.WebServer.Dtos;

namespace Gauniv.WebServer.Models
{
    public class CategorySelect
    {
        public int Id { get; set; }
        public string Libelle { get; set; } = string.Empty;
    }

    public class GameIndexViewModel
    {
        public List<GameDto> Games { get; set; } = new();
        public List<CategorySelect> Categories { get; set; } = new();

        public int? SelectedCategory { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public string? Search { get; set; }
        public decimal? GlobalMaxPrice { get; set; }
        public bool OnlyOwned { get; set; }
        public bool IsAuthenticated { get; set; }
    }
}