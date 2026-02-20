using System.ComponentModel.DataAnnotations;

namespace NexusM.Models;

public class Rating
{
    [Key] public int Id { get; set; }
    [Required] public string MediaType { get; set; } = ""; // video | track | album | musicvideo | picture | ebook
    public int MediaId { get; set; }
    [Required] public string Username { get; set; } = "";
    public int Stars { get; set; }  // 1–5
    public DateTime DateRated { get; set; } = DateTime.UtcNow;
    public DateTime? DateModified { get; set; }
}
