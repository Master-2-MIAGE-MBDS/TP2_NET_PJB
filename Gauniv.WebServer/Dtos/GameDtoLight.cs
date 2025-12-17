namespace Gauniv.WebServer.Dtos
{
    public class GameDtoLight
    {
        public string Id { get; set; } = string.Empty;
        
        public string Name { get; set; } = string.Empty;
        
        public string? Description { get; set; }
        
        public decimal Price { get; set; }
    }
}