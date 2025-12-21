namespace Gauniv.WebServer.Dtos
{
    public class GameDtoLight
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public decimal Price { get; set; }
        
        public List<string> Categories { get; set; } = new();
    }
}