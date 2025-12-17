namespace Gauniv.WebServer.Dtos
{
    public class CategorieDto
    {
        public string Id { get; set; } = string.Empty;
        public string Libelle { get; set; } = string.Empty;
        public List<GameDtoLight> Games { get; set; } = new List<GameDtoLight>();
    }
}