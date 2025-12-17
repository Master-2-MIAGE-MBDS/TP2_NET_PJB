using Gauniv.WebServer.Dtos;
using System.Collections.Generic;

namespace Gauniv.WebServer.Models
{
    public class CategorySelect
    {
        public int Id { get; set; }
        public string Libelle { get; set; } = string.Empty;
    }

    public class GameIndexViewModel
    {
        public List<GameDto> Games { get; set; } = new List<GameDto>();
        public List<CategorySelect> Categories { get; set; } = new List<CategorySelect>();

        public int? SelectedCategory { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public string? Search { get; set; }
        public decimal? GlobalMaxPrice { get; set; }
    }
}