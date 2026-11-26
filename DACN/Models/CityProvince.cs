using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DACN.Models
{
    [Table("CityProvinces")]
    public class CityProvince
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required(ErrorMessage = "Tên tỉnh/thành phố không được để trống")]
        [StringLength(100)]
        [Display(Name = "Tỉnh / Thành phố")]
        public string Name { get; set; } = string.Empty;

        // Navigation Properties
        public virtual ICollection<Facility> Facilities { get; set; } = new List<Facility>();
    }
}
