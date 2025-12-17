using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Gauniv.WebServer.Data
{
    public class Categorie
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Key]
        public int Id { get; set; }
        [Required]
        [MaxLength(50)]
        public string Libelle { get; set; } = string.Empty;
        
        [JsonIgnore]
        public ICollection<Game> Games { get; set; } = new List<Game>();
    }
}