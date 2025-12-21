namespace Gauniv.WebServer.Dtos
{
    public class CategorieDto
    {
        public int Id { get; set; }
        public string Libelle { get; set; } = string.Empty;
        public List<GameDtoLight> Games { get; set; } = new();
    }
}