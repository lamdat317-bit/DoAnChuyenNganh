using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DACN.Models
{
    [Table("SportTypes")]
    public class SportType
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required(ErrorMessage = "Tên loại môn thể thao không được để trống")]
        [StringLength(100, ErrorMessage = "Tên không được vượt quá 100 ký tự")]
        [Display(Name = "Tên môn thể thao")]
        public string Name { get; set; } = string.Empty;

        [StringLength(100)]
        [Display(Name = "Icon hiển thị (CSS Class)")]
        public string? IconCss { get; set; } = "bi bi-activity";

        // Navigation Properties
        public virtual ICollection<Facility> Facilities { get; set; } = new List<Facility>();
    }
}
